namespace YPM.Core.Models;

public sealed class LyricResult
{
    public string? Lyric { get; set; }

    public string? TranslatedLyric { get; set; }

    public string? RomanLyric { get; set; }

    public string? YrcLyric { get; set; }

    public int Version { get; set; }

    public bool IsPureMusic { get; set; }

    public bool HasYrc => !string.IsNullOrWhiteSpace(YrcLyric);
}

public sealed class NewLyricResult
{
    public string? Lrc { get; set; }

    public string? Tlyric { get; set; }

    public string? Romalrc { get; set; }

    public string? Yrc { get; set; }

    public int Version { get; set; }

    public bool IsPureMusic => string.IsNullOrWhiteSpace(Lrc?.Replace("[0:0.000]", "").Trim());
}
