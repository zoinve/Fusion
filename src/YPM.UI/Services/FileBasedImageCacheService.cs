using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using YPM.Core.Services;

namespace YPM.UI.Services;

public sealed class FileBasedImageCacheService : IImageCacheService
{
    private readonly string _cacheDir;
    private readonly long _maxSizeBytes;
    private readonly ConcurrentDictionary<string, ImageCacheEntry> _index = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _keyLocks = new(StringComparer.OrdinalIgnoreCase);
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient = new();
    private bool _initialized;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private readonly object _persistLock = new();

    private string IndexPath => Path.Combine(_cacheDir, "image_cache_index.json");
    private const long DefaultMaxCacheSizeBytes = 200L * 1024 * 1024; // 200 MB

    public FileBasedImageCacheService(string? cacheDir = null, long maxSizeBytes = DefaultMaxCacheSizeBytes)
    {
        _cacheDir = cacheDir ?? GetDefaultCachePath();
        _maxSizeBytes = maxSizeBytes > 0 ? maxSizeBytes : DefaultMaxCacheSizeBytes;
        _httpClient.Timeout = TimeSpan.FromSeconds(15);
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
    }

    public string CacheDirectory => _cacheDir;

    public static string GetDefaultCachePath()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Fusion",
            "image_cache");
    }

    public async Task InitializeAsync()
    {
        if (_initialized) return;

        await _initLock.WaitAsync();
        try
        {
            if (_initialized) return;
            _initialized = true;

            try { Directory.CreateDirectory(_cacheDir); }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to create image cache directory: {ex.Message}");
                return;
            }

            if (File.Exists(IndexPath))
            {
                try
                {
                    var json = await File.ReadAllTextAsync(IndexPath);
                    var entries = JsonSerializer.Deserialize<List<ImageCacheEntry>>(json, _jsonOptions);
                    if (entries is not null)
                    {
                        _index.Clear();
                        foreach (var entry in entries)
                        {
                            if (entry is null || string.IsNullOrEmpty(entry.Key))
                                continue;

                            if (!string.IsNullOrEmpty(entry.FileName) && File.Exists(Path.Combine(_cacheDir, entry.FileName)))
                            {
                                _index[entry.Key] = entry;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Failed to load image cache index, rebuilding: {ex.Message}");
                    try { File.Delete(IndexPath); } catch { }
                }
            }

            await PruneToSizeAsync(_maxSizeBytes);
        }
        finally
        {
            _initLock.Release();
        }
    }

    public string? GetCachedFilePath(string url)
    {
        var key = HashUrl(url);
        if (_index.TryGetValue(key, out var entry) && !string.IsNullOrEmpty(entry.FileName))
        {
            var filePath = Path.Combine(_cacheDir, entry.FileName);
            if (File.Exists(filePath))
            {
                entry.LastAccessedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                return filePath;
            }

            // Stale entry — remove it
            _index.TryRemove(key, out _);
        }

        return null;
    }

    public async Task CacheImageAsync(string url, CancellationToken cancellationToken = default)
    {
        var key = HashUrl(url);

        // If already cached, skip
        if (_index.TryGetValue(key, out var existing) &&
            !string.IsNullOrEmpty(existing.FileName) &&
            File.Exists(Path.Combine(_cacheDir, existing.FileName)))
        {
            return;
        }

        // Deduplicate concurrent downloads for the same URL
        var keyLock = _keyLocks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await keyLock.WaitAsync(cancellationToken);
        try
        {
            // Double-check after acquiring lock
            if (_index.TryGetValue(key, out var entry) &&
                !string.IsNullOrEmpty(entry.FileName) &&
                File.Exists(Path.Combine(_cacheDir, entry.FileName)))
            {
                return;
            }

            var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseContentRead, cancellationToken);
            response.EnsureSuccessStatusCode();

            var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            if (bytes is null || bytes.Length == 0) return;

            var ext = ".jpg";
            var contentType = response.Content.Headers.ContentType?.MediaType;
            if (!string.IsNullOrEmpty(contentType))
            {
                if (contentType.Contains("png", StringComparison.OrdinalIgnoreCase))
                    ext = ".png";
                else if (contentType.Contains("webp", StringComparison.OrdinalIgnoreCase))
                    ext = ".webp";
            }

            var fileName = $"{key}{ext}";
            var filePath = Path.Combine(_cacheDir, fileName);

            Directory.CreateDirectory(_cacheDir);
            await File.WriteAllBytesAsync(filePath, bytes, cancellationToken);

            var newEntry = new ImageCacheEntry
            {
                Key = key,
                FileName = fileName,
                Url = url,
                Size = bytes.Length,
                CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                LastAccessedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            };

            _index[key] = newEntry;
            await SaveIndexAsync();

            if (await GetCacheSizeAsync() > _maxSizeBytes)
            {
                _ = PruneToSizeAsync(_maxSizeBytes);
            }
        }
        catch (OperationCanceledException)
        {
            // Ignore cancellation
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to cache image '{url}': {ex.Message}");
        }
        finally
        {
            keyLock.Release();
            _keyLocks.TryRemove(key, out _);
        }
    }

    public async Task<long> GetCacheSizeAsync()
    {
        long total = 0;
        foreach (var kvp in _index)
        {
            var entry = kvp.Value;
            if (entry.Size > 0)
            {
                total += entry.Size;
            }
            else if (!string.IsNullOrEmpty(entry.FileName))
            {
                var path = Path.Combine(_cacheDir, entry.FileName);
                if (File.Exists(path))
                {
                    try { total += new FileInfo(path).Length; } catch { }
                }
            }
        }
        return await Task.FromResult(total);
    }

    public async Task ClearAllAsync()
    {
        foreach (var kvp in _index)
        {
            if (!string.IsNullOrEmpty(kvp.Value.FileName))
            {
                var path = Path.Combine(_cacheDir, kvp.Value.FileName);
                if (File.Exists(path))
                {
                    try { File.Delete(path); } catch { }
                }
            }
        }

        _index.Clear();
        await SaveIndexAsync();

        if (Directory.Exists(_cacheDir))
        {
            try { Directory.Delete(_cacheDir, recursive: true); } catch { }
        }
    }

    private async Task PruneToSizeAsync(long maxSizeBytes)
    {
        var currentSize = await GetCacheSizeAsync();
        if (currentSize <= maxSizeBytes) return;

        var entriesByLastAccess = _index.Values
            .OrderBy(e => e.LastAccessedAt)
            .ToList();

        foreach (var entry in entriesByLastAccess)
        {
            if (currentSize <= maxSizeBytes) break;

            if (!string.IsNullOrEmpty(entry.FileName))
            {
                var path = Path.Combine(_cacheDir, entry.FileName);
                if (File.Exists(path))
                {
                    var fileSize = entry.Size > 0 ? entry.Size : new FileInfo(path).Length;
                    currentSize -= fileSize;
                    try { File.Delete(path); } catch { }
                }
            }

            _index.TryRemove(entry.Key, out _);
        }

        await SaveIndexAsync();
    }

    private static string HashUrl(string url)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(url));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private Task SaveIndexAsync()
    {
        var entries = _index.Values
            .Where(e => e is not null && !string.IsNullOrEmpty(e.Key))
            .ToList();
        var json = JsonSerializer.Serialize(entries, _jsonOptions);

        lock (_persistLock)
        {
            Directory.CreateDirectory(_cacheDir);
            var tempPath = IndexPath + ".tmp";
            File.WriteAllText(tempPath, json);
            try
            {
                File.Move(tempPath, IndexPath, overwrite: true);
            }
            catch (FileNotFoundException)
            {
                File.WriteAllText(IndexPath, json);
            }
        }

        return Task.CompletedTask;
    }
}

internal sealed class ImageCacheEntry
{
    public string Key { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public long Size { get; set; }
    public long CreatedAt { get; set; }
    public long LastAccessedAt { get; set; }
}
