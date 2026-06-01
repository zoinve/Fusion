using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using YPM.Core.Services;
using YPM.UI.Helpers;

namespace YPM.UI.Services;

public sealed class FileBasedCacheService : ILocalCacheService
{
    private readonly string _cacheDir;
    private readonly long _maxSizeBytes;
    private readonly Dictionary<string, CacheEntry> _index = new(StringComparer.OrdinalIgnoreCase);
    private string IndexPath => Path.Combine(_cacheDir, "cache_index.json");
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);
    private const long DefaultMaxCacheSizeBytes = 500L * 1024 * 1024; // 500 MB
    private bool _initialized;
    private readonly SemaphoreSlim _saveLock = new(1, 1);

    public FileBasedCacheService(string? cacheDir = null, long maxSizeBytes = DefaultMaxCacheSizeBytes)
    {
        _cacheDir = cacheDir ?? GetDefaultCachePath();
        _maxSizeBytes = maxSizeBytes > 0 ? maxSizeBytes : DefaultMaxCacheSizeBytes;
    }

    public static string GetDefaultCachePath()
    {
        return AppDataPaths.CacheDirectory;
    }

    public async Task InitializeAsync()
    {
        if (_initialized) return;
        _initialized = true;

        Directory.CreateDirectory(_cacheDir);

        if (File.Exists(IndexPath))
        {
            try
            {
                var json = await File.ReadAllTextAsync(IndexPath);
                var entries = JsonSerializer.Deserialize<List<CacheEntry>>(json, _jsonOptions);
                if (entries is not null)
                {
                    _index.Clear();
                    foreach (var entry in entries)
                    {
                        if (File.Exists(GetCacheFilePath(entry.Key)))
                        {
                            _index[entry.Key] = entry;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to load cache index, rebuilding: {ex.Message}");
                try { File.Delete(IndexPath); } catch { }
            }
        }

        await ClearExpiredAsync();
    }

    public async Task<T?> GetAsync<T>(string key) where T : class
    {
        if (!_index.TryGetValue(key, out var entry)) return null;

        if (entry.ExpiresAt > 0 && DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() > entry.ExpiresAt)
        {
            await RemoveAsync(key);
            return null;
        }

        var filePath = GetCacheFilePath(key);
        if (!File.Exists(filePath))
        {
            _index.Remove(key);
            await SaveIndexAsync();
            return null;
        }

        try
        {
            var json = await File.ReadAllTextAsync(filePath);
            return JsonSerializer.Deserialize<T>(json, _jsonOptions);
        }
        catch
        {
            return null;
        }
    }

    public async Task SetAsync<T>(string key, T value, TimeSpan? ttl = null, bool protectFromPrune = false) where T : class
    {
        var json = JsonSerializer.Serialize(value, _jsonOptions);
        var filePath = GetCacheFilePath(key);

        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        await File.WriteAllTextAsync(filePath, json);

        var entry = new CacheEntry
        {
            Key = key,
            CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            ExpiresAt = ttl.HasValue
                ? DateTimeOffset.UtcNow.Add(ttl.Value).ToUnixTimeMilliseconds()
                : 0,
            Size = json.Length,
            ProtectFromPrune = protectFromPrune,
        };

        _index[key] = entry;
        await SaveIndexAsync();

        if (_index.Values.Sum(static e => e.Size) > _maxSizeBytes)
        {
            _ = PruneToSizeAsync(_maxSizeBytes);
        }
    }

    public async Task<bool> RemoveAsync(string key)
    {
        var filePath = GetCacheFilePath(key);
        var hadFile = false;

        if (File.Exists(filePath))
        {
            File.Delete(filePath);
            hadFile = true;
        }

        var hadIndex = _index.Remove(key);
        if (hadFile || hadIndex)
        {
            await SaveIndexAsync();
        }

        return hadFile;
    }

    public async Task<long> GetCacheSizeAsync()
    {
        long totalSize = 0;
        foreach (var entry in _index.Values)
        {
            totalSize += entry.Size > 0 ? entry.Size : new FileInfo(GetCacheFilePath(entry.Key)).Length;
        }
        return await Task.FromResult(totalSize);
    }

    public async Task ClearExpiredAsync()
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var expiredKeys = _index
            .Where(kv => kv.Value.ExpiresAt > 0 && now > kv.Value.ExpiresAt)
            .Select(kv => kv.Key)
            .ToList();

        foreach (var key in expiredKeys)
        {
            var filePath = GetCacheFilePath(key);
            if (File.Exists(filePath)) File.Delete(filePath);
            _index.Remove(key);
        }

        if (expiredKeys.Count > 0) await SaveIndexAsync();
    }

    public async Task ClearAllAsync()
    {
        foreach (var entry in _index.Values)
        {
            var filePath = GetCacheFilePath(entry.Key);
            if (File.Exists(filePath)) File.Delete(filePath);
        }
        _index.Clear();
        await SaveIndexAsync();

        if (Directory.Exists(_cacheDir))
        {
            try { Directory.Delete(_cacheDir, recursive: true); } catch { }
        }
    }

    public async Task<long> PruneToSizeAsync(long maxSizeBytes)
    {
        var currentSize = await GetCacheSizeAsync();
        if (currentSize <= maxSizeBytes) return currentSize;

        var entriesByAge = _index.Values
            .Where(static e => !e.ProtectFromPrune)
            .OrderBy(e => e.CreatedAt)
            .ToList();
        foreach (var entry in entriesByAge)
        {
            if (currentSize <= maxSizeBytes) break;
            var filePath = GetCacheFilePath(entry.Key);
            if (File.Exists(filePath))
            {
                currentSize -= entry.Size > 0 ? entry.Size : new FileInfo(filePath).Length;
                File.Delete(filePath);
            }
            _index.Remove(entry.Key);
        }

        await SaveIndexAsync();
        return currentSize;
    }

    private string GetCacheFilePath(string key)
    {
        var bytes = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(key));
        var hash = Convert.ToHexString(bytes);
        return Path.Combine(_cacheDir, $"{hash}.cache");
    }

    private async Task SaveIndexAsync()
    {
        var entries = _index.Values.ToList();
        var json = JsonSerializer.Serialize(entries, _jsonOptions);
        Directory.CreateDirectory(_cacheDir);

        await _saveLock.WaitAsync();
        try
        {
            var tempPath = IndexPath + ".tmp";
            await File.WriteAllTextAsync(tempPath, json);
            try
            {
                File.Move(tempPath, IndexPath, overwrite: true);
            }
            catch (FileNotFoundException)
            {
                File.WriteAllText(IndexPath, json);
            }
        }
        finally
        {
            _saveLock.Release();
        }
    }
}
