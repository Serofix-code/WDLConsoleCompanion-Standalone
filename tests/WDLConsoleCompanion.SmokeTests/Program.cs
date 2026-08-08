using System.Security.Cryptography;
using WDLConsoleCompanion.Services;

var testRoot = Path.Combine(Path.GetTempPath(), $"wdlcc-tests-{Guid.NewGuid():N}");
Directory.CreateDirectory(testRoot);
try
{
    var originalPath = Path.Combine(testRoot, "save.dat");
    var original = RandomNumberGenerator.GetBytes(4096);
    await File.WriteAllBytesAsync(originalPath, original);

    var validator = new SaveValidator();
    var service = new BackupService(validator);
    var validation = await validator.ValidateAsync(originalPath);
    Assert(validation.IsValid && validation.Sha256 is not null, "valid save rejected");

    var backup = await service.CreateBackupAsync(originalPath, Path.Combine(testRoot, "backups"));
    Assert(File.Exists(backup) && File.Exists(backup + ".json"), "backup or manifest missing");

    await File.WriteAllBytesAsync(originalPath, RandomNumberGenerator.GetBytes(4096));
    await service.RestoreAsync(backup, originalPath);
    Assert((await File.ReadAllBytesAsync(originalPath)).SequenceEqual(original), "restore was not byte-identical");

    var tampered = await File.ReadAllBytesAsync(backup);
    tampered[100] ^= 0xFF;
    await File.WriteAllBytesAsync(backup, tampered);
    var rejected = false;
    try { await service.RestoreAsync(backup, originalPath); }
    catch (InvalidDataException) { rejected = true; }
    Assert(rejected, "tampered backup was accepted");

    Console.WriteLine("PASS: validation, backup, byte-identical restore, and tamper rejection");
}
finally
{
    if (Directory.Exists(testRoot)) Directory.Delete(testRoot, true);
}

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
