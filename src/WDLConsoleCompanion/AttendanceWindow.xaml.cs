using System.Collections.ObjectModel;
using System.Windows;
using WDLConsoleCompanion.Models;
using WDLConsoleCompanion.Services;

namespace WDLConsoleCompanion;
public partial class AttendanceWindow : Window
{
    private readonly TrainerSession _session; private readonly OperativeRecord _operative; private readonly ObservableCollection<OperativeAttendance> _rows = [];
    internal AttendanceWindow(TrainerSession session, OperativeRecord operative) { InitializeComponent(); _session = session; _operative = operative; AttendanceGrid.ItemsSource = _rows; Loaded += async (_, _) => await ReloadAsync(); }
    private async Task ReloadAsync() { try { var rows = await Task.Run(() => _session.ReadAttendances(_operative)); _rows.Clear(); foreach (var row in rows) _rows.Add(row); Footer.Text = $"{rows.Count} editable attendance schedules loaded."; } catch (Exception ex) { Footer.Text = _session.ReportError("WDL-SCHEDULE-001", "Contract schedules could not be loaded", ex); } }
    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        AttendanceGrid.CommitEdit(System.Windows.Controls.DataGridEditingUnit.Cell, true); AttendanceGrid.CommitEdit(System.Windows.Controls.DataGridEditingUnit.Row, true);
        if (AttendanceGrid.SelectedItem is not OperativeAttendance row) { MessageBox.Show("Select one attendance schedule first."); return; }
        if (MessageBox.Show("Contract schedules affect NPC routines and can damage relationships or save data. Continue?", "SUPER RISKY contract edit", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        try { Footer.Text = await Task.Run(() => _session.SaveAttendance(_operative, row)); await ReloadAsync(); } catch (Exception ex) { Footer.Text = _session.ReportError("WDL-SCHEDULE-002", "Contract schedule was not changed", ex); MessageBox.Show(Footer.Text, "Contract not changed", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }
}
