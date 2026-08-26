namespace Butchi.Infrastructure;

public sealed class AppPaths
{
    public AppPaths(string? dataDirectory = null)
    {
        DataDirectory = dataDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "butchi");
    }

    public string DataDirectory { get; }
    public string ConfigPath => Path.Combine(DataDirectory, "config.json");
    public string HistoryDbPath => Path.Combine(DataDirectory, "history.db");
    public string LegacyHistoryPath => Path.Combine(DataDirectory, "history.json");
    public string MigratedLegacyHistoryPath => Path.Combine(DataDirectory, "history.migrated.json");
    public string ModelsDirectory => Path.Combine(DataDirectory, "models");

    public string ModelPath(string repo, string file) =>
        Path.Combine(ModelsDirectory, repo.Replace("/", "__", StringComparison.Ordinal), file);

    public void EnsureDirectories()
    {
        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(ModelsDirectory);
    }
}
