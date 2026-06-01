namespace YPM.Core.Models;

public sealed class CommentInfo
{
    public long CommentId { get; set; }

    public string Content { get; set; } = string.Empty;

    public long Time { get; set; }

    public long LikedCount { get; set; }

    public bool Liked { get; set; }

    public CommentUser? User { get; set; }

    public List<CommentInfo>? Replies { get; set; }

    public long ParentCommentId { get; set; }
}

public sealed class CommentUser
{
    public long UserId { get; set; }

    public string Nickname { get; set; } = string.Empty;

    public string AvatarUrl { get; set; } = string.Empty;

    public int UserType { get; set; }

    public int AuthStatus { get; set; }

    public string? VipType { get; set; }
}

public sealed class CommentResult
{
    public List<CommentInfo> Comments { get; set; } = [];

    public List<CommentInfo>? HotComments { get; set; }

    public long Total { get; set; }

    public bool More { get; set; }

    public long? Cursor { get; set; }
}
