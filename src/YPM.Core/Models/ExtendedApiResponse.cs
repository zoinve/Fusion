namespace YPM.Core.Models;

public sealed class ApiResponse<T>
{
    public int Code { get; set; }

    public string? Message { get; set; }

    public T? Data { get; set; }

    public bool IsSuccess => Code == 200;

    public bool HasMore { get; set; }

    public long Total { get; set; }
}

public sealed class SimiTrackResult
{
    public List<TrackInfo> Songs { get; set; } = [];
}

public sealed class SimiPlaylistResult
{
    public List<PlaylistSummary> Playlists { get; set; } = [];
}

public sealed class SimiMvResult
{
    public List<MvInfo> Mvs { get; set; } = [];
}

public sealed class NewestAlbumResult
{
    public List<AlbumDetail> Albums { get; set; } = [];
}

public sealed class TopAlbumResult
{
    public List<AlbumDetail> MonthData { get; set; } = [];

    public int Total { get; set; }
}

public sealed class TopListDetailResult
{
    public PlaylistDetail? Playlist { get; set; }
}

public sealed class UserCloudResult
{
    public List<TrackInfo> Data { get; set; } = [];

    public int Count { get; set; }

    public long MaxSize { get; set; }

    public long Size { get; set; }
}

public sealed class UserDjResult
{
    public List<object> DjRadios { get; set; } = [];

    public int Count { get; set; }

    public bool HasMore { get; set; }
}

public sealed class TopicDetailResult
{
    public object? Act { get; set; }

    public object? HotEvents { get; set; }
}

public sealed class EventForwardResult
{
    public int Code { get; set; }

    public string? Message { get; set; }
}

public sealed class ShareResourceResult
{
    public int Code { get; set; }

    public string? Message { get; set; }
}

public sealed class PlaymodeIntelligenceResult
{
    public List<TrackInfo> Data { get; set; } = [];

    public int Code { get; set; }
}

public sealed class CommentLikeResult
{
    public int Code { get; set; }

    public string? Message { get; set; }
}
