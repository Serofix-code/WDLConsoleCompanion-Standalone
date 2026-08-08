using System.Collections.ObjectModel;
using System.Windows;
using WDLConsoleCompanion.Models;
using WDLConsoleCompanion.Services;

namespace WDLConsoleCompanion;

public partial class AdvancedOperativeWindow : Window
{
    private readonly TrainerSession _session;
    private readonly OperativeRecord _operative;
    private readonly ObservableCollection<AdvancedOperativeField> _fields = [];

    internal AdvancedOperativeWindow(TrainerSession session, OperativeRecord operative)
    {
        InitializeComponent(); _session = session; _operative = operative;
        Title = $"High-risk metadata — {operative.FirstName} {operative.Surname}";
        FieldGrid.ItemsSource = _fields;
        Loaded += async (_, _) => await RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        FieldGrid.IsEnabled = false; Footer.Text = "Resolving and validating NPC data…";
        try { var values = await Task.Run(() => _session.ReadAdvancedFields(_operative)); _fields.Clear(); foreach (var value in values) _fields.Add(value); Footer.Text = $"{values.Count} CT-derived fields loaded. All are HIGH RISK."; }
        catch (Exception ex) { Footer.Text = "Stopped safely: " + ex.Message; MessageBox.Show(ex.Message, "Metadata read stopped", MessageBoxButton.OK, MessageBoxImage.Warning); }
        finally { FieldGrid.IsEnabled = true; }
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshAsync();
    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        if (FieldGrid.SelectedItem is not AdvancedOperativeField field) { MessageBox.Show("Select one field first."); return; }
        FieldGrid.CommitEdit(System.Windows.Controls.DataGridEditingUnit.Cell, true);
        if (MessageBox.Show($"Write {field.DisplayName} to {field.Value}?\n\nHIGH RISK: an accepted value can still crash the game or damage the save.", "Confirm high-risk write", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        try { Footer.Text = await Task.Run(() => _session.SaveAdvancedField(_operative, field)); MessageBox.Show(Footer.Text, "Field saved", MessageBoxButton.OK, MessageBoxImage.Information); await RefreshAsync(); }
        catch (Exception ex) { Footer.Text = "No change retained: " + ex.Message; MessageBox.Show(ex.Message, "Field not saved", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }
}
