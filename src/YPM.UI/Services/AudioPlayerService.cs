using Windows.Media;
using Windows.Media.Core;
using Windows.Media.Playback;
using YPM.Api.Abstractions;
using YPM.Core.Models;
using YPM.Core.Services;
using Microsoft.UI.Dispatching;
using YPM.UI.Extensions;

namespace YPM.UI.Services;

public sealed class AudioPlayerService : IAudioPlayerService, IDisposable
{
    private readonly INeteaseApiClient _apiClient;
    private MediaPlayer? _player;

    internal MediaPlayer? MediaPlayer => _player;
    private DispatcherQueueTimer? _timer;
    private DispatcherQueue? _dispatcherQueue;
    private readonly List<TrackInfo> _queue = [];
    private int _queueIndex = -1;
    private PlayMode _mode = PlayMode.List;
    private bool _isMuted;
    private double _volume = 1.0;
    private TrackInfo? _current;
    private PlayerState _state = PlayerState.Idle;
    private bool _initialized;

    public AudioPlayerService(INeteaseApiClient apiClient)
    {
        _apiClient = apiClient;
    }

    public event EventHandler<TrackInfo?>? TrackChanged;
    public event EventHandler<PlayerState>? StateChanged;
    public event EventHandler<TimeSpan>? PositionChanged;
    public event EventHandler<double>? VolumeChanged;
    public event EventHandler? QueueChanged;

    public PlayerState State => _state;

    public TrackInfo? CurrentTrack => _current;

    public TimeSpan Position => _player?.PlaybackSession.Position ?? TimeSpan.Zero;

    public TimeSpan Duration => _player?.PlaybackSession.NaturalDuration ?? TimeSpan.Zero;

    public double Volume
    {
        get => _volume;
        set
        {
            _volume = Math.Clamp(value, 0, 1);
            if (_player is not null) _player.Volume = _volume;
            VolumeChanged?.Invoke(this, _volume);
        }
    }

    public PlayMode Mode
    {
        get => _mode;
        set => _mode = value;
    }

    public bool IsMuted
    {
        get => _isMuted;
        set
        {
            _isMuted = value;
            if (_player is not null) _player.IsMuted = value;
        }
    }

    public IReadOnlyList<TrackInfo> Queue => _queue;

    public int QueueIndex => _queueIndex;

    private void EnsureInitialized()
    {
        if (_initialized) return;
        _initialized = true;

        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
        _player = new MediaPlayer { IsMuted = _isMuted, Volume = _volume };
        _player.MediaEnded += OnMediaEnded;
        _player.MediaFailed += OnMediaFailed;
        _player.PlaybackSession.PlaybackStateChanged += OnPlaybackStateChanged;

        _timer = _dispatcherQueue?.CreateTimer();
        if (_timer is null)
        {
            throw new InvalidOperationException("AudioPlayerService must be initialized on a thread with a DispatcherQueue.");
        }
        _timer.Interval = TimeSpan.FromMilliseconds(250);
        _timer.Tick += OnTimerTick;
    }

    public void SetQueue(IEnumerable<TrackInfo> tracks, int startIndex = 0)
    {
        _queue.Clear();
        _queue.AddRange(tracks);
        _queueIndex = _queue.Count == 0 ? -1 : Math.Clamp(startIndex, 0, _queue.Count - 1);
        QueueChanged?.Invoke(this, EventArgs.Empty);
    }

    public void AddToQueue(TrackInfo track)
    {
        _queue.Add(track);
        QueueChanged?.Invoke(this, EventArgs.Empty);
    }

    public void AddToQueueNext(TrackInfo track)
    {
        if (_queueIndex < 0) { _queue.Insert(0, track); _queueIndex = 0; }
        else _queue.Insert(_queueIndex + 1, track);
        QueueChanged?.Invoke(this, EventArgs.Empty);
    }

