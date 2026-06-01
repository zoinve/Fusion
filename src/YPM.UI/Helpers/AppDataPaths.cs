namespace YPM.UI.Helpers;

public static class AppDataPaths
{
    private static string? _dataDir;

    public static string DataDirectory
    {
        get
        {
            if (_dataDir is not null) return _dataDir;
            _dataDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Fusion");
            return _dataDir;
        }
    }

    // Legacy path used before we migrated to LocalApplicationData.
    public static string LegacyDataDirectory =>
        Path.Combine(AppContext.BaseDirectory, "userdata");

    public static string SettingsDirectory => DataDirectory;
    public static string CacheDirectory => Path.Combine(DataDirectory, "cache");

    // Ensure the data directory exists and migrate any legacy data.
    public static void EnsureDataDirectory()
    {
        Directory.CreateDirectory(DataDirectory);

        // Migrate legacy settings.json if present.
        var legacySettingsDir = LegacyDataDirectory;
        if (Directory.Exists(legacySettingsDir) && legacySettingsDir != DataDirectory)
        {
            try
            {
                var legacySettings = Path.Combine(legacySettingsDir, "settings.json");
                var newSettings = Path.Combine(DataDirectory, "settings.json");
                if (File.Exists(legacySettings) && !File.Exists(newSettings))
                {
                    File.Copy(legacySettings, newSettings, overwrite: false);
                }

                var legacyCache = Path.Combine(legacySettingsDir, "cache");
                var newCache = CacheDirectory;
                if (Directory.Exists(legacyCache) && !Directory.Exists(newCache))
                {
                    CopyDirectory(legacyCache, newCache);
                }
            }
            catch
            {
                // Migration is best-effort.
            }
        }
    }

    private static void CopyDirectory(string sourceDir, string destDir)
    {
        Directory.CreateDirectory(destDir);
        foreach (var file in Directory.GetFiles(sourceDir))
        {
            var dest = Path.Combine(destDir, Path.GetFileName(file));
            File.Copy(file, dest, overwrite: false);
        }
        foreach (var dir in Directory.GetDirectories(sourceDir))
        {
            var dest = Path.Combine(destDir, Path.GetFileName(dir));
            CopyDirectory(dir, dest);
        }
    }
}
