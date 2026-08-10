using System.Windows;
using System.Windows.Controls;
using WDLConsoleCompanion.Services;

namespace WDLConsoleCompanion;

public partial class SettingsWindow : Window
{
    private readonly AppSettings _settings;
    internal event Action? SettingsSaved;
    internal SettingsWindow(AppSettings settings)
    {
        InitializeComponent(); _settings = settings;
        ThemeBox.SelectedIndex = settings.Theme switch { "System" => 0, "Light" => 2, _ => 1 };
        AutoInjectBox.IsChecked = settings.AutoInject; CleanupBox.IsChecked = settings.DisableCheatsOnExit;
        DelayBox.Text = settings.AutoInjectDelaySeconds.ToString(); RefreshBox.Text = settings.CoordinateRefreshMilliseconds.ToString(); MemoryBox.Text = settings.CompanionMemoryTrimMb.ToString();
    }
    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();
    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(DelayBox.Text, out int delay) || delay is < 3 or > 60 || !int.TryParse(RefreshBox.Text, out int refresh) || refresh is < 200 or > 5000 || !int.TryParse(MemoryBox.Text, out int memory) || memory < 0)
        { MessageBox.Show("Use a 3–60 second delay, 200–5000 ms coordinate refresh, and a non-negative RAM value.", "Invalid settings", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
        _settings.Theme = ThemeBox.SelectedItem is ComboBoxItem { Tag: "Dark" } ? "Dark" : ThemeBox.SelectedIndex == 0 ? "System" : "Light";
        _settings.AutoInject = AutoInjectBox.IsChecked == true; _settings.DisableCheatsOnExit = CleanupBox.IsChecked == true;
        _settings.AutoInjectDelaySeconds = delay; _settings.CoordinateRefreshMilliseconds = refresh; _settings.CompanionMemoryTrimMb = memory;
        ((App)Application.Current).SaveSettings(); SettingsSaved?.Invoke(); Close();
    }
}
