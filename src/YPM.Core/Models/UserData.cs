namespace YPM.Core.Models;

public sealed class UserAccountResult
{
    public long Id { get; set; }

    public string UserName { get; set; } = string.Empty;

    public int Type { get; set; }

    public int Status { get; set; }

    public int AuthStatus { get; set; }

    public long CreateTime { get; set; }

    public UserProfile? Profile { get; set; }
}

public sealed class UserSubCountResult
{
    public int DjRadioCount { get; set; }

    public int MvCount { get; set; }

    public int ArtistCount { get; set; }

    public int CreatedPlaylistCount { get; set; }

    public int SubPlaylistCount { get; set; }

    public int ProgramCount { get; set; }
}

public sealed class UserLevelResult
{
    public long UserId { get; set; }

    public int Level { get; set; }

    public int Progress { get; set; }

    public long NextPlayCount { get; set; }

    public long NextLoginCount { get; set; }

    public long NowPlayCount { get; set; }

    public long NowLoginCount { get; set; }
}

public sealed class UserPlaylistResult
{
    public List<PlaylistSummary> Playlist { get; set; } = [];

    public bool More { get; set; }

    public int Total { get; set; }
}

public sealed class UserRecordResult
{
    public List<RecordItem> WeekData { get; set; } = [];

    public List<RecordItem> AllData { get; set; } = [];
}

public sealed class RecordItem
{
    public int PlayCount { get; set; }

    public int Score { get; set; }

    public TrackInfo? Song { get; set; }
}

public sealed class UserEventResult
{
    public List<EventItem> Events { get; set; } = [];

    public bool More { get; set; }

    public long LastTime { get; set; }
}

public sealed class EventItem
{
    public long Id { get; set; }

    public int Type { get; set; }

    public long EventTime { get; set; }

    public string? Json { get; set; }
}

public sealed class UserFollowResult
{
    public List<UserProfile> Follow { get; set; } = [];

    public bool More { get; set; }

    public int Total { get; set; }
}

public sealed class UserCommentHistoryResult
{
    public List<CommentInfo> Comments { get; set; } = [];

    public bool More { get; set; }

    public long Time { get; set; }
}
