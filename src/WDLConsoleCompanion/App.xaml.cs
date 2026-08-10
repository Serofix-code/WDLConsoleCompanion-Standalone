using Microsoft.Win32;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using WDLConsoleCompanion.Services;

namespace WDLConsoleCompanion;

public partial class App : System.Windows.Application
{
    private string _themePreference = "System";
    internal string ThemePreference => _themePreference;
    internal AppSettings Settings { get; private set; } = new();
    internal string SettingsPath => Path.Combine(AppContext.BaseDirectory, "config", "settings.json");

    protected override void OnStartup(StartupEventArgs e)
    {
        Settings = AppSettings.Load(SettingsPath);
        _themePreference = Settings.Theme;
        ApplyTheme(_themePreference);
        base.OnStartup(e);
    }

    internal void SetTheme(string preference)
    {
        if (preference is not ("System" or "Dark" or "Light")) preference = "System";
        _themePreference = preference;
        Settings.Theme = preference;
        ApplyTheme(preference);
        SaveSettings();
    }

    internal void SaveSettings() { try { Settings.Save(SettingsPath); _themePreference = Settings.Theme; ApplyTheme(Settings.Theme); } catch { } }

    private void ApplyTheme(string preference)
    {
        bool dark = preference == "Dark" || preference == "System" && SystemUsesDarkApps();
        var colors = dark
            ? new Dictionary<string, string> { ["BackgroundBrush"]="#202327", ["PanelBrush"]="#2B2F35", ["BorderBrush"]="#4A5059", ["TextBrush"]="#FFFFFF", ["MutedBrush"]="#C0C5CC", ["ButtonBrush"]="#343941", ["InputBrush"]="#3A3F46", ["InputTextBrush"]="#FFFFFF", ["RowBrush"]="#292D33", ["AlternateRowBrush"]="#30353C", ["HeaderBrush"]="#343941", ["HeaderTextBrush"]="#FFFFFF", ["GridLineBrush"]="#464C55" }
            : new Dictionary<string, string> { ["BackgroundBrush"]="#E4E7EB", ["PanelBrush"]="#F3F4F6", ["BorderBrush"]="#B8BEC7", ["TextBrush"]="#1F2937", ["MutedBrush"]="#596273", ["ButtonBrush"]="#D9DEE5", ["InputBrush"]="#FFFFFF", ["InputTextBrush"]="#111827", ["RowBrush"]="#F7F8FA", ["AlternateRowBrush"]="#ECEFF3", ["HeaderBrush"]="#D6DBE2", ["HeaderTextBrush"]="#202733", ["GridLineBrush"]="#CDD2D9" };
        foreach ((string key, string value) in colors)
            Resources[key] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(value));
    }

    private static bool SystemUsesDarkApps()
    {
        try { return Convert.ToInt32(Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize")?.GetValue("AppsUseLightTheme", 0)) == 0; }
        catch { return true; }
    }

    private static string LoadThemePreference()
    {
        try
        {
            string path = Path.Combine(AppContext.BaseDirectory, "config", "theme.json");
            if (!File.Exists(path)) return "Dark";
            using JsonDocument json = JsonDocument.Parse(File.ReadAllText(path));
            string? value = json.RootElement.TryGetProperty("theme", out JsonElement theme) ? theme.GetString() : null;
            return value is "System" or "Dark" or "Light" ? value : "Dark";
        }
        catch { return "Dark"; }
    }
}
