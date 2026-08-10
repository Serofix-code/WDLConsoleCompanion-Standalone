using System.Text.Json;

namespace WDLConsoleCompanion.Services;

internal sealed class HotkeyBinding
{
    public string Command { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public string Key { get; set; } = "None";
}

internal sealed class HotkeySettings
{
    private static readonly (string Command, string Label)[] Commands =
    [
        ("godmode", "God Mode"), ("notrace", "No Trace"), ("infammo", "Infinite Ammo"),
        ("noreload", "No Reload"), ("norecoil", "No Recoil"), ("fastsearch", "Fast Search"),
        ("hackcooldown", "Instant Hacker Cooldowns"), ("freezehack", "Freeze Hack Timer"), ("dronerange", "Maximum Drone Range"),
        ("dronehealth", "Infinite Drone Health"), ("onehitkill", "One Hit Kill")
    ];

    internal static IReadOnlyList<string> AvailableKeys { get; } = ["None", "F1", "F2", "F3", "F4", "F5", "F6", "F7", "F8", "F9", "F10", "F11", "F12"];
    internal List<HotkeyBinding> Bindings { get; } = [];

    internal static HotkeySettings Load(string path)
    {
        Dictionary<string, string> saved = [];
        try
        {
            if (File.Exists(path)) saved = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path), new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];
        }
        catch { }
        var settings = new HotkeySettings();
        foreach ((string command, string label) in Commands)
        {
            string key = saved.TryGetValue(command, out string? value) && AvailableKeys.Contains(value, StringComparer.OrdinalIgnoreCase)
                ? (value.Equals("None", StringComparison.OrdinalIgnoreCase) ? "None" : value.ToUpperInvariant()) : "None";
            settings.Bindings.Add(new HotkeyBinding { Command = command, DisplayName = label, Key = key });
        }
        return settings;
    }

    internal void Save(string path)
    {
        string[] duplicates = Bindings.Where(binding => binding.Key != "None").GroupBy(binding => binding.Key, StringComparer.OrdinalIgnoreCase).Where(group => group.Count() > 1).Select(group => group.Key).ToArray();
        if (duplicates.Length > 0) throw new InvalidOperationException("Each shortcut must be unique. Duplicates: " + string.Join(", ", duplicates));
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? AppContext.BaseDirectory);
        File.WriteAllText(path, JsonSerializer.Serialize(Bindings.ToDictionary(binding => binding.Command, binding => binding.Key), new JsonSerializerOptions { WriteIndented = true }));
    }
}
