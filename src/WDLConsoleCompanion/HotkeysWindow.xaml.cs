using System.Windows;
using WDLConsoleCompanion.Services;

namespace WDLConsoleCompanion;

public partial class HotkeysWindow : Window
{
    private readonly HotkeySettings _settings;
    private readonly string _path;
    private readonly Action _saved;
    internal HotkeysWindow(HotkeySettings settings, string path, Action saved)
    {
        InitializeComponent(); _settings = settings; _path = path; _saved = saved;
        BindingGrid.ItemsSource = settings.Bindings; KeyColumn.ItemsSource = HotkeySettings.AvailableKeys;
    }
    private void Save_Click(object sender, RoutedEventArgs e)
    {
        BindingGrid.CommitEdit();
        try { _settings.Save(_path); _saved(); Footer.Text = "Shortcuts saved and registered."; }
        catch (Exception ex) { Footer.Text = ex.Message; MessageBox.Show(ex.Message, "Shortcuts not saved", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }
}
