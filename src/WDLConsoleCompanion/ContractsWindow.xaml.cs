using System.Windows;
using WDLConsoleCompanion.Models;
using WDLConsoleCompanion.Services;
namespace WDLConsoleCompanion;
public partial class ContractsWindow : Window
{
    private readonly TrainerSession _session; private readonly OperativeRecord _operative;
    internal ContractsWindow(TrainerSession session, OperativeRecord operative) { InitializeComponent(); _session = session; _operative = operative; Title = $"Contacts — {operative.FirstName} {operative.Surname}"; Loaded += async (_, _) => await LoadAsync(); }
    private async Task LoadAsync() { try { var rows = await Task.Run(() => _session.ReadContracts(_operative)); ContractGrid.ItemsSource = rows; Footer.Text = $"{rows.Count} contracts loaded and resolved against 596 known contract types."; } catch (Exception ex) { Footer.Text = _session.ReportError("WDL-CONTRACT-001", "Contracts could not be loaded", ex); MessageBox.Show(Footer.Text, "Contracts not loaded", MessageBoxButton.OK, MessageBoxImage.Warning); } }
    private void EditSchedules_Click(object sender, RoutedEventArgs e) => new AttendanceWindow(_session, _operative) { Owner = this }.Show();
}
