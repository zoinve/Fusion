using Microsoft.UI.Dispatching;
using System.ComponentModel;
using YPM.Core.Models;
using YPM.Core.Mvvm;
using YPM.Core.Services;
using YPM.UI.Helpers;

namespace YPM.UI.ViewModels;

public sealed class PlayerBarViewModel : ObservableObject, IDisposable
{
    private readonly IAudioPlayerService _player;
    private readonly ILikedSongsService? _likedService;
    private readonly DispatcherQueue _dispatcher;
    private TrackInfo? _currentTrack;
    private PlayerState _state;
    private TimeSpan _position;
    private TimeSpan _duration;
    private double _volume = 1.0;
    private PlayMode _mode;
    private bool _isMuted;
    private bool _isPlayerAvailable;

    public PlayerBarViewModel(IAudioPlayerService player, ILikedSongsService? likedService, DispatcherQueue dispatcher)
    {
        _player = player;
        _likedService = likedService;
        _dispatcher = dispatcher;

        _player.TrackChanged += OnTrackChanged;
        _player.StateChanged += OnStateChanged;
        _player.PositionChanged += OnPositionChanged;
        _player.VolumeChanged += OnVolumeChanged;
        _player.QueueChanged += OnQueueChanged;
        _player.ModeChanged += OnModeChanged;

        if (_likedService is not null)
        {
            _likedService.TrackLiked += OnLikedStateChanged;
            _likedService.TrackUnliked += OnLikedStateChanged;
            _likedService.Refreshed += OnLikedRefreshed;
        }

        SyncFromPlayer();
    }

    public TrackInfo? CurrentTrack
    {
        get => _currentTrack;
        set => SetProperty(ref _currentTrack, value);
    }

    public PlayerState State
    {
        get => _state;
        set => SetProperty(ref _state, value);
    }

    public TimeSpan Position
    {
        get => _position;
        set => SetProperty(ref _position, value);
    }

    public TimeSpan Duration
    {
        get => _duration;
        set => SetProperty(ref _duration, value);
    }

    public double Volume
    {
        get => _volume;
        set
        {
            if (SetProperty(ref _volume, value))
            {
                _player.Volume = value;
                OnPropertyChanged(nameof(VolumeGlyph));
            }
        }
    }

    public PlayMode Mode
    {
        get => _mode;
        set
        {
            if (SetProperty(ref _mode, value))
            {
                _player.Mode = value;
                OnPropertyChanged(nameof(ModeGlyph));
                OnPropertyChanged(nameof(ModeTooltip));
            }
        }
    }

    public bool IsMuted
    {
        get => _isMuted;
        set
        {
            if (SetProperty(ref _isMuted, value))
            {
                _player.IsMuted = value;
                OnPropertyChanged(nameof(VolumeGlyph));
            }
        }
    }

    public bool IsPlayerAvailable
    {
        get => _isPlayerAvailable;
        set => SetProperty(ref _isPlayerAvailable, value);
    }

    public bool IsPlaying => _state == PlayerState.Playing;

    public bool IsPaused => _state == PlayerState.Paused;

    public bool ShowPlayer => _currentTrack is not null;

    public bool ShowEmptyState => !ShowPlayer;

    public bool ShowProgressBar => _player.Queue.Count > 0;

    public IReadOnlyList<TrackInfo> QueueTracks => _player.Queue.ToList();

    public long CurrentTrackId => _currentTrack?.Id ?? 0;

    public int CurrentQueueIndex => _player.QueueIndex;

    public bool HasQueue => _player.Queue.Count > 0;

    public string CurrentTrackName => _currentTrack?.Name ?? string.Empty;

    public string CurrentTrackArtistsText => _currentTrack?.ArtistsText ?? string.Empty;

    public string CurrentTrackCoverUrl => _currentTrack?.Album?.CoverUrl ?? string.Empty;

    public string VolumePercentText => $"{(int)Math.Round(_volume * 100)}%";

    public string PositionText => $"{_position.Minutes:D2}:{_position.Seconds:D2}";

    public string DurationText => $"{_duration.Minutes:D2}:{_duration.Seconds:D2}";

    public double ProgressValue => _duration.TotalMilliseconds > 0
        ? _position.TotalMilliseconds / _duration.TotalMilliseconds * 100
        : 0;

    public string PlayPauseGlyph => _state == PlayerState.Playing ? IconGlyph.Pause : IconGlyph.Play;

    public string ModeGlyph => _mode switch
    {
        PlayMode.Single => IconGlyph.RepeatOne,
        PlayMode.Shuffle => IconGlyph.Shuffle,
        _ => IconGlyph.Repeat,
    };

