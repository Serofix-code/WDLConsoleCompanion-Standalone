using System.Collections.ObjectModel;
using System.Windows;
using WDLConsoleCompanion.Services;

namespace WDLConsoleCompanion;

public partial class MemoryScannerWindow : Window
{
    private readonly TrainerSession _session;
    private readonly MemoryScanner _scanner;
    private readonly ObservableCollection<MemoryScanResult> _results = [];
    private CancellationTokenSource? _scanCancellation;
    private bool _busy;

    internal MemoryScannerWindow(TrainerSession session)
    {
        InitializeComponent();
        _session = session;
        _scanner = session.CreateMemoryScanner();
        TypeBox.ItemsSource = Enum.GetValues<MemoryScanValueType>();
        TypeBox.SelectedItem = MemoryScanValueType.Int32;
        ScopeBox.ItemsSource = Enum.GetValues<MemoryScanScope>();
        ScopeBox.SelectedItem = MemoryScanScope.WritableMemory;
        ResultsGrid.ItemsSource = _results;
        Closed += (_, _) => _scanCancellation?.Cancel();
    }

    private async void FirstExact_Click(object sender, RoutedEventArgs e) => await FirstScanAsync(MemoryScanComparison.Exact);
    private async void FirstUnknown_Click(object sender, RoutedEventArgs e) => await FirstScanAsync(MemoryScanComparison.Unknown);
    private async void NextExact_Click(object sender, RoutedEventArgs e) => await NextScanAsync(MemoryScanComparison.Exact);
    private async void Changed_Click(object sender, RoutedEventArgs e) => await NextScanAsync(MemoryScanComparison.Changed);
    private async void Unchanged_Click(object sender, RoutedEventArgs e) => await NextScanAsync(MemoryScanComparison.Unchanged);
    private async void Increased_Click(object sender, RoutedEventArgs e) => await NextScanAsync(MemoryScanComparison.Increased);
    private async void Decreased_Click(object sender, RoutedEventArgs e) => await NextScanAsync(MemoryScanComparison.Decreased);

    private async Task FirstScanAsync(MemoryScanComparison comparison)
    {
        if (TypeBox.SelectedItem is not MemoryScanValueType type || ScopeBox.SelectedItem is not MemoryScanScope scope) return;
        string input = ValueBox.Text;
        await RunScanAsync(() => _scanner.FirstScan(type, scope, comparison, input, _scanCancellation!.Token, ReportProgress));
    }

    private async Task NextScanAsync(MemoryScanComparison comparison)
    {
        string input = ValueBox.Text;
        await RunScanAsync(() => _scanner.NextScan(comparison, input, _scanCancellation!.Token, ReportProgress));
    }

    private async Task RunScanAsync(Func<MemoryScanSummary> scan)
    {
        if (_busy) return;
        _busy = true;
        _scanCancellation = new CancellationTokenSource();
        CancelButton.IsEnabled = true;
        ExactFirstButton.IsEnabled = UnknownFirstButton.IsEnabled = false;
        try
        {
            StatusText.Text = "Scanning…";
            MemoryScanSummary summary = await Task.Run(scan);
            RefreshResults();
            string limited = summary.Truncated ? " Result storage was capped; narrow with an exact first value when possible." : "";
            StatusText.Text = $"{summary.Results:N0} candidates; scanned {summary.BytesScanned / 1024 / 1024:N0} MB in {summary.Elapsed.TotalSeconds:0.0}s. Showing the first {_results.Count:N0}.{limited}";
            _session.ReportMemoryScan($"Memory scan completed: {summary.Results:N0} candidates after {summary.Elapsed.TotalSeconds:0.0}s.{(summary.Truncated ? " Results capped." : "")}");
        }
        catch (OperationCanceledException) { StatusText.Text = "Scan cancelled; previous result set retained where possible."; }
        catch (Exception ex) { StatusText.Text = _session.ReportError("WDL-SCAN-001", "Memory scan failed", ex); }
        finally
        {
            _busy = false;
            CancelButton.IsEnabled = false;
            ExactFirstButton.IsEnabled = UnknownFirstButton.IsEnabled = true;
            _scanCancellation.Dispose();
            _scanCancellation = null;
        }
    }

    private void ReportProgress(string value) => Dispatcher.BeginInvoke(() => StatusText.Text = value);
    private void RefreshResults() { _results.Clear(); foreach (MemoryScanResult result in _scanner.Preview()) _results.Add(result); }
    private void Cancel_Click(object sender, RoutedEventArgs e) => _scanCancellation?.Cancel();
    private void NewScan_Click(object sender, RoutedEventArgs e) { if (_busy) return; _results.Clear(); StatusText.Text = "Choose a type, scope, and initial Exact or Unknown scan."; }

    private void Write_Click(object sender, RoutedEventArgs e)
    {
        if (ResultsGrid.SelectedItem is not MemoryScanResult selected) { StatusText.Text = "Select a scan result first."; return; }
        if (MessageBox.Show($"Write '{WriteValueBox.Text}' to {selected.AddressText}?\n\nA scan match does not prove what an address controls. An incorrect write can crash the game or corrupt state.", "SUPER RISKY memory write", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        try { StatusText.Text = _scanner.Write(selected.Address, WriteValueBox.Text); _session.ReportMemoryScan("SUPER RISKY: " + StatusText.Text); RefreshResults(); }
        catch (Exception ex) { StatusText.Text = _session.ReportError("WDL-SCAN-002", "Memory write failed", ex); }
    }
}
