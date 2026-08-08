using System.Text.RegularExpressions;

namespace WDLConsoleCompanion.Services;

public sealed record SessionModeStatus(string Label, bool LauncherOffline, string Detail);

public sealed class SessionModeDetector
{
    public SessionModeStatus Detect()
    {
        if (ReadUbisoftOfflineFlag()) return new("Ubisoft Connect offline", true, "Ubisoft Connect settings report offline mode.");
        if (ReadSteamOfflineFlag()) return new("Steam offline", true, "The most-recent Steam profile requests offline mode.");
        return new("Launchers online - game mode unknown", false,
            "Steam and Ubisoft Connect are online. This does not reveal whether Legion is in campaign or Online mode.");
    }

    private static bool ReadUbisoftOfflineFlag()
    {
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Ubisoft Game Launcher", "settings.yaml");
        if (!File.Exists(path)) return false;
        try { return File.ReadLines(path).Any(line => Regex.IsMatch(line, @"^\s*offline\s*:\s*true\s*$", RegexOptions.IgnoreCase)); }
        catch (IOException) { return false; }
    }

    private static bool ReadSteamOfflineFlag()
    {
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam", "config", "loginusers.vdf");
        if (!File.Exists(path)) return false;
        try
        {
            var blocks = Regex.Matches(File.ReadAllText(path), @"\{(?<body>[^{}]+)\}", RegexOptions.Singleline);
            return blocks.Cast<Match>().Any(match =>
                Regex.IsMatch(match.Groups["body"].Value, "\\\"MostRecent\\\"\\s+\\\"1\\\"") &&
                Regex.IsMatch(match.Groups["body"].Value, "\\\"WantsOfflineMode\\\"\\s+\\\"1\\\""));
        }
        catch (IOException) { return false; }
    }
}
