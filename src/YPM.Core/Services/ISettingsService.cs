namespace YPM.Core.Services;

using YPM.Core.Models;

public interface ISettingsService
{
    Task<AppSettings> LoadAsync();

    Task SaveAsync(AppSettings settings);
}
