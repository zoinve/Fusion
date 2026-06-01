namespace YPM.Core.Models;

public sealed class AlbumSummary
{
    public long Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string ArtistName { get; set; } = string.Empty;

    public string CoverUrl { get; set; } = string.Empty;
}
