namespace YPM.Core.Models;

public sealed class UserProfile
{
    public long UserId { get; set; }

    public string Nickname { get; set; } = string.Empty;

    public string AvatarUrl { get; set; } = string.Empty;
}