    public void RemoveFromQueue(int index)
    {
        if (index < 0 || index >= _queue.Count) return;
        if (index < _queueIndex) _queueIndex--;
        else if (index == _queueIndex)
        {
            _queue.RemoveAt(index);
            if (_queue.Count == 0)
            {
                _queueIndex = -1;
                QueueChanged?.Invoke(this, EventArgs.Empty);
                return;
            }
            if (_queueIndex >= _queue.Count) _queueIndex = 0;
        }
        else _queue.RemoveAt(index);
        QueueChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ClearQueue()
    {
        _queue.Clear();
        _queueIndex = -1;
        QueueChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task PlayAsync(TrackInfo track, CancellationToken cancellationToken = default)
    {
        EnsureInitialized();
        SetState(PlayerState.Loading);

        var index = _queue.FindIndex(t => t.Id == track.Id);
        if (index >= 0) _queueIndex = index;

        _current = track;
        TrackChanged?.Invoke(this, _current);

        var playbackUri = await ResolvePlaybackUriAsync(track, cancellationToken);
        if (playbackUri is null)
        {
            SetState(PlayerState.Stopped);
            return;
        }

        var source = MediaSource.CreateFromUri(playbackUri);
        var item = new MediaPlaybackItem(source);
        var props = item.GetDisplayProperties();
        props.MusicProperties.Title = track.Name;
        props.MusicProperties.Artist = track.ArtistsText;
        props.MusicProperties.AlbumTitle = track.AlbumName;
        props.MusicProperties.TrackNumber = (uint)track.TrackNumber;
        item.ApplyDisplayProperties(props);
        _player!.Source = item;
        _player.Play();
        _timer!.Start();
    }

    private async Task<Uri?> ResolvePlaybackUriAsync(TrackInfo track, CancellationToken cancellationToken)
    {
        var bitrate = App.Settings.MusicQuality;
        if (App.MusicCacheService is not null)
        {
            var cachedPath = await App.MusicCacheService.TryGetCachedFilePathAsync(track.Id, bitrate, cancellationToken);
            if (!string.IsNullOrWhiteSpace(cachedPath))
            {
                return new Uri(cachedPath);
            }
        }

        var url = track.Mp3Url;
        if (string.IsNullOrWhiteSpace(url))
        {
            url = await ResolveTrackUrlAsync(track, cancellationToken);
            if (url is not null)
            {
                track.Mp3Url = url;
            }
        }

        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        if (App.Settings.AutomaticallyCacheSongs && App.MusicCacheService is not null)
        {
            // Cache in the background so playback can start immediately.
            _ = App.MusicCacheService.CacheTrackAsync(track.Id, bitrate, url);
        }

        return new Uri(url);
    }

    private async Task<string?> ResolveTrackUrlAsync(TrackInfo track, CancellationToken cancellationToken)
    {
        try
        {
            var level = MusicQualityToLevel(App.Settings.MusicQuality);
            var urls = await _apiClient.GetSongUrlsV1Async(track.Id.ToString(), level);
            return urls.FirstOrDefault(u => u.Code == 200 && !string.IsNullOrWhiteSpace(u.Url))?.Url;
        }
        catch
        {
            return null;
        }
    }

    private static string MusicQualityToLevel(long bitrate) => bitrate switch
    {
        >= 999000 => "lossless",
        >= 320000 => "exhigh",
        >= 192000 => "higher",
        _ => "standard",
    };

    public async Task PlayAsync(int queueIndex, CancellationToken cancellationToken = default)
    {
        if (queueIndex < 0 || queueIndex >= _queue.Count) return;
        await PlayAsync(_queue[queueIndex], cancellationToken);
    }

    public async Task ResumeAsync()
    {
        EnsureInitialized();
        _player!.Play();
        _timer!.Start();
        await Task.CompletedTask;
    }

    public async Task PauseAsync()
    {
        EnsureInitialized();
        _player!.Pause();
        _timer!.Stop();
        await Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        EnsureInitialized();
        _player!.Pause();
        _player.Source = null;
        _timer!.Stop();
        SetState(PlayerState.Stopped);
        await Task.CompletedTask;
    }

    public async Task NextAsync()
    {
        if (_queue.Count == 0) return;
        var nextIndex = GetNextIndex();
        if (nextIndex < 0) return;
        await PlayAsync(nextIndex);
    }

    public async Task PreviousAsync()
    {
        if (_queue.Count == 0) return;
        EnsureInitialized();
        var prevIndex = GetPreviousIndex();
        if (prevIndex < 0) return;
        await PlayAsync(prevIndex);
    }

    public async Task SeekAsync(TimeSpan position)
    {
        EnsureInitialized();
        _player!.PlaybackSession.Position = position;
        await Task.CompletedTask;
    }

    public async Task RestoreStateAsync(PlaybackSessionState state, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (state.Queue.Count == 0 || state.QueueIndex < 0 || state.QueueIndex >= state.Queue.Count)
        {
            ClearQueue();
            return;
        }

        EnsureInitialized();

        Mode = state.Mode;
        Volume = state.Volume;
        IsMuted = state.IsMuted;
        SetQueue(state.Queue, state.QueueIndex);

        var shouldResume = state.WasPlaying;
        if (!shouldResume)
        {
            _player!.IsMuted = true;
        }

        await PlayAsync(state.QueueIndex, cancellationToken);
        await RestorePositionInternalAsync(TimeSpan.FromMilliseconds(Math.Max(0, state.PositionMilliseconds)), cancellationToken);

        if (!shouldResume)
        {
            _player!.Pause();
            _timer?.Stop();
            _player.IsMuted = _isMuted;
            SetState(PlayerState.Paused);
        }
    }

    private int GetNextIndex()
    {
        if (_queue.Count == 0) return -1;
        return _mode switch
        {
            PlayMode.Shuffle => Random.Shared.Next(_queue.Count),
            _ => (_queueIndex + 1) % _queue.Count,
        };
    }

    private int GetPreviousIndex()
    {
        if (_queue.Count == 0) return -1;
        return _mode switch
        {
            PlayMode.Shuffle => Random.Shared.Next(_queue.Count),
            _ => (_queueIndex - 1 + _queue.Count) % _queue.Count,
        };
    }

    private void OnMediaEnded(MediaPlayer sender, object args)
    {
        var dispatcherQueue = _dispatcherQueue;
        if (dispatcherQueue is null) return;
        _ = dispatcherQueue.EnqueueAsync(async () =>
        {
            _timer?.Stop();
            if (_mode == PlayMode.Single)
            {
                await PlayAsync(_queueIndex);
            }
            else
            {
                await NextAsync();
            }
        });
    }

    private void OnMediaFailed(MediaPlayer sender, MediaPlayerFailedEventArgs args)
    {
        var dispatcherQueue = _dispatcherQueue;
        if (dispatcherQueue is null) return;
        _ = dispatcherQueue.EnqueueAsync(() => SetState(PlayerState.Stopped));
    }

    private void OnPlaybackStateChanged(MediaPlaybackSession sender, object args)
    {
        var dispatcherQueue = _dispatcherQueue;
        if (dispatcherQueue is null) return;
        _ = dispatcherQueue.EnqueueAsync(() =>
        {
            var s = sender.PlaybackState switch
            {
                MediaPlaybackState.Playing => PlayerState.Playing,
                MediaPlaybackState.Paused => PlayerState.Paused,
                _ => PlayerState.Idle,
            };
            if (s != _state) SetState(s);
        });
    }

    private void OnTimerTick(DispatcherQueueTimer sender, object args)
    {
        if (_player is not null)
        {
            PositionChanged?.Invoke(this, _player.PlaybackSession.Position);
        }
    }

    private void SetState(PlayerState state)
    {
        if (_state == state) return;
        _state = state;
        StateChanged?.Invoke(this, _state);
    }

    private async Task RestorePositionInternalAsync(TimeSpan position, CancellationToken cancellationToken)
    {
        if (position <= TimeSpan.Zero)
        {
            return;
        }

        for (var attempt = 0; attempt < 10; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (_player is null)
            {
                return;
            }

            _player.PlaybackSession.Position = position;

            if (_player.PlaybackSession.NaturalDuration > TimeSpan.Zero)
            {
                return;
            }

            await Task.Delay(150, cancellationToken);
        }
    }

    public void Dispose()
    {
        if (_timer is not null)
        {
            _timer.Stop();
            _timer.Tick -= OnTimerTick;
        }
        if (_player is not null)
        {
            _player.MediaEnded -= OnMediaEnded;
            _player.MediaFailed -= OnMediaFailed;
            _player.PlaybackSession.PlaybackStateChanged -= OnPlaybackStateChanged;
            _player.Dispose();
        }
    }
}
