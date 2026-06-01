namespace YPM.Core.Models;

public sealed class MvInfo
{
    public long Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string CoverUrl { get; set; } = string.Empty;

    public long PlayCount { get; set; }

    public long Duration { get; set; }

    public long PublishTime { get; set; }

    public string? BriefDesc { get; set; }

    public List<ArtistSummary> Artists { get; set; } = [];

    public string? ArtistName { get; set; }

    public bool Subbed { get; set; }

    public long SubCount { get; set; }

    public long ShareCount { get; set; }

    public long CommentCount { get; set; }
}

public sealed class MvUrlInfo
{
    public long Id { get; set; }

    public string? Url { get; set; }

    public int R { get; set; }

    public long Size { get; set; }

    public int Code { get; set; }

    public long Expi { get; set; }

    public string? Md5 { get; set; }
}

public sealed class RelatedVideoInfo
{
    public long Vid { get; set; }

    public int Type { get; set; }

    public string Title { get; set; } = string.Empty;

    public string CoverUrl { get; set; } = string.Empty;

    public long DurationMs { get; set; }

    public long PlayTime { get; set; }

    public List<ArtistSummary> Creator { get; set; } = [];
}
