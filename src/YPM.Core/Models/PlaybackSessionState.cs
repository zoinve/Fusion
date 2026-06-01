using YPM.Core.Services;

namespace YPM.Core.Models;

public sealed class PlaybackSessionState
{
    public List<TrackInfo> Queue { get; set; } = [];

    public int QueueIndex { get; set; } = -1;

    public long PositionMilliseconds { get; set; }

    public PlayMode Mode { get; set; } = PlayMode.List;

    public double Volume { get; set; } = 1.0;

    public bool IsMuted { get; set; }

    public bool WasPlaying { get; set; }
}
