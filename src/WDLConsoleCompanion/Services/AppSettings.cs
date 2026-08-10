using System.Text.Json;

namespace WDLConsoleCompanion.Services;

internal sealed class AppSettings
{
    public string Theme { get; set; } = "Dark";
    public bool AutoInject { get; set; } = true;
    public int AutoInjectDelaySeconds { get; set; } = 12;
    public bool DisableCheatsOnExit { get; set; } = true;
    public int CoordinateRefreshMilliseconds { get; set; } = 500;
    public int CompanionMemoryTrimMb { get; set; }

    internal void Normalize()
    {
        if (Theme is not ("System" or "Dark" or "Light")) Theme = "Dark";
        AutoInjectDelaySeconds = Math.Clamp(AutoInjectDelaySeconds, 3, 60);
        CoordinateRefreshMilliseconds = Math.Clamp(CoordinateRefreshMilliseconds, 200, 5000);
        CompanionMemoryTrimMb = CompanionMemoryTrimMb == 0 ? 0 : Math.Clamp(CompanionMemoryTrimMb, 128, 4096);
    }

    internal static AppSettings Load(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                AppSettings result = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), JsonOptions()) ?? new();
                result.Normalize(); return result;
            }
        }
        catch { }
        return new AppSettings();
    }

    internal void Save(string path)
    {
        Normalize(); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(this, JsonOptions()));
    }

    private static JsonSerializerOptions JsonOptions() => new() { PropertyNameCaseInsensitive = true, WriteIndented = true };
}
