using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
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
            Footer.Text = $"{records.Count} operatives · refreshed {DateTime.Now:T}. Use the focused editor buttons for risky fields; switch away from and back to an operative after appearance changes.";
        }
        catch (Exception ex) { Footer.Text = _session.ReportError("WDL-ROSTER-001", "Roster read stopped", ex); MessageBox.Show(Footer.Text, "Roster read stopped", MessageBoxButton.OK, MessageBoxImage.Warning); }
        finally { SetBusy(false); }
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshAsync();
    private void RosterGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        DependencyObject? source = e.OriginalSource as DependencyObject;
        while (source is not null and not DataGridCell) source = VisualTreeHelper.GetParent(source);
        if (source is not DataGridCell cell || cell.Column.DisplayIndex is not (1 or 2)) return;
        RosterGrid.CurrentCell = new DataGridCellInfo(cell);
        RosterGrid.SelectedItem = cell.DataContext;
        RosterGrid.BeginEdit();
        e.Handled = true;
        Footer.Text = $"Editing {cell.Column.Header}. Enter an exact game name, press Enter, then click Save changes.";
    }
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
        catch (Exception ex) { Footer.Text = _session.ReportError("WDL-ROSTER-002", "Operative changes were not written", ex); MessageBox.Show(Footer.Text, "No changes written", MessageBoxButton.OK, MessageBoxImage.Warning); }
        finally { SetBusy(false); }
    }
    private async void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (RosterGrid.SelectedItem is not OperativeRecord row) { MessageBox.Show("Select an operative first."); return; }
        if (MessageBox.Show($"Remove {row.FirstName} {row.Surname} ({row.IdHex}) from the roster?\n\nThis compacts the roster pointer array and cannot be undone by this app after the game saves.",
            "Confirm roster removal", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        SetBusy(true, "Re-validating and compacting roster…");
        try { await Task.Run(() => _session.RemoveOperative(row)); await RefreshAsync(); }
        catch (Exception ex) { Footer.Text = _session.ReportError("WDL-ROSTER-003", "Operative removal stopped", ex); MessageBox.Show(Footer.Text, "Removal stopped", MessageBoxButton.OK, MessageBoxImage.Warning); }
        finally { SetBusy(false); }
    }
    private void Perks_Click(object sender, RoutedEventArgs e)
    {
        if (RosterGrid.SelectedItem is not OperativeRecord row) { MessageBox.Show("Select an operative first."); return; }
        new PerkWindow(_session, row) { Owner = this }.Show();
    }
    private void Advanced_Click(object sender, RoutedEventArgs e)
    {
        if (RosterGrid.SelectedItem is not OperativeRecord row) { MessageBox.Show("Select an operative first."); return; }
        new AdvancedOperativeWindow(_session, row) { Owner = this }.Show();
    }
    private void Statistics_Click(object sender, RoutedEventArgs e)
    {
        if (RosterGrid.SelectedItem is not OperativeRecord row) { MessageBox.Show("Select an operative first."); return; }
        try { new StatisticsWindow(_session, row) { Owner = this }.Show(); }
        catch (Exception ex) { string error = _session.ReportError("WDL-STATS-000", "Statistics editor could not be opened", ex); MessageBox.Show(error, "Statistics editor stopped", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }
    private void Events_Click(object sender, RoutedEventArgs e)
    {
        if (RosterGrid.SelectedItem is not OperativeRecord row) { MessageBox.Show("Select an operative first."); return; }
        try { new EventsWindow(_session, row) { Owner = this }.Show(); }
        catch (Exception ex) { string error = _session.ReportError("WDL-EVENT-000", "Recent Events editor could not be opened", ex); MessageBox.Show(error, "Recent Events stopped", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }
    private void Appearance_Click(object sender, RoutedEventArgs e) { if (RosterGrid.SelectedItem is OperativeRecord row) new AppearanceWindow(_session, row) { Owner = this }.Show(); else MessageBox.Show("Select an operative first."); }
    private void Contracts_Click(object sender, RoutedEventArgs e) { if (RosterGrid.SelectedItem is OperativeRecord row) new ContractsWindow(_session, row) { Owner = this }.Show(); else MessageBox.Show("Select an operative first."); }
    private void SetBusy(bool busy, string? text = null) { RosterGrid.IsEnabled = !busy; if (text is not null) Footer.Text = text; }
}
