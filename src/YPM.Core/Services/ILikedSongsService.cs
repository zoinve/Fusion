namespace YPM.Core.Services;

public interface ILikedSongsService
{
    bool IsLiked(long trackId);
    Task<bool> LikeAsync(long trackId);
    Task<bool> UnlikeAsync(long trackId);
    Task RefreshAsync();
    IReadOnlySet<long> LikedTrackIds { get; }
    bool IsLoaded { get; }
    event EventHandler<long>? TrackLiked;
    event EventHandler<long>? TrackUnliked;
    event EventHandler? Refreshed;
}
