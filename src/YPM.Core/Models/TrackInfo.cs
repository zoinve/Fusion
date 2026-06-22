namespace YPM.Core.Models;

public class TrackInfo
{
    public long Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public List<ArtistSummary> Artists { get; set; } = [];

    public AlbumSummary? Album { get; set; }

    public long Duration { get; set; }

    public bool IsLiked { get; set; }

    public int Fee { get; set; }

    public string? Mp3Url { get; set; }

    public long Br { get; set; }

    public int DiscNumber { get; set; }

    public int TrackNumber { get; set; }

    public int DisplayTrackNumber => TrackNumber;

    public string DurationText => TimeSpan.FromMilliseconds(Duration).ToString(Duration >= 3600000 ? @"h\:mm\:ss" : @"m\:ss");

    public string ArtistsText => string.Join(" / ", Artists.Select(a => a.Name));

    public string AlbumName => Album?.Name ?? string.Empty;

    public string ListCoverUrl { get; set; } = string.Empty;

    public int DisplayIndex { get; set; }

    public string LikeGlyph => IsLiked ? "" : "";
}

public sealed class TrackUrlInfo
{
    public long Id { get; set; }

    public string? Url { get; set; }

    public long Br { get; set; }

    public long Size { get; set; }

    public string? Type { get; set; }

    public int Code { get; set; }

    public long Expi { get; set; }

    public int Fee { get; set; }

    public int Payed { get; set; }

    public bool CanExtend { get; set; }

    public string? Md5 { get; set; }
}

public sealed class TrackDetailResult
{
    public List<TrackInfo> Songs { get; set; } = [];

    public int Total { get; set; }
}
