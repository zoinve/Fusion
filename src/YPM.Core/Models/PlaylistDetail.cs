namespace YPM.Core.Models;

public sealed class PlaylistDetail
{
    public long Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string CoverUrl { get; set; } = string.Empty;

    public string? Description { get; set; }

    public UserProfile? Creator { get; set; }

    public long CreateTime { get; set; }

    public long UpdateTime { get; set; }

    public long TrackCount { get; set; }

    public long PlayCount { get; set; }

    public long SubscribedCount { get; set; }

    public long ShareCount { get; set; }

    public long CommentCount { get; set; }

    public bool Subscribed { get; set; }

    public List<string> Tags { get; set; } = [];

    public List<TrackInfo> Tracks { get; set; } = [];

    public List<long> TrackIds { get; set; } = [];
}

public sealed class PlaylistDetailDynamic
{
    public bool IsSub { get; set; }

    public long SubCount { get; set; }

    public long PlayCount { get; set; }

    public long ShareCount { get; set; }

    public long CommentCount { get; set; }
}

public sealed class PlaylistCatInfo
{
    public string Name { get; set; } = string.Empty;

    public string Category { get; set; } = string.Empty;

    public bool Hot { get; set; }
}

public sealed class PlaylistCatlistResult
{
    public List<string> All { get; set; } = [];

    public List<string> Sub { get; set; } = [];

    public Dictionary<string, string> Categories { get; set; } = new();
}

public sealed class PlaylistHighqualityTag
{
    public long Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public int Type { get; set; }

    public int Category { get; set; }

    public bool Hot { get; set; }
}

public sealed class PlaylistSubscriber
{
    public long UserId { get; set; }

    public string Nickname { get; set; } = string.Empty;

    public string AvatarUrl { get; set; } = string.Empty;

    public string Signature { get; set; } = string.Empty;

    public long Time { get; set; }
}
