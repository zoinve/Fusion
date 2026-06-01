namespace YPM.Core.Models;

public sealed class AlbumDetail
{
    public long Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string CoverUrl { get; set; } = string.Empty;

    public ArtistSummary? Artist { get; set; }

    public List<ArtistSummary> Artists { get; set; } = [];

    public long PublishTime { get; set; }

    public long CompanyId { get; set; }

    public string? Company { get; set; }

    public string? Description { get; set; }

    public string? SubType { get; set; }

    public string? Type { get; set; }

    public long Size { get; set; }

    public bool Subscribed { get; set; }

    public long ShareCount { get; set; }

    public long CommentCount { get; set; }

    public List<TrackInfo> Tracks { get; set; } = [];
}

public sealed class AlbumDynamic
{
    public bool IsSub { get; set; }

    public long SubCount { get; set; }

    public long CommentCount { get; set; }

    public long ShareCount { get; set; }
}
