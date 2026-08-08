using System.Collections.ObjectModel;
using System.Windows;
using WDLConsoleCompanion.Models;
using WDLConsoleCompanion.Services;

namespace WDLConsoleCompanion;

public partial class OperativesWindow : Window
{
    private readonly TrainerSession _session;
    private readonly ObservableCollection<OperativeRecord> _rows = [];
    internal OperativesWindow(TrainerSession session)
    {
        InitializeComponent();
        _session = session;
        AvailabilityColumn.ItemsSource = OperativeChoices.Availabilities;
        OriginColumn.ItemsSource = OperativeChoices.Origins;
        RosterGrid.ItemsSource = _rows;
    }

    internal async Task RefreshAsync()
    {
        SetBusy(true, "Reading validated roster and census chains…");
        try
        {
            var records = await Task.Run(_session.ReadRoster);
            _rows.Clear(); foreach (var record in records) _rows.Add(record);
            Footer.Text = $"{records.Count} operatives · refreshed {DateTime.Now:T}. Appearance codes are CT-compatible 24-byte values; switch away from and back to an edited operative to refresh its rendered model.";
        }
        catch (Exception ex) { Footer.Text = "Read stopped safely: " + ex.Message; MessageBox.Show(ex.Message, "Roster read stopped", MessageBoxButton.OK, MessageBoxImage.Warning); }
        finally { SetBusy(false); }
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshAsync();
    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        if (RosterGrid.SelectedItem is not OperativeRecord row) { MessageBox.Show("Select an operative first."); return; }
        RosterGrid.CommitEdit(System.Windows.Controls.DataGridEditingUnit.Cell, true);
        RosterGrid.CommitEdit(System.Windows.Controls.DataGridEditingUnit.Row, true);
        SetBusy(true, "Validating and writing operative changes…");
        try
        {
            string result = await Task.Run(() => _session.UpdateNames(row));
            await RefreshAsync();
            MessageBox.Show(result + "\n\nClose and reopen the in-game Team menu before selecting an operative whose availability changed.",
                "Operative changes saved", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "No changes written", MessageBoxButton.OK, MessageBoxImage.Warning); }
        finally { SetBusy(false); }
    }
    private async void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (RosterGrid.SelectedItem is not OperativeRecord row) { MessageBox.Show("Select an operative first."); return; }
        if (MessageBox.Show($"Remove {row.FirstName} {row.Surname} ({row.IdHex}) from the roster?\n\nThis compacts the roster pointer array and cannot be undone by this app after the game saves.",
            "Confirm roster removal", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        SetBusy(true, "Re-validating and compacting roster…");
        try { await Task.Run(() => _session.RemoveOperative(row)); await RefreshAsync(); }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Removal stopped", MessageBoxButton.OK, MessageBoxImage.Warning); }
        finally { SetBusy(false); }
    }
    private void Perks_Click(object sender, RoutedEventArgs e)
    {
        if (RosterGrid.SelectedItem is not OperativeRecord row) { MessageBox.Show("Select an operative first."); return; }
        new PerkWindow(_session, row) { Owner = this }.ShowDialog();
    }
    private void Advanced_Click(object sender, RoutedEventArgs e)
    {
        if (RosterGrid.SelectedItem is not OperativeRecord row) { MessageBox.Show("Select an operative first."); return; }
        new AdvancedOperativeWindow(_session, row) { Owner = this }.ShowDialog();
    }
    private void SetBusy(bool busy, string? text = null) { RosterGrid.IsEnabled = !busy; if (text is not null) Footer.Text = text; }
}
