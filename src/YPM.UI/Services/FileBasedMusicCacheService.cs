using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using YPM.Core.Services;

namespace YPM.UI.Services;

public sealed class FileBasedMusicCacheService : IMusicCacheService
{
    private readonly string _cacheDir;
    private readonly long _maxSizeBytes;
    private readonly Dictionary<string, MusicCacheEntry> _index = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _keyLocks = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _globalLock = new(1, 1);
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient = new();
    private readonly Func<string?>? _sessionCookieProvider;
    private bool _initialized;

    private string IndexPath => Path.Combine(_cacheDir, "music_cache_index.json");
    private const long DefaultMaxCacheSizeBytes = 2L * 1024 * 1024 * 1024; // 2 GB

    public FileBasedMusicCacheService(string? cacheDir = null, long maxSizeBytes = DefaultMaxCacheSizeBytes, Func<string?>? sessionCookieProvider = null)
    {
        _cacheDir = cacheDir ?? GetDefaultCachePath();
        _maxSizeBytes = maxSizeBytes > 0 ? maxSizeBytes : DefaultMaxCacheSizeBytes;
        _sessionCookieProvider = sessionCookieProvider;
        _httpClient.Timeout = TimeSpan.FromMinutes(10);
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");
    }

    public string CacheDirectory => _cacheDir;

