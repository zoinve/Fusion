namespace YPM.Core.Models;

public sealed class ArtistDetail
{
    public long Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string CoverUrl { get; set; } = string.Empty;

    public List<string> Alias { get; set; } = [];

    public long AlbumSize { get; set; }

    public long MusicSize { get; set; }

    public long MvSize { get; set; }

    public string? BriefDesc { get; set; }

    public bool Followed { get; set; }

    public int Identity { get; set; }

    public List<string> Identities { get; set; } = [];
}

public sealed class ArtistTopSongsResult
{
    public List<TrackInfo> Songs { get; set; } = [];
}

public sealed class ArtistSongsResult
{
    public List<TrackInfo> Songs { get; set; } = [];

    public int Total { get; set; }

    public bool More { get; set; }
}
