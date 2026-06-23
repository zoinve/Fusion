using Microsoft.UI.Dispatching;
using YPM.Core.Models;
using YPM.Core.Mvvm;
using YPM.Core.Services;
using YPM.UI.Helpers;

namespace YPM.UI.ViewModels;

public sealed class NowPlayingViewModel : ObservableObject, IDisposable
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
    private List<LyricLine> _lyrics = [];
    private int _currentLyricIndex = -1;
    private bool _isLyricsLoading;
    private string _audioQualitySummary = string.Empty;

    public NowPlayingViewModel(IAudioPlayerService player, ILikedSongsService? likedService, DispatcherQueue dispatcher)
    {
        _player = player;
        _likedService = likedService;
        _dispatcher = dispatcher;

        _player.TrackChanged += OnTrackChanged;
        _player.StateChanged += OnStateChanged;
        _player.PositionChanged += OnPositionChanged;
        _player.VolumeChanged += OnVolumeChanged;
        _player.ModeChanged += OnModeChanged;

        if (_likedService is not null)
        {
            _likedService.TrackLiked += OnLikedStateChanged;
            _likedService.TrackUnliked += OnLikedStateChanged;
            _likedService.Refreshed += OnLikedRefreshed;
        }
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

    public List<LyricLine> Lyrics
    {
        get => _lyrics;
        set => SetProperty(ref _lyrics, value);
    }

    public int CurrentLyricIndex
    {
        get => _currentLyricIndex;
        set => SetProperty(ref _currentLyricIndex, value);
    }

    public bool IsLyricsLoading
    {
        get => _isLyricsLoading;
        set => SetProperty(ref _isLyricsLoading, value);
    }

    public string AudioQualitySummary
    {
        get => _audioQualitySummary;
        set => SetProperty(ref _audioQualitySummary, value);
    }

    public bool HasLyrics => _lyrics.Count > 0;

    public bool IsPlaying => _state == PlayerState.Playing;

    public bool IsPaused => _state == PlayerState.Paused;

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

    public string CurrentTrackName => _currentTrack?.Name ?? string.Empty;

    public string CurrentTrackArtistsText => _currentTrack?.ArtistsText ?? string.Empty;

    public string CurrentTrackCoverUrl => _currentTrack?.Album?.CoverUrl ?? string.Empty;

    public string CurrentTrackAlbumName => _currentTrack?.Album?.Name ?? string.Empty;

    public string CurrentTrackAlbumArtist => _currentTrack?.Album?.ArtistName ?? string.Empty;

    public string VolumePercentText => $"{(int)Math.Round(_volume * 100)}%";

    public IReadOnlyList<NameValuePair> LeftMetadataLines
    {
        get
        {
            var lines = new List<NameValuePair>();
            if (_currentTrack is null)
            {
                return lines;
            }

            if (_currentTrack.Album is not null)
            {
                lines.Add(new NameValuePair("专辑", _currentTrack.Album.Name));
                if (!string.IsNullOrWhiteSpace(_currentTrack.Album.ArtistName))
                {
                    lines.Add(new NameValuePair("专辑歌手", _currentTrack.Album.ArtistName));
                }
            }

            if (!string.IsNullOrWhiteSpace(_audioQualitySummary))
            {
                lines.Add(new NameValuePair("音质", _audioQualitySummary));
            }

            return lines;
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
                {
                    await _player.PlayAsync(_currentTrack);
                }
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

    public void ToggleMute() => IsMuted = !IsMuted;

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

    public void SeekTo(double percent)
    {
        if (_duration.TotalMilliseconds > 0)
        {
            var pos = TimeSpan.FromMilliseconds(_duration.TotalMilliseconds * percent / 100);
            _ = _player.SeekAsync(pos);
        }
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

    public async Task LoadLyricsAsync()
    {
        if (_currentTrack is null)
        {
            Lyrics = [];
            UpdateLyricIndex(0);
            OnPropertyChanged(nameof(HasLyrics));
            return;
        }

        IsLyricsLoading = true;
        try
        {
            var newResult = await App.ApiClient.GetNewLyricAsync(_currentTrack.Id);
            if (string.IsNullOrWhiteSpace(newResult.Lrc))
            {
                var oldResult = await App.ApiClient.GetLyricAsync(_currentTrack.Id);
                var parsed = ParseLyricsFromOld(oldResult);
                _dispatcher.TryEnqueue(() =>
                {
                    Lyrics = parsed;
                    UpdateLyricIndex(0);
                    OnPropertyChanged(nameof(HasLyrics));
                    IsLyricsLoading = false;
                });
                return;
            }

            var parsedNew = ParseLyrics(newResult);
            _dispatcher.TryEnqueue(() =>
            {
                Lyrics = parsedNew;
                UpdateLyricIndex(0);
                OnPropertyChanged(nameof(HasLyrics));
                IsLyricsLoading = false;
            });
        }
        catch
        {
            _dispatcher.TryEnqueue(() =>
            {
                Lyrics = [];
                UpdateLyricIndex(0);
                OnPropertyChanged(nameof(HasLyrics));
                IsLyricsLoading = false;
            });
        }
    }

    public async Task LoadAudioQualityAsync()
    {
        if (_currentTrack is null)
        {
            AudioQualitySummary = string.Empty;
            OnPropertyChanged(nameof(LeftMetadataLines));
            return;
        }

        try
        {
            var targetLevel = _currentTrack.ActualQualityLevel ?? MusicQualityToLevel(App.Settings.MusicQuality);
            var detail = await App.ApiClient.GetSongMusicDetailAsync(_currentTrack.Id);
            var quality = detail?.GetQuality(targetLevel);
            var bitrate = quality?.Bitrate ?? _currentTrack.Br;
            var sampleRate = quality?.SampleRate ?? _currentTrack.ActualSr;
            var summary = FormatAudioQualitySummary(bitrate, sampleRate);

            _dispatcher.TryEnqueue(() =>
            {
                AudioQualitySummary = summary;
                OnPropertyChanged(nameof(LeftMetadataLines));
            });
        }
        catch
        {
            _dispatcher.TryEnqueue(() =>
            {
                AudioQualitySummary = FormatAudioQualitySummary(_currentTrack.Br, _currentTrack.ActualSr);
                OnPropertyChanged(nameof(LeftMetadataLines));
            });
        }
    }

    private static List<LyricLine> ParseLyrics(NewLyricResult result)
    {
        var lines = new List<LyricLine>();
        var original = ParseLrcString(result.Lrc);
        var translated = ParseLrcString(result.Tlyric);

        for (int i = 0; i < original.Count; i++)
        {
            var line = original[i];
            if (i < translated.Count)
            {
                line.TranslatedText = translated[i].Text;
            }

            lines.Add(line);
        }

        return lines;
    }

    private static List<LyricLine> ParseLyricsFromOld(LyricResult result)
    {
        var lines = new List<LyricLine>();
        var original = ParseLrcString(result.Lyric);
        var translated = ParseLrcString(result.TranslatedLyric);

        for (int i = 0; i < original.Count; i++)
        {
            var line = original[i];
            if (i < translated.Count)
            {
                line.TranslatedText = translated[i].Text;
            }

            lines.Add(line);
        }

        return lines;
    }

    private static List<LyricLine> ParseLrcString(string? lrc)
    {
        var lines = new List<LyricLine>();
        if (string.IsNullOrWhiteSpace(lrc))
        {
            return lines;
        }

        var rawLines = lrc.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        foreach (var rawLine in rawLines)
        {
            var match = System.Text.RegularExpressions.Regex.Matches(rawLine, @"\[(\d{1,3}):(\d{2})\.(\d{2,3})\]");
            if (match.Count == 0)
            {
                continue;
            }

            var text = System.Text.RegularExpressions.Regex.Replace(rawLine, @"(\[\d{1,3}:\d{2}\.\d{2,3}\])+", "").Trim();

            foreach (System.Text.RegularExpressions.Match m in match)
            {
                var min = int.Parse(m.Groups[1].Value);
                var sec = int.Parse(m.Groups[2].Value);
                var msStr = m.Groups[3].Value;
                var ms = msStr.Length == 2 ? int.Parse(msStr) * 10 : int.Parse(msStr);
                var ts = new TimeSpan(0, 0, min, sec, ms);
                lines.Add(new LyricLine { Timestamp = ts, Text = text });
            }
        }

        return lines.OrderBy(l => l.Timestamp).ToList();
    }

    private void UpdateLyricIndex(int? fallbackIndex = null)
    {
        if (_lyrics.Count == 0)
        {
            CurrentLyricIndex = -1;
            return;
        }

        var idx = -1;
        for (int i = _lyrics.Count - 1; i >= 0; i--)
        {
            if (_lyrics[i].Timestamp <= _position)
            {
                idx = i;
                break;
            }
        }

        var newIndex = idx >= 0 ? idx : (fallbackIndex ?? -1);
        if (newIndex != _currentLyricIndex)
        {
            if (_currentLyricIndex >= 0 && _currentLyricIndex < _lyrics.Count)
            {
                _lyrics[_currentLyricIndex].IsHighlighted = false;
            }

            if (newIndex >= 0 && newIndex < _lyrics.Count)
            {
                _lyrics[newIndex].IsHighlighted = true;
            }

            CurrentLyricIndex = newIndex;
        }
    }

    private static string FormatAudioQualitySummary(long bitrate, long sampleRate)
    {
        if (bitrate <= 0 && sampleRate <= 0)
        {
            return string.Empty;
        }

        var parts = new List<string>();
        if (bitrate > 0)
        {
            parts.Add($"{bitrate / 1000} kbps");
        }

        if (sampleRate > 0)
        {
            parts.Add($"{sampleRate / 1000d:0.#} kHz");
        }

        return string.Join(" / ", parts);
    }

    private static string MusicQualityToLevel(long bitrate) => bitrate switch
    {
        >= 999000 => "lossless",
        >= 320000 => "exhigh",
        >= 192000 => "higher",
        _ => "standard",
    };

    private void OnTrackChanged(object? sender, TrackInfo? track)
    {
        _dispatcher.TryEnqueue(() =>
        {
            CurrentTrack = track;
            Duration = ResolveDisplayDuration(track);
            OnPropertyChanged(nameof(LikeGlyph));
            OnPropertyChanged(nameof(LeftMetadataLines));
            OnPropertyChanged(nameof(CurrentTrackName));
            OnPropertyChanged(nameof(CurrentTrackArtistsText));
            OnPropertyChanged(nameof(CurrentTrackCoverUrl));
            OnPropertyChanged(nameof(CurrentTrackAlbumName));
            OnPropertyChanged(nameof(CurrentTrackAlbumArtist));
            OnPropertyChanged(nameof(DurationText));
            OnPropertyChanged(nameof(ProgressValue));
            _ = LoadLyricsAsync();
            _ = LoadAudioQualityAsync();
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
            UpdateLyricIndex();
        });
    }

    private void OnModeChanged(object? sender, PlayMode mode)
    {
        _dispatcher.TryEnqueue(() =>
        {
            _mode = mode;
            OnPropertyChanged(nameof(Mode));
            OnPropertyChanged(nameof(ModeGlyph));
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

    public void RefreshFromPlayer()
    {
        CurrentTrack = _player.CurrentTrack;
        State = _player.State;
        Position = _player.Position;
        Duration = ResolveDisplayDuration(_player.CurrentTrack);
        _volume = _player.Volume;
        Mode = _player.Mode;
        _isMuted = _player.IsMuted;

        OnPropertyChanged(nameof(IsPlaying));
        OnPropertyChanged(nameof(IsPaused));
        OnPropertyChanged(nameof(PlayPauseGlyph));
        OnPropertyChanged(nameof(PositionText));
        OnPropertyChanged(nameof(DurationText));
        OnPropertyChanged(nameof(ProgressValue));
        OnPropertyChanged(nameof(VolumeGlyph));
        OnPropertyChanged(nameof(VolumePercentText));
        OnPropertyChanged(nameof(ModeGlyph));
        OnPropertyChanged(nameof(LikeGlyph));
        OnPropertyChanged(nameof(LeftMetadataLines));
        OnPropertyChanged(nameof(CurrentTrackName));
        OnPropertyChanged(nameof(CurrentTrackArtistsText));
        OnPropertyChanged(nameof(CurrentTrackCoverUrl));
        OnPropertyChanged(nameof(CurrentTrackAlbumName));
        OnPropertyChanged(nameof(CurrentTrackAlbumArtist));
        _ = LoadLyricsAsync();
        _ = LoadAudioQualityAsync();
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
            if (_currentTrack is not null)
            {
                _currentTrack.IsLiked = _likedService?.IsLiked(_currentTrack.Id) ?? _currentTrack.IsLiked;
                OnPropertyChanged(nameof(LikeGlyph));
            }
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
        _player.ModeChanged -= OnModeChanged;
    }
}
