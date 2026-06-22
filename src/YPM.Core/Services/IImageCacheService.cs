namespace YPM.Core.Services;

public interface IImageCacheService
{
    string CacheDirectory { get; }

    Task InitializeAsync();
    string? GetCachedFilePath(string url);
    Task CacheImageAsync(string url, CancellationToken cancellationToken = default);
    Task<long> GetCacheSizeAsync();
    Task ClearAllAsync();
}
