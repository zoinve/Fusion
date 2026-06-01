using System.Diagnostics;
using System.Threading;
using Microsoft.UI.Xaml;
using YPM.Api;
using YPM.Api.Abstractions;
using YPM.Core.Models;
using YPM.Core.Services;
using YPM.UI.Services;

namespace YPM.UI;

public partial class App : Application
{
    private const string AuthCacheKey = "auth/session";
    private static readonly SemaphoreSlim PlaybackStateSaveLock = new(1, 1);
    private static TimeSpan _lastPersistedPlaybackPosition = TimeSpan.MinValue;
    private static bool _playbackPersistenceAttached;
    private static bool _isRestoringPlaybackState;
    public static ISettingsService SettingsService { get; private set; } = null!;
    public static IBackendHostService BackendHostService { get; private set; } = null!;
    public static AppSettings Settings { get; private set; } = new();
    public static INeteaseApiClient ApiClient { get; private set; } = null!;
    public static IAudioPlayerService? AudioPlayer { get; set; }
    public static ILocalCacheService? CacheService { get; private set; }
    public static IMusicCacheService? MusicCacheService { get; private set; }
    public static ILikedSongsService? LikedSongsService { get; set; }
    public static INavigationService? NavigationService { get; set; }
    public static ILocalizationService? LocalizationService { get; private set; }
    public static MainWindow MainWindow { get; private set; } = null!;

    public App()
    {
        InitializeComponent();
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            Task.Run(() => PersistPlaybackStateAsync(force: true)).GetAwaiter().GetResult();
            BackendHostService?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        };
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            SettingsService = new LocalSettingsService();
            Settings = await SettingsService.LoadAsync();

            await InitializeCacheServiceAsync();
            await InitializeMusicCacheServiceAsync();
            await RestoreAuthStateFromCacheAsync();

            BackendHostService = new NodeBackendHostService(Settings.Api);
            await BackendHostService.StartAsync();
            RecreateApiClient(Settings.SessionCookie);

            await RefreshSessionIfNeededAsync();

