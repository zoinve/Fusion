namespace YPM.Core.Services;

public interface ILocalCacheService
{
    Task InitializeAsync();
    Task<T?> GetAsync<T>(string key) where T : class;
    Task SetAsync<T>(string key, T value, TimeSpan? ttl = null, bool protectFromPrune = false) where T : class;
    Task<bool> RemoveAsync(string key);
    Task<long> GetCacheSizeAsync();
    Task ClearExpiredAsync();
    Task ClearAllAsync();
    Task<long> PruneToSizeAsync(long maxSizeBytes);
}

public sealed class CacheEntry
{
    public string Key { get; set; } = string.Empty;
    public long CreatedAt { get; set; }
    public long ExpiresAt { get; set; }
    public long Size { get; set; }
    public bool ProtectFromPrune { get; set; }
}