    public string ModeTooltip => _mode switch
    {
        PlayMode.List => "列表循环",
        PlayMode.Single => "单曲循环",
        PlayMode.Shuffle => "随机播放",
        _ => "列表循环",
    };

    public string VolumeGlyph => _isMuted || _volume <= 0 ? IconGlyph.VolumeMute : IconGlyph.Volume;

    public string LikeGlyph
    {
        get
        {
            if (_currentTrack is null) return IconGlyph.Heart;
            var liked = _likedService is not null
                ? _likedService.IsLiked(_currentTrack.Id)
                : _currentTrack.IsLiked;
            return liked ? IconGlyph.HeartSolid : IconGlyph.Heart;
        }
    }

    public async Task PlayQueueItemAsync(TrackInfo track)
    {
        var index = _player.Queue.ToList().FindIndex(item => item.Id == track.Id);
        if (index >= 0)
        {
            await _player.PlayAsync(index);
        }
    }

    public async Task PlayPauseAsync()
    {
        switch (_state)
        {
            case PlayerState.Playing:
                await _player.PauseAsync();
                break;
            case PlayerState.Paused:
                await _player.ResumeAsync();
                break;
            default:
                if (_currentTrack is not null)
                    await _player.PlayAsync(_currentTrack);
                break;
        }
    }

    public async Task NextAsync() => await _player.NextAsync();
    public async Task PreviousAsync() => await _player.PreviousAsync();

    public void ToggleMode()
    {
        Mode = _mode switch
        {
            PlayMode.List => PlayMode.Single,
            PlayMode.Single => PlayMode.Shuffle,
            PlayMode.Shuffle => PlayMode.List,
            _ => PlayMode.List,
        };
    }

    public void ToggleMute()
    {
        IsMuted = !IsMuted;
    }

    public void SetVolumeLevel(double volume)
    {
        var normalized = Math.Clamp(volume, 0, 1);
        if (normalized > 0 && _isMuted)
        {
            IsMuted = false;
        }

        Volume = normalized;
        OnPropertyChanged(nameof(VolumePercentText));
    }

    public async Task ToggleLikeAsync()
    {
        if (_currentTrack is null) return;

        var isLiked = _likedService?.IsLiked(_currentTrack.Id) ?? _currentTrack.IsLiked;

        if (_likedService is not null)
        {
            if (isLiked)
                await _likedService.UnlikeAsync(_currentTrack.Id);
            else
                await _likedService.LikeAsync(_currentTrack.Id);
        }
        else
        {
            try
            {
                var newLiked = !isLiked;
                await App.ApiClient.LikeTrackAsync(_currentTrack.Id, newLiked);
                _currentTrack.IsLiked = newLiked;
                OnPropertyChanged(nameof(LikeGlyph));
            }
            catch { }
        }
    }

    public event PropertyChangedEventHandler? ViewModelPropertyChanged
    {
        add => PropertyChanged += value;
        remove => PropertyChanged -= value;
    }

    private void OnTrackChanged(object? sender, TrackInfo? track)
    {
        _dispatcher.TryEnqueue(() =>
        {
            CurrentTrack = track;
            Duration = ResolveDisplayDuration(track);
            IsPlayerAvailable = track is not null;
            OnPropertyChanged(nameof(ShowPlayer));
            OnPropertyChanged(nameof(ShowEmptyState));
            OnPropertyChanged(nameof(ShowProgressBar));
            OnPropertyChanged(nameof(QueueTracks));
            OnPropertyChanged(nameof(CurrentQueueIndex));
            OnPropertyChanged(nameof(HasQueue));
            OnPropertyChanged(nameof(LikeGlyph));
            OnPropertyChanged(nameof(CurrentTrackId));
            OnPropertyChanged(nameof(CurrentTrackName));
            OnPropertyChanged(nameof(CurrentTrackArtistsText));
            OnPropertyChanged(nameof(CurrentTrackCoverUrl));
            OnPropertyChanged(nameof(DurationText));
            OnPropertyChanged(nameof(ProgressValue));
        });
    }

    private void OnStateChanged(object? sender, PlayerState state)
    {
        _dispatcher.TryEnqueue(() =>
        {
            State = state;
            OnPropertyChanged(nameof(IsPlaying));
            OnPropertyChanged(nameof(IsPaused));
            OnPropertyChanged(nameof(PlayPauseGlyph));
        });
    }

    private void OnPositionChanged(object? sender, TimeSpan position)
    {
        _dispatcher.TryEnqueue(() =>
        {
            Position = position;
            Duration = ResolveDisplayDuration(_currentTrack);
            OnPropertyChanged(nameof(PositionText));
            OnPropertyChanged(nameof(DurationText));
            OnPropertyChanged(nameof(ProgressValue));
        });
    }

