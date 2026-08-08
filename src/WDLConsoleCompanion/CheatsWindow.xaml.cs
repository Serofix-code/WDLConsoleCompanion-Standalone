using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using WDLConsoleCompanion.Services;

namespace WDLConsoleCompanion;

public partial class CheatsWindow : Window
{
    private readonly TrainerSession _session;
    private readonly ObservableCollection<CheatRow> _rows =
    [
        new("godmode", "God Mode", "Infinite player health"), new("notrace", "No Trace", "No wanted level and stealth"),
        new("infammo", "Infinite Ammo", "Keeps ammunition at 999"), new("noreload", "No Reload", "Skips the reload requirement"),
        new("norecoil", "No Recoil", "Suppresses weapon recoil"), new("fastsearch", "Fast Search", "Ends pursuit searches faster"),
        new("noclip", "Noclip / Fly", "Requires Legion ScriptHook; no verified CT collision signature is available", "SUPER RISKY", false)
    ];
    internal CheatsWindow(TrainerSession session) { InitializeComponent(); _session = session; CheatItems.ItemsSource = _rows; Refresh(); }
    internal void Refresh() { foreach (CheatRow row in _rows) row.IsOn = _session.IsCheatActive(row.Name); Footer.Text = _session.IsAttached ? $"Attached to PID {_session.ProcessId}" : "Not attached"; CheatItems.IsEnabled = _session.IsAttached; }
    private async void Toggle_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string name }) return;
        CheatItems.IsEnabled = false;
        try { Footer.Text = await Task.Run(() => _session.ToggleCheat(name, null)); }
        catch (Exception ex) { Footer.Text = "Stopped safely: " + ex.Message; MessageBox.Show(ex.Message, "Cheat not changed", MessageBoxButton.OK, MessageBoxImage.Warning); }
        finally { Refresh(); }
    }
    internal sealed class CheatRow(string name, string displayName, string description, string risk = "STANDARD", bool isAvailable = true) : System.ComponentModel.INotifyPropertyChanged
    {
        private bool _isOn;
        public string Name { get; } = name; public string DisplayName { get; } = displayName; public string Description { get; } = description; public string Risk { get; } = risk; public bool IsAvailable { get; } = isAvailable;
        public bool IsOn { get => _isOn; set { if (_isOn == value) return; _isOn = value; PropertyChanged?.Invoke(this, new(nameof(IsOn))); } }
        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    }
}
