namespace YPM.Core.Models;

public sealed class PlaylistSummary
{
    public long Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string CoverUrl { get; set; } = string.Empty;

    public string? Description { get; set; }

    public string? Copywriter { get; set; }
}