    private void OnVolumeChanged(object? sender, double volume)
    {
        _dispatcher.TryEnqueue(() =>
        {
            _volume = volume;
            OnPropertyChanged(nameof(Volume));
            OnPropertyChanged(nameof(VolumeGlyph));
            OnPropertyChanged(nameof(VolumePercentText));
        });
    }

    private void OnModeChanged(object? sender, PlayMode mode)
    {
        _dispatcher.TryEnqueue(() =>
        {
            _mode = mode;
            OnPropertyChanged(nameof(Mode));
            OnPropertyChanged(nameof(ModeGlyph));
            OnPropertyChanged(nameof(ModeTooltip));
        });
    }

    private void OnQueueChanged(object? sender, EventArgs e)
    {
        _dispatcher.TryEnqueue(() =>
        {
            OnPropertyChanged(nameof(ShowProgressBar));
            OnPropertyChanged(nameof(QueueTracks));
            OnPropertyChanged(nameof(CurrentQueueIndex));
            OnPropertyChanged(nameof(HasQueue));
            OnPropertyChanged(nameof(CurrentTrackName));
            OnPropertyChanged(nameof(CurrentTrackArtistsText));
            OnPropertyChanged(nameof(CurrentTrackCoverUrl));
        });
    }

    private void SyncFromPlayer()
    {
        _dispatcher.TryEnqueue(() =>
        {
            CurrentTrack = _player.CurrentTrack;
            State = _player.State;
            Position = _player.Position;
            Duration = ResolveDisplayDuration(_player.CurrentTrack);
            _volume = _player.Volume;
            _mode = _player.Mode;
            _isMuted = _player.IsMuted;
            IsPlayerAvailable = _player.CurrentTrack is not null;

            OnPropertyChanged(nameof(ShowPlayer));
            OnPropertyChanged(nameof(ShowEmptyState));
            OnPropertyChanged(nameof(ShowProgressBar));
            OnPropertyChanged(nameof(QueueTracks));
            OnPropertyChanged(nameof(CurrentQueueIndex));
            OnPropertyChanged(nameof(HasQueue));
            OnPropertyChanged(nameof(CurrentTrackId));
            OnPropertyChanged(nameof(IsPlaying));
            OnPropertyChanged(nameof(IsPaused));
            OnPropertyChanged(nameof(PlayPauseGlyph));
            OnPropertyChanged(nameof(PositionText));
            OnPropertyChanged(nameof(DurationText));
            OnPropertyChanged(nameof(ProgressValue));
            OnPropertyChanged(nameof(Volume));
            OnPropertyChanged(nameof(VolumeGlyph));
            OnPropertyChanged(nameof(VolumePercentText));
            OnPropertyChanged(nameof(ModeGlyph));
            OnPropertyChanged(nameof(ModeTooltip));
            OnPropertyChanged(nameof(LikeGlyph));
        });
    }

    private TimeSpan ResolveDisplayDuration(TrackInfo? track)
    {
        if (_player.Duration > TimeSpan.Zero)
        {
            return _player.Duration;
        }

        return track is not null && track.Duration > 0
            ? TimeSpan.FromMilliseconds(track.Duration)
            : TimeSpan.Zero;
    }

    private void OnLikedStateChanged(object? sender, long trackId)
    {
        if (_currentTrack is not null && _currentTrack.Id == trackId)
        {
            _dispatcher.TryEnqueue(() => OnPropertyChanged(nameof(LikeGlyph)));
        }
    }

    private void OnLikedRefreshed(object? sender, EventArgs e)
    {
        _dispatcher.TryEnqueue(() =>
        {
            _currentTrack!.IsLiked = _likedService?.IsLiked(_currentTrack.Id) ?? _currentTrack.IsLiked;
            OnPropertyChanged(nameof(LikeGlyph));
        });
    }

    public void Dispose()
    {
        if (_likedService is not null)
        {
            _likedService.TrackLiked -= OnLikedStateChanged;
            _likedService.TrackUnliked -= OnLikedStateChanged;
            _likedService.Refreshed -= OnLikedRefreshed;
        }

        _player.TrackChanged -= OnTrackChanged;
        _player.StateChanged -= OnStateChanged;
        _player.PositionChanged -= OnPositionChanged;
        _player.VolumeChanged -= OnVolumeChanged;
        _player.QueueChanged -= OnQueueChanged;
        _player.ModeChanged -= OnModeChanged;
    }
}
