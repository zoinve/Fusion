namespace YPM.Core.Models;

public sealed class DailyRecommendSongsResult
{
    public List<TrackInfo> DailySongs { get; set; } = [];

    public List<PlaylistSummary> RecommendReasons { get; set; } = [];
}

public sealed class DailyRecommendPlaylistsResult
{
    public List<PlaylistSummary> Recommend { get; set; } = [];
}

public sealed class PersonalFmTrack : TrackInfo
{
    public string? Reason { get; set; }
}
