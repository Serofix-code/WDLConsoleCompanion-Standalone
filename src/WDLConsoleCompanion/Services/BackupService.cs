using System.Security.Cryptography;
using System.Text.Json;

namespace WDLConsoleCompanion.Services;

public sealed record BackupManifest(string SourceFileName, long Length, string Sha256, DateTimeOffset CreatedUtc);

public sealed class BackupService
{
    private readonly SaveValidator _validator;
    public BackupService(SaveValidator validator) => _validator = validator;

    public async Task<string> CreateBackupAsync(string sourcePath, string destinationFolder, CancellationToken cancellationToken = default)
    {
        var validation = await _validator.ValidateAsync(sourcePath, cancellationToken);
        if (!validation.IsValid) throw new InvalidDataException(validation.Message);
        Directory.CreateDirectory(destinationFolder);
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmssfff");
        var backupPath = Path.Combine(destinationFolder, $"{Path.GetFileName(sourcePath)}.{stamp}.wdlbackup");
        await CopyAtomicAsync(sourcePath, backupPath, cancellationToken);
        var manifest = new BackupManifest(Path.GetFileName(sourcePath), new FileInfo(sourcePath).Length, validation.Sha256!, DateTimeOffset.UtcNow);
        await File.WriteAllTextAsync(backupPath + ".json", JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }), cancellationToken);
        return backupPath;
    }

    public async Task RestoreAsync(string backupPath, string targetPath, CancellationToken cancellationToken = default)
    {
        var manifestPath = backupPath + ".json";
        if (!File.Exists(manifestPath)) throw new InvalidDataException("Backup manifest is missing.");
        var manifest = JsonSerializer.Deserialize<BackupManifest>(await File.ReadAllTextAsync(manifestPath, cancellationToken))
            ?? throw new InvalidDataException("Backup manifest is invalid.");
        await using (var stream = File.OpenRead(backupPath))
        {
            var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));
            if (!CryptographicOperations.FixedTimeEquals(Convert.FromHexString(hash), Convert.FromHexString(manifest.Sha256)))
                throw new InvalidDataException("Backup checksum does not match its manifest.");
        }
        if (new FileInfo(backupPath).Length != manifest.Length) throw new InvalidDataException("Backup length does not match its manifest.");
        await CopyAtomicAsync(backupPath, targetPath, cancellationToken);
    }

    public static async Task WriteAtomicAsync(string targetPath, byte[] data, CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(targetPath)!;
        var temporary = Path.Combine(directory, $".{Path.GetFileName(targetPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllBytesAsync(temporary, data, cancellationToken);
            File.Move(temporary, targetPath, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static async Task CopyAtomicAsync(string sourcePath, string targetPath, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(targetPath)!;
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, $".{Path.GetFileName(targetPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, 131072, true))
            await using (var target = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 131072, true))
                await source.CopyToAsync(target, cancellationToken);
            File.Move(temporary, targetPath, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
