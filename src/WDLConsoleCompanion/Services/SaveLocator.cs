namespace WDLConsoleCompanion.Services;

public sealed class SaveLocator
{
    private static readonly string[] KnownGameFolders = ["3353", "7016", "Watch Dogs Legion", "WatchDogsLegion"];

    public Task<string?> FindMostRecentAsync(CancellationToken cancellationToken = default) => Task.Run(() =>
    {
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var roots = new[]
        {
            Path.Combine(documents, "Ubisoft", "Ubisoft Game Launcher", "savegames"),
            Path.Combine(documents, "Ubisoft Game Launcher", "savegames"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Ubisoft Game Launcher", "savegames"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Ubisoft", "Ubisoft Game Launcher", "savegames")
        };

        var candidates = new List<FileInfo>();
        foreach (var root in roots.Where(Directory.Exists))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var info = new FileInfo(file);
                    if (info.Length < 64 || info.Extension is ".json" or ".png" or ".jpg") continue;
                    if (KnownGameFolders.Any(folder => file.Contains($"{Path.DirectorySeparatorChar}{folder}{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)))
                        candidates.Add(info);
                }
            }
            catch (UnauthorizedAccessException) { }
            catch (IOException) { }
        }

        return candidates.OrderByDescending(f => f.LastWriteTimeUtc).FirstOrDefault()?.FullName;
    }, cancellationToken);
}
