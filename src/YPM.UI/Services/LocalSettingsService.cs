using System.Text.Json;
using YPM.Core.Models;
using YPM.Core.Services;
using YPM.UI.Helpers;

namespace YPM.UI.Services;

public sealed class LocalSettingsService : ISettingsService
{
    private const string FileName = "settings.json";
    private readonly JsonSerializerOptions _serializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    private static string SettingsDirectory => AppDataPaths.SettingsDirectory;
    private static string SettingsPath => Path.Combine(SettingsDirectory, FileName);

    public async Task<AppSettings> LoadAsync()
    {
        try
        {
            AppDataPaths.EnsureDataDirectory();

            if (!File.Exists(SettingsPath))
            {
                return new AppSettings();
            }

            var json = await File.ReadAllTextAsync(SettingsPath);
            return JsonSerializer.Deserialize<AppSettings>(json, _serializerOptions) ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public async Task SaveAsync(AppSettings settings)
    {
        Directory.CreateDirectory(SettingsDirectory);
        var json = JsonSerializer.Serialize(settings, _serializerOptions);
        await File.WriteAllTextAsync(SettingsPath, json);
    }
}