    public static string GetDefaultCachePath()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Fusion",
            "music_cache");
    }

    public async Task InitializeAsync()
    {
        if (_initialized) return;
        
        await _globalLock.WaitAsync();
        try
        {
            if (_initialized) return;
            _initialized = true;

            try
            {
                Directory.CreateDirectory(_cacheDir);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to create cache directory: {ex.Message}");
                return;
            }

            // Clean up orphaned temp files from interrupted downloads.
            try
            {
                foreach (var tmpFile in Directory.GetFiles(_cacheDir, "*.tmp"))
                {
                    try { File.Delete(tmpFile); } catch { }
                }
            }
            catch { }

            if (File.Exists(IndexPath))
            {
                try
                {
                    var json = await File.ReadAllTextAsync(IndexPath);
                    var entries = JsonSerializer.Deserialize<List<MusicCacheEntry>>(json, _jsonOptions);
                    if (entries is not null)
                    {
                        _index.Clear();
                        foreach (var entry in entries)
                        {
                            if (!string.IsNullOrEmpty(entry.FileName) && File.Exists(Path.Combine(_cacheDir, entry.FileName)))
                            {
                                _index[entry.Key] = entry;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Failed to load music cache index, rebuilding: {ex.Message}");
                    try { File.Delete(IndexPath); } catch { }
                }
            }

            await PruneToSizeInternalAsync(_maxSizeBytes);
        }
        finally
        {
            _globalLock.Release();
        }
    }

    public async Task<string?> TryGetCachedFilePathAsync(long trackId, long bitrate, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync();
        var key = BuildTrackKey(trackId, bitrate);

        MusicCacheEntry? entry;
        await _globalLock.WaitAsync(cancellationToken);
        try
        {
            if (!_index.TryGetValue(key, out entry))
            {
                return null;
            }
        }
        finally
        {
            _globalLock.Release();
        }

        cancellationToken.ThrowIfCancellationRequested();

        var filePath = Path.Combine(_cacheDir, entry.FileName);
        if (!File.Exists(filePath) || new FileInfo(filePath).Length <= 0)
        {
            await _globalLock.WaitAsync(cancellationToken);
            try
            {
                _index.Remove(key);
                await SaveIndexInternalAsync();
            }
            finally
            {
                _globalLock.Release();
            }
            return null;
        }

        return filePath;
    }

    public async Task CacheTrackAsync(long trackId, long bitrate, string sourceUrl, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sourceUrl))
        {
            return;
        }

        await EnsureInitializedAsync();

        var key = BuildTrackKey(trackId, bitrate);
        var gate = _keyLocks.GetOrAdd(key, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);

        try
        {
            bool alreadyExists = false;
            await _globalLock.WaitAsync(cancellationToken);
            try
            {
                if (_index.TryGetValue(key, out var existingEntry))
                {
                    var existingPath = Path.Combine(_cacheDir, existingEntry.FileName);
                    if (File.Exists(existingPath) && new FileInfo(existingPath).Length > 0)
                    {
                        alreadyExists = true;
                    }
                    else
                    {
                        _index.Remove(key);
                    }
                }
            }
            finally
            {
                _globalLock.Release();
            }

            if (alreadyExists) return;

            var extension = GetSafeExtension(sourceUrl);
            var fileName = $"{ComputeStableHash(key)}{extension}";
            var filePath = Path.Combine(_cacheDir, fileName);
            
            // Final check on disk before downloading
            if (File.Exists(filePath))
            {
                var info = new FileInfo(filePath);
                if (info.Length > 0)
                {
                    await AddToIndexAsync(key, fileName, info.Length);
                    return;
                }
                try { File.Delete(filePath); } catch { }
            }

            var tempPath = Path.Combine(_cacheDir, $"{Guid.NewGuid():N}.tmp");
            try
            {
                using var request = CreateDownloadRequest(sourceUrl);
                using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                response.EnsureSuccessStatusCode();

                var finalExtension = GetSafeExtension(response.RequestMessage?.RequestUri?.ToString() ?? sourceUrl);
                if (!string.Equals(finalExtension, extension, StringComparison.OrdinalIgnoreCase))
                {
                    fileName = $"{ComputeStableHash(key)}{finalExtension}";
                    filePath = Path.Combine(_cacheDir, fileName);
                }

                await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken))
                await using (var output = File.Create(tempPath))
                {
                    await input.CopyToAsync(output, cancellationToken);
                    await output.FlushAsync(cancellationToken);
                }

                var info = new FileInfo(tempPath);
                if (info.Length <= 0) return;

                try
                {
                    if (File.Exists(filePath))
                    {
                        File.Delete(filePath);
                    }
                    File.Move(tempPath, filePath, overwrite: true);
                }
                catch (IOException)
                {
                    // Destination file appeared or is locked — if it exists and is valid, use it.
                    if (!File.Exists(filePath) || new FileInfo(filePath).Length <= 0)
                    {
                        throw;
                    }
                }

                var finalInfo = new FileInfo(filePath);
                await AddToIndexAsync(key, fileName, finalInfo.Length);
                await PruneToSizeAsync(_maxSizeBytes);
            }
            finally
            {
                if (File.Exists(tempPath))
                {
                    try { File.Delete(tempPath); } catch { }
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Music cache failed for track {trackId} at bitrate {bitrate}: {sourceUrl}. {ex}");
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task AddToIndexAsync(string key, string fileName, long size)
    {
        await _globalLock.WaitAsync();
        try
        {
            _index[key] = new MusicCacheEntry
            {
                Key = key,
                FileName = fileName,
                CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                Size = size,
            };
            await SaveIndexInternalAsync();
        }
        finally
        {
            _globalLock.Release();
        }
    }

    public async Task<long> GetCacheSizeAsync()
    {
        await EnsureInitializedAsync();
        await _globalLock.WaitAsync();
        try
        {
            return _index.Values.Sum(static entry => entry.Size);
        }
        finally
        {
            _globalLock.Release();
        }
    }

    public async Task ClearAllAsync()
    {
        await EnsureInitializedAsync();
        await _globalLock.WaitAsync();
        try
        {
            foreach (var entry in _index.Values)
            {
                var filePath = Path.Combine(_cacheDir, entry.FileName);
                if (File.Exists(filePath))
                {
                    try { File.Delete(filePath); } catch { }
                }
            }

            _index.Clear();
            await SaveIndexInternalAsync();
        }
        finally
        {
            _globalLock.Release();
        }
    }

    private async Task EnsureInitializedAsync()
    {
        if (!_initialized)
        {
            await InitializeAsync();
        }
    }

    public async Task<long> PruneToSizeAsync(long maxSizeBytes)
    {
        await _globalLock.WaitAsync();
        try
        {
            return await PruneToSizeInternalAsync(maxSizeBytes);
        }
        finally
        {
            _globalLock.Release();
        }
    }

    private async Task<long> PruneToSizeInternalAsync(long maxSizeBytes)
    {
        var currentSize = _index.Values.Sum(static entry => entry.Size);
        if (currentSize <= maxSizeBytes)
        {
            return currentSize;
        }

        foreach (var entry in _index.Values.OrderBy(static entry => entry.CreatedAt).ToList())
        {
            if (currentSize <= maxSizeBytes)
            {
                break;
            }

            var filePath = Path.Combine(_cacheDir, entry.FileName);
            if (File.Exists(filePath))
            {
                try
                {
                    File.Delete(filePath);
                }
                catch
                {
                    // File is locked (e.g. being played) — skip this entry.
                    continue;
                }
            }

            _index.Remove(entry.Key);
            currentSize -= entry.Size;
        }

        await SaveIndexInternalAsync();
        return currentSize;
    }

    private async Task SaveIndexInternalAsync()
    {
        try
        {
            Directory.CreateDirectory(_cacheDir);
            var entries = _index.Values.ToList();
            var json = JsonSerializer.Serialize(entries, _jsonOptions);
            var tempPath = IndexPath + ".tmp";
            
            // Retry mechanism for saving index
            for (int i = 0; i < 3; i++)
            {
                try
                {
                    await File.WriteAllTextAsync(tempPath, json);
                    if (File.Exists(IndexPath)) File.Delete(IndexPath);
                    File.Move(tempPath, IndexPath, overwrite: true);
                    break;
                }
                catch (IOException) when (i < 2)
                {
                    await Task.Delay(100);
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to save music cache index: {ex.Message}");
        }
    }

    private static string BuildTrackKey(long trackId, long bitrate) => $"{trackId}:{bitrate}";

    private HttpRequestMessage CreateDownloadRequest(string sourceUrl)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, sourceUrl);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*"));

        if (Uri.TryCreate(sourceUrl, UriKind.Absolute, out var uri))
        {
            request.Headers.Referrer = new Uri($"{uri.Scheme}://{uri.Host}/");
        }

        var sessionCookie = _sessionCookieProvider?.Invoke();
        if (!string.IsNullOrWhiteSpace(sessionCookie))
        {
            request.Headers.TryAddWithoutValidation("Cookie", sessionCookie);
        }

        return request;
    }

    private static string ComputeStableHash(string text)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(bytes);
    }

    private static string GetSafeExtension(string sourceUrl)
    {
        if (Uri.TryCreate(sourceUrl, UriKind.Absolute, out var uri))
        {
            var ext = Path.GetExtension(uri.AbsolutePath);
            if (!string.IsNullOrWhiteSpace(ext) && ext.Length >= 2 && ext.Length <= 8)
            {
                return ext;
            }
        }

        return ".mp3";
    }

    internal sealed class MusicCacheEntry
    {
        public string Key { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public long CreatedAt { get; set; }
        public long Size { get; set; }
    }
}
