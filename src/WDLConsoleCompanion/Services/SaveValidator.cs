using System.Diagnostics;
using System.Security.Cryptography;

namespace WDLConsoleCompanion.Services;

public sealed record ValidationResult(bool IsValid, string Message, string? Sha256 = null);

public sealed class SaveValidator
{
    public bool IsGameRunning() => Process.GetProcessesByName("WatchDogsLegion").Length > 0;

    public async Task<ValidationResult> ValidateAsync(string path, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(path)) return new(false, "Save file does not exist.");
        var info = new FileInfo(path);
        if (info.Length < 64) return new(false, "Save file is unexpectedly small.");
        try
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 131072, true);
            var sample = new byte[Math.Min(4096, (int)info.Length)];
            var read = await stream.ReadAsync(sample, cancellationToken);
            if (read == 0 || sample.Take(read).All(b => b == 0)) return new(false, "Save data is empty or all zeroes.");
            stream.Position = 0;
            var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));
            return new(true, "Basic integrity checks passed.", hash);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return new(false, ex.Message); }
    }
}
