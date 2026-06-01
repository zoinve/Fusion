namespace YPM.Core.Models;

public sealed class SongMusicDetailResult
{
    public Dictionary<string, TrackAudioQualityInfo> Qualities { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public TrackAudioQualityInfo? GetQuality(string? level)
    {
        if (string.IsNullOrWhiteSpace(level) || Qualities.Count == 0)
        {
            return null;
        }

        if (Qualities.TryGetValue(level, out var exact))
        {
            return exact;
        }

        var orderedLevels = new[]
        {
            "standard",
            "higher",
            "exhigh",
            "lossless",
            "hires",
            "jyeffect",
            "sky",
            "dolby",
            "jymaster",
        };

        var requestedIndex = Array.IndexOf(orderedLevels, level);
        if (requestedIndex < 0)
        {
            return Qualities.Values.OrderByDescending(q => q.Bitrate).FirstOrDefault();
        }

        for (var i = requestedIndex; i >= 0; i--)
        {
            if (Qualities.TryGetValue(orderedLevels[i], out var fallback))
            {
                return fallback;
            }
        }

        return Qualities.Values.OrderByDescending(q => q.Bitrate).FirstOrDefault();
    }
}

public sealed class TrackAudioQualityInfo
{
    public string Level { get; set; } = string.Empty;

    public long Bitrate { get; set; }

    public long SampleRate { get; set; }

    public long Size { get; set; }

    public double VolumeDelta { get; set; }
}
