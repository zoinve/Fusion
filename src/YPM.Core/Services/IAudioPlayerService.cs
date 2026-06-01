using YPM.Core.Models;

namespace YPM.Core.Services;

public enum PlayMode
{
    List,
    Single,
    Shuffle,
}

public enum PlayerState
{
    Idle,
    Loading,
    Playing,
    Paused,
    Stopped,
}

public interface IAudioPlayerService
{
    event EventHandler<TrackInfo?>? TrackChanged;
    event EventHandler<PlayerState>? StateChanged;
    event EventHandler<TimeSpan>? PositionChanged;
    event EventHandler<double>? VolumeChanged;
    event EventHandler? QueueChanged;

    PlayerState State { get; }
    TrackInfo? CurrentTrack { get; }
    TimeSpan Position { get; }
    TimeSpan Duration { get; }
    double Volume { get; set; }
    PlayMode Mode { get; set; }
    bool IsMuted { get; set; }
    IReadOnlyList<TrackInfo> Queue { get; }
    int QueueIndex { get; }

    void SetQueue(IEnumerable<TrackInfo> tracks, int startIndex = 0);
    void AddToQueue(TrackInfo track);
    void AddToQueueNext(TrackInfo track);
    void RemoveFromQueue(int index);
    void ClearQueue();

    Task PlayAsync(TrackInfo track, CancellationToken cancellationToken = default);
    Task PlayAsync(int queueIndex, CancellationToken cancellationToken = default);
    Task ResumeAsync();
    Task PauseAsync();
    Task StopAsync();
    Task NextAsync();
    Task PreviousAsync();
    Task SeekAsync(TimeSpan position);
    Task RestoreStateAsync(PlaybackSessionState state, CancellationToken cancellationToken = default);
}

public sealed class TrackChangedEventArgs : EventArgs
{
    public TrackInfo? Track { get; }
    public TrackChangedEventArgs(TrackInfo? track) => Track = track;
}
