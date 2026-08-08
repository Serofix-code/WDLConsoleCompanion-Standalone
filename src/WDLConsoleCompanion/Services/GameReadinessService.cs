using System.Diagnostics;

namespace WDLConsoleCompanion.Services;

public sealed record GameReadiness(bool ProcessDetected, bool EngineReady, int? ProcessId, string Message);

public sealed class GameReadinessService
{
    private static readonly string[] EngineModules =
    [
        "DuniaDemo_clang_64_dx12.dll", "DuniaDemo_clang_64_dx11.dll",
        "DuniaDemo_clang_64_dx12_plus.dll", "DuniaDemo_clang_64_dx11_plus.dll"
    ];

    public GameReadiness Detect()
    {
        var processes = Process.GetProcessesByName("WatchDogsLegion");
        if (processes.Length == 0) return new(false, false, null, "Waiting for the real WatchDogsLegion.exe...");
        foreach (var process in processes)
        {
            try
            {
                var path = process.MainModule?.FileName;
                if (!string.Equals(Path.GetFileName(path), "WatchDogsLegion.exe", StringComparison.OrdinalIgnoreCase)) continue;
                var modules = process.Modules.Cast<ProcessModule>().Select(module => module.ModuleName).ToHashSet(StringComparer.OrdinalIgnoreCase);
                if (EngineModules.Any(modules.Contains)) return new(true, true, process.Id, "Game engine ready - NOT INJECTED");
                return new(true, false, process.Id, "WatchDogsLegion.exe detected; waiting for the Dunia engine module...");
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                return new(true, false, process.Id, "Game process detected; waiting for module access...");
            }
            finally { process.Dispose(); }
        }
        return new(true, false, null, "Launcher/stub detected; waiting for the real game executable...");
    }
}
