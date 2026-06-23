using Microsoft.UI.Dispatching;
using YPM.Api.Abstractions;
using YPM.Core.Services;

namespace YPM.UI.Services;

public sealed class LikedSongsService : ILikedSongsService, IDisposable
{
    private readonly HashSet<long> _likedTrackIds = [];
    private readonly INeteaseApiClient _apiClient;
    private readonly ILocalCacheService _cacheService;
    private readonly DispatcherQueueTimer _refreshTimer;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    internal const string LikedIdsCacheKey = "liked/track_ids";
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMinutes(5);

    private bool _isLoaded;

    public LikedSongsService(INeteaseApiClient apiClient, ILocalCacheService cacheService, DispatcherQueue dispatcherQueue)
    {
        _apiClient = apiClient;
        _cacheService = cacheService;
        _refreshTimer = dispatcherQueue.CreateTimer();
        _refreshTimer.Interval = RefreshInterval;
        _refreshTimer.Tick += OnRefreshTimerTick;
    }

    public bool IsLoaded => _isLoaded;

    public IReadOnlySet<long> LikedTrackIds => _likedTrackIds;

    public bool IsLiked(long trackId) => _likedTrackIds.Contains(trackId);

    public event EventHandler<long>? TrackLiked;
    public event EventHandler<long>? TrackUnliked;
    public event EventHandler? Refreshed;

    public async Task InitializeAsync(long uid)
    {
        await LoadIdsFromCacheAsync();
        await RefreshAsync(uid);

        _isLoaded = true;
        _refreshTimer.Start();
    }

    public async Task<bool> LikeAsync(long trackId)
    {
        try
        {
            var result = await _apiClient.LikeTrackAsync(trackId, true);
            if (result.IsSuccess)
            {
                _likedTrackIds.Add(trackId);
                await SaveIdsToCacheAsync();
                TrackLiked?.Invoke(this, trackId);
                return true;
            }
        }
        catch { }
        return false;
    }

    public async Task<bool> UnlikeAsync(long trackId)
    {
        try
        {
            var result = await _apiClient.LikeTrackAsync(trackId, false);
            if (result.IsSuccess)
            {
                _likedTrackIds.Remove(trackId);
                await SaveIdsToCacheAsync();
                TrackUnliked?.Invoke(this, trackId);
                return true;
            }
        }
        catch { }
        return false;
    }

    public async Task RefreshAsync()
    {
        var user = App.Settings.CurrentUser;
        if (user is null) return;
        await RefreshAsync(user.UserId);
    }

    private async Task RefreshAsync(long uid)
    {
        if (!_refreshLock.Wait(0)) return;

        try
        {
            var ids = await _apiClient.GetLikedTrackIdsAsync(uid);
            _likedTrackIds.Clear();
            foreach (var id in ids)
                _likedTrackIds.Add(id);

            await SaveIdsToCacheAsync();
            Refreshed?.Invoke(this, EventArgs.Empty);
        }
        catch { }
        finally
        {
            _refreshLock.Release();
        }
    }

    private async void OnRefreshTimerTick(DispatcherQueueTimer sender, object args)
    {
        await RefreshAsync();
    }

    private async Task LoadIdsFromCacheAsync()
    {
        if (_cacheService is null) return;

        try
        {
            var cached = await _cacheService.GetAsync<CachedLikedTrackIds>(LikedIdsCacheKey);
            if (cached?.Ids is { Count: > 0 })
            {
                _likedTrackIds.Clear();
                foreach (var id in cached.Ids)
                    _likedTrackIds.Add(id);
            }
        }
        catch { }
    }

    private async Task SaveIdsToCacheAsync()
    {
        if (_cacheService is null) return;

        try
        {
            var payload = new CachedLikedTrackIds { Ids = [.. _likedTrackIds] };
            await _cacheService.SetAsync(LikedIdsCacheKey, payload, ttl: null, protectFromPrune: true);
        }
        catch { }
    }

    public void Dispose()
    {
        _refreshTimer.Stop();
        _refreshTimer.Tick -= OnRefreshTimerTick;
        _refreshLock.Dispose();
    }
}

internal sealed class CachedLikedTrackIds
{
    public List<long> Ids { get; set; } = [];
}