            MainWindow = new MainWindow();
            await InitializeLikedSongsServiceAsync();
            MainWindow.Activate();
            await RestorePlaybackStateAsync();
        }
        catch (Exception ex)
        {
            LogStartupError(ex);
            throw;
        }
    }

    private static void LogStartupError(Exception ex)
    {
        try
        {
            var logDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Fusion");
            Directory.CreateDirectory(logDir);
            var logPath = Path.Combine(logDir, "startup_error.log");
            File.AppendAllText(logPath,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ex}{Environment.NewLine}");
        }
        catch
        {
            // Last resort — can't even log to disk.
        }
    }

    private async Task InitializeCacheServiceAsync()
    {
        CacheService = new FileBasedCacheService();
        await CacheService.InitializeAsync();
    }

    private async Task InitializeMusicCacheServiceAsync()
    {
        string? cacheDir = null;
        if (!string.IsNullOrWhiteSpace(Settings.CacheLocation) && Path.IsPathRooted(Settings.CacheLocation))
        {
            cacheDir = Settings.CacheLocation;
        }

        var cacheLimitMb = Settings.CacheLimit > 0 ? Settings.CacheLimit : 2048;
        MusicCacheService = new FileBasedMusicCacheService(cacheDir, cacheLimitMb * 1024 * 1024, () => Settings.SessionCookie);
        await MusicCacheService.InitializeAsync();
    }

    private static async Task InitializeLikedSongsServiceAsync()
    {
        if (Settings.CurrentUser is null || CacheService is null) return;

        try
        {
            LikedSongsService = new LikedSongsService(ApiClient, CacheService, MainWindow.DispatcherQueue);
            await ((LikedSongsService)LikedSongsService).InitializeAsync(Settings.CurrentUser.UserId);
        }
        catch
        {
            LikedSongsService = null;
        }
    }

    private static async Task RefreshSessionIfNeededAsync()
    {
        if (string.IsNullOrWhiteSpace(Settings.SessionCookie) || Settings.CurrentUser is null)
        {
            return;
        }

        // Validate the session is still valid.
        try
        {
            var currentUser = await ApiClient.GetCurrentUserAsync();
            if (currentUser is null)
            {
                // Session expired — clear it.
                Settings.SessionCookie = string.Empty;
                Settings.CurrentUser = null;
                Settings.LastCookieRefreshDay = 0;
                await SettingsService.SaveAsync(Settings);
                await ClearAuthStateCacheAsync();
                RecreateApiClient();
                return;
            }

            Settings.CurrentUser = currentUser;
        }
        catch
        {
            // Can't validate — keep the existing state for now.
            return;
        }

        // Refresh cookie once per day (mirrors web version's dailyTask).
        var today = DateTime.UtcNow.DayOfYear;
        if (Settings.LastCookieRefreshDay == today)
        {
            return;
        }

        try
        {
            var refreshResult = await ApiClient.LoginRefreshAsync();
            if (refreshResult.IsSuccess && !string.IsNullOrWhiteSpace(refreshResult.Cookie))
            {
                ApiClient.SetSessionCookie(refreshResult.Cookie);
                Settings.SessionCookie = ApiClient.ExportSessionCookie();
                Settings.LastCookieRefreshDay = today;
                await SettingsService.SaveAsync(Settings);
                await SaveAuthStateToCacheAsync();
            }
        }
        catch
        {
            // Refresh is best-effort.
        }
    }

    public static void RecreateApiClient(string? sessionCookie = null)
    {
        if (ApiClient is IDisposable disposable)
        {
            disposable.Dispose();
        }

        ApiClient = new NeteaseApiClient(Settings.Api, sessionCookie, CacheService);
    }

    public static void AttachPlaybackPersistence()
    {
        if (_playbackPersistenceAttached || AudioPlayer is null)
        {
            return;
        }

        _playbackPersistenceAttached = true;
        AudioPlayer.TrackChanged += OnPlaybackTrackChanged;
        AudioPlayer.StateChanged += OnPlaybackPlayerStateChanged;
        AudioPlayer.QueueChanged += OnPlaybackQueueChanged;
        AudioPlayer.PositionChanged += OnPlaybackPositionChanged;
    }

    public static void ReinitializeCacheService()
    {
        CacheService = new FileBasedCacheService();
        _ = CacheService.InitializeAsync();

        RecreateApiClient(Settings.SessionCookie);
    }

    public static void ReinitializeMusicCacheService()
    {
        string? cacheDir = null;
        if (!string.IsNullOrWhiteSpace(Settings.CacheLocation) && Path.IsPathRooted(Settings.CacheLocation))
        {
            cacheDir = Settings.CacheLocation;
        }

        var cacheLimitMb = Settings.CacheLimit > 0 ? Settings.CacheLimit : 2048;
        MusicCacheService = new FileBasedMusicCacheService(cacheDir, cacheLimitMb * 1024 * 1024, () => Settings.SessionCookie);
        _ = MusicCacheService.InitializeAsync();
    }

    public static async Task SaveAuthStateToCacheAsync()
    {
        if (CacheService is null)
        {
            return;
        }

        var payload = new CachedAuthState
        {
            SessionCookie = Settings.SessionCookie,
            CurrentUser = Settings.CurrentUser,
        };
        await CacheService.SetAsync(AuthCacheKey, payload, ttl: null, protectFromPrune: true);
    }

    public static async Task ClearAuthStateCacheAsync()
    {
        if (CacheService is null)
        {
            return;
        }

        await CacheService.RemoveAsync(AuthCacheKey);
    }

    private static async Task RestoreAuthStateFromCacheAsync()
    {
        if (CacheService is null)
        {
            return;
        }

        var cachedAuth = await CacheService.GetAsync<CachedAuthState>(AuthCacheKey);
        if (cachedAuth is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(Settings.SessionCookie) && !string.IsNullOrWhiteSpace(cachedAuth.SessionCookie))
        {
            Settings.SessionCookie = cachedAuth.SessionCookie;
        }

        if (Settings.CurrentUser is null && cachedAuth.CurrentUser is not null)
        {
            Settings.CurrentUser = cachedAuth.CurrentUser;
        }
    }

    public static async Task RestorePlaybackStateAsync()
    {
        if (AudioPlayer is null)
        {
            return;
        }

        AttachPlaybackPersistence();

        var lastPlayback = Settings.LastPlayback;
        if (lastPlayback is null || lastPlayback.Queue.Count == 0 || lastPlayback.QueueIndex < 0)
        {
            return;
        }

        try
        {
            _isRestoringPlaybackState = true;
            await AudioPlayer.RestoreStateAsync(new PlaybackSessionState
            {
                Queue = lastPlayback.Queue.Select(CloneTrack).ToList(),
                QueueIndex = lastPlayback.QueueIndex,
                PositionMilliseconds = lastPlayback.PositionMilliseconds,
                Mode = lastPlayback.Mode,
                Volume = lastPlayback.Volume,
                IsMuted = lastPlayback.IsMuted,
                WasPlaying = false,
            });
            _lastPersistedPlaybackPosition = TimeSpan.FromMilliseconds(Math.Max(0, lastPlayback.PositionMilliseconds));
        }
        catch
        {
            // Playback restoration is best-effort.
        }
        finally
        {
            _isRestoringPlaybackState = false;
        }
    }

    public static async Task PersistPlaybackStateAsync(bool force = false)
    {
        if (AudioPlayer is null || SettingsService is null)
        {
            return;
        }

        if (_isRestoringPlaybackState)
        {
            return;
        }

        var snapshot = BuildPlaybackSnapshot(AudioPlayer);
        var currentPosition = snapshot?.PositionMilliseconds ?? 0;
        if (!force && snapshot is not null && Math.Abs(currentPosition - _lastPersistedPlaybackPosition.TotalMilliseconds) < 5000)
        {
            return;
        }

        await PlaybackStateSaveLock.WaitAsync();
        try
        {
            Settings.LastPlayback = snapshot;
            await SettingsService.SaveAsync(Settings);
            _lastPersistedPlaybackPosition = snapshot is null
                ? TimeSpan.Zero
                : TimeSpan.FromMilliseconds(Math.Max(0, snapshot.PositionMilliseconds));
        }
        finally
        {
            PlaybackStateSaveLock.Release();
        }
    }

    private static PlaybackSessionState? BuildPlaybackSnapshot(IAudioPlayerService player)
    {
        if (player.Queue.Count == 0 || player.QueueIndex < 0 || player.CurrentTrack is null)
        {
            return null;
        }

        return new PlaybackSessionState
        {
            Queue = player.Queue.Select(CloneTrack).ToList(),
            QueueIndex = player.QueueIndex,
            PositionMilliseconds = Math.Max(0, (long)player.Position.TotalMilliseconds),
            Mode = player.Mode,
            Volume = player.Volume,
            IsMuted = player.IsMuted,
            WasPlaying = player.State is PlayerState.Playing or PlayerState.Loading,
        };
    }

    private static TrackInfo CloneTrack(TrackInfo track)
    {
        return new TrackInfo
        {
            Id = track.Id,
            Name = track.Name,
            Artists = track.Artists.Select(artist => new ArtistSummary
            {
                Id = artist.Id,
                Name = artist.Name,
                CoverUrl = artist.CoverUrl,
            }).ToList(),
            Album = track.Album is null
                ? null
                : new AlbumSummary
                {
                    Id = track.Album.Id,
                    Name = track.Album.Name,
                    CoverUrl = track.Album.CoverUrl,
                    ArtistName = track.Album.ArtistName,
                },
            Duration = track.Duration,
            IsLiked = track.IsLiked,
            Fee = track.Fee,
            Mp3Url = track.Mp3Url,
            Br = track.Br,
            DiscNumber = track.DiscNumber,
            TrackNumber = track.TrackNumber,
            DisplayIndex = track.DisplayIndex,
        };
    }

    private static async void OnPlaybackTrackChanged(object? sender, TrackInfo? track)
    {
        if (_isRestoringPlaybackState)
        {
            return;
        }

        await PersistPlaybackStateAsync(force: true);
    }

    private static async void OnPlaybackPlayerStateChanged(object? sender, PlayerState state)
    {
        if (_isRestoringPlaybackState)
        {
            return;
        }

        await PersistPlaybackStateAsync(force: true);
    }

    private static async void OnPlaybackQueueChanged(object? sender, EventArgs e)
    {
        if (_isRestoringPlaybackState)
        {
            return;
        }

        await PersistPlaybackStateAsync(force: true);
    }

    private static async void OnPlaybackPositionChanged(object? sender, TimeSpan position)
    {
        if (_isRestoringPlaybackState)
        {
            return;
        }

        if (_lastPersistedPlaybackPosition != TimeSpan.MinValue &&
            Math.Abs((position - _lastPersistedPlaybackPosition).TotalMilliseconds) < 5000)
        {
            return;
        }

        await PersistPlaybackStateAsync();
    }
}

internal sealed class CachedAuthState
{
    public string SessionCookie { get; set; } = string.Empty;
    public UserProfile? CurrentUser { get; set; }
}
