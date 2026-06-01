namespace YPM.Core.Services;

public interface IMusicCacheService
{
    string CacheDirectory { get; }

    Task InitializeAsync();
    Task<string?> TryGetCachedFilePathAsync(long trackId, long bitrate, CancellationToken cancellationToken = default);
    Task CacheTrackAsync(long trackId, long bitrate, string sourceUrl, CancellationToken cancellationToken = default);
    Task<long> GetCacheSizeAsync();
    Task ClearAllAsync();
}
