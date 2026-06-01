using Windows.Media;
using Windows.Media.Playback;
using Windows.Storage.Streams;
using YPM.Core.Models;
using YPM.Core.Services;

namespace YPM.UI.Services;

public sealed class SMTCService : ISMTCService, IDisposable
{
    private readonly IAudioPlayerService _audioPlayer;
    private readonly HttpClient _httpClient;
    private MediaPlayer? _player;
    private SystemMediaTransportControls? _controls;
    private bool _isInitialized;
    private string? _lastCoverUrl;
    private long _lastTimelineUpdateTicks;

    public SMTCService(IAudioPlayerService audioPlayer)
    {
        _audioPlayer = audioPlayer;
        _httpClient = new HttpClient();
    }

    public void Initialize()
    {
        if (_isInitialized) return;
        _isInitialized = true;

        _audioPlayer.TrackChanged += OnTrackChanged;
        _audioPlayer.StateChanged += OnStateChanged;
        _audioPlayer.PositionChanged += OnPositionChanged;
    }

    private bool TryGetPlayer()
    {
        if (_player is not null) return true;

        if (_audioPlayer is AudioPlayerService aps)
            _player = aps.MediaPlayer;

        if (_player is not null)
        {
            _controls = _player.SystemMediaTransportControls;

            var cm = _player.CommandManager;
            cm.NextBehavior.EnablingRule = MediaCommandEnablingRule.Always;
            cm.PreviousBehavior.EnablingRule = MediaCommandEnablingRule.Always;
            cm.NextReceived += OnNextReceived;
            cm.PreviousReceived += OnPreviousReceived;
            return true;
        }

        return false;
    }

    private void OnNextReceived(MediaPlaybackCommandManager sender, MediaPlaybackCommandManagerNextReceivedEventArgs args)
    {
        _ = HandleNextAsync();
    }

    private void OnPreviousReceived(MediaPlaybackCommandManager sender, MediaPlaybackCommandManagerPreviousReceivedEventArgs args)
    {
        _ = HandlePreviousAsync();
    }

    private void OnTrackChanged(object? sender, TrackInfo? track)
    {
        if (!TryGetPlayer() || _controls is null) return;

        if (track is null)
        {
            _controls.IsEnabled = false;
            return;
        }

        _controls.DisplayUpdater.AppMediaId = track.Id.ToString();

        var coverUrl = track.Album?.CoverUrl;
        if (!string.IsNullOrWhiteSpace(coverUrl) && coverUrl != _lastCoverUrl)
            _ = DownloadAndApplyThumbnailAsync(coverUrl);
    }

    private async Task DownloadAndApplyThumbnailAsync(string coverUrl)
    {
        var player = _player;
        if (player is null) return;

        _lastCoverUrl = coverUrl;

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var bytes = await _httpClient.GetByteArrayAsync(coverUrl, cts.Token);

            var stream = new InMemoryRandomAccessStream();
            using (var writer = new DataWriter(stream))
            {
                writer.WriteBytes(bytes);
                await writer.StoreAsync();
            }
            stream.Seek(0);

            var reference = RandomAccessStreamReference.CreateFromStream(stream);

            if (player.Source is MediaPlaybackItem item)
            {
                var props = item.GetDisplayProperties();
                props.Thumbnail = reference;
                item.ApplyDisplayProperties(props);
            }
        }
        catch
        {
            // Thumbnail is best-effort.
        }
    }

    private void OnStateChanged(object? sender, PlayerState state)
    {
        if (_controls is null) return;

        _controls.PlaybackStatus = state switch
        {
            PlayerState.Playing => MediaPlaybackStatus.Playing,
            PlayerState.Paused => MediaPlaybackStatus.Paused,
            PlayerState.Loading => MediaPlaybackStatus.Changing,
            PlayerState.Stopped => MediaPlaybackStatus.Stopped,
            _ => MediaPlaybackStatus.Closed,
        };

        if (state == PlayerState.Stopped)
        {
            _controls.IsEnabled = false;
            return;
        }

        _controls.IsEnabled = true;

        if (state == PlayerState.Playing && _audioPlayer.CurrentTrack is { } track)
        {
            var duration = TimeSpan.FromMilliseconds(Math.Max(0, track.Duration));
            _controls.UpdateTimelineProperties(new SystemMediaTransportControlsTimelineProperties
            {
                StartTime = TimeSpan.Zero,
                EndTime = duration,
                MinSeekTime = TimeSpan.Zero,
                MaxSeekTime = duration,
                Position = _audioPlayer.Position,
            });
        }
    }

    private void OnPositionChanged(object? sender, TimeSpan position)
    {
        if (_controls is null) return;

        var now = DateTime.UtcNow.Ticks;
        if (now - _lastTimelineUpdateTicks < TimeSpan.TicksPerSecond)
            return;
        _lastTimelineUpdateTicks = now;

        _controls.UpdateTimelineProperties(new SystemMediaTransportControlsTimelineProperties
        {
            StartTime = TimeSpan.Zero,
            EndTime = _audioPlayer.Duration,
            MinSeekTime = TimeSpan.Zero,
            MaxSeekTime = _audioPlayer.Duration,
            Position = position,
        });
    }

    private async Task HandleNextAsync()
    {
        await _audioPlayer.NextAsync();
    }

    private async Task HandlePreviousAsync()
    {
        if (_audioPlayer.Position > TimeSpan.FromSeconds(5))
            await _audioPlayer.SeekAsync(TimeSpan.Zero);
        else
            await _audioPlayer.PreviousAsync();
    }

    public void Dispose()
    {
        _audioPlayer.TrackChanged -= OnTrackChanged;
        _audioPlayer.StateChanged -= OnStateChanged;
        _audioPlayer.PositionChanged -= OnPositionChanged;

        if (_player is not null)
        {
            _player.CommandManager.NextReceived -= OnNextReceived;
            _player.CommandManager.PreviousReceived -= OnPreviousReceived;
        }

        if (_controls is not null)
        {
            _controls.IsEnabled = false;
            _controls = null;
        }

        _player = null;
        _httpClient.Dispose();
    }
}
