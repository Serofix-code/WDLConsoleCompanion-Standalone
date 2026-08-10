using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using WDLConsoleCompanion.Models;
using WDLConsoleCompanion.Services;

namespace WDLConsoleCompanion;

public partial class StatisticsWindow : Window
{
    private readonly TrainerSession _session;
    private readonly OperativeRecord _operative;
    private readonly ObservableCollection<AdvancedOperativeField> _fields = [];

    internal StatisticsWindow(TrainerSession session, OperativeRecord operative)
    {
        InitializeComponent();
        _session = session;
        _operative = operative;
        Title = $"Statistics — {operative.FirstName} {operative.Surname}";
        StatusBox.ItemsSource = new Dictionary<byte, string> { [0] = "Available", [1] = "Dead", [2] = "Injured", [3] = "Arrested", [4] = "Pending Deportation" };
        FieldGrid.ItemsSource = _fields;
        Loaded += async (_, _) => await LoadAsync();
    }

    private async Task LoadAsync()
    {
        Footer.Text = "Reading core and demographic statistics…";
        try
        {
            var loaded = await Task.Run(() => (_session.ReadStatistics(_operative), _session.ReadAdvancedFields(_operative)));
            OperativeStatistics statistics = loaded.Item1;
            AgeBox.Text = statistics.Age.ToString();
            IncomeBox.Text = statistics.Income.ToString();
            StatusBox.SelectedValue = statistics.Status;
            CountryBox.Text = statistics.CountryTag;
            BirthplaceBox.Text = statistics.DetailedBirthplace;
            PrimaryBiographyBox.Text = statistics.PrimaryBiography;
            _fields.Clear();
            foreach (AdvancedOperativeField field in loaded.Item2) _fields.Add(field);
            Footer.Text = $"Core statistics and {_fields.Count} named demographic statistics loaded. Select a readable value or edit its exact raw hash.";
        }
        catch (Exception ex) { Footer.Text = _session.ReportError("WDL-STATS-001", "Statistics could not be loaded", ex); }
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await LoadAsync();

    private async void SaveCore_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(AgeBox.Text, out int age) || !int.TryParse(IncomeBox.Text, out int income) || StatusBox.SelectedValue is not byte status) { MessageBox.Show("Enter a numeric age/income and choose a status."); return; }
        if (MessageBox.Show("Age, income, and NPC status are HIGH RISK. Back up your save before continuing.", "Confirm statistics write", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        try { Footer.Text = await Task.Run(() => _session.SaveStatistics(_operative, new OperativeStatistics { Age = age, Income = income, Status = status })); await LoadAsync(); }
        catch (Exception ex) { Footer.Text = _session.ReportError("WDL-STATS-002", "Core statistics were not saved", ex); MessageBox.Show(Footer.Text, "Statistics not saved", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    private async void SaveMetadata_Click(object sender, RoutedEventArgs e)
    {
        if (FieldGrid.SelectedItem is not AdvancedOperativeField field) { MessageBox.Show("Select one demographic statistic first."); return; }
        FieldGrid.CommitEdit(DataGridEditingUnit.Cell, true);
        if (!field.IsAvailable) { MessageBox.Show("That statistic's pointer is unavailable for this operative."); return; }
        if (MessageBox.Show($"Write {field.DisplayName} as '{field.ResolvedName}'?\n\nHIGH RISK: even a known value can produce an incompatible operative combination.", "Confirm demographic statistic", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        try { Footer.Text = await Task.Run(() => _session.SaveAdvancedField(_operative, field)); await LoadAsync(); }
        catch (Exception ex) { Footer.Text = _session.ReportError("WDL-STATS-003", "Demographic statistic was not saved", ex); MessageBox.Show(Footer.Text, "Statistic not saved", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    private void Events_Click(object sender, RoutedEventArgs e)
    {
        try { new EventsWindow(_session, _operative) { Owner = this }.Show(); }
        catch (Exception ex) { string error = _session.ReportError("WDL-EVENT-000", "Recent Events editor could not be opened", ex); MessageBox.Show(error, "Recent Events stopped", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }
}
