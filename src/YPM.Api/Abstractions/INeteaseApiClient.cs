using YPM.Core.Models;

namespace YPM.Api.Abstractions;

public interface INeteaseApiClient
{
    string ExportSessionCookie();
    void SetSessionCookie(string cookie);

    // ── Banner & Home ──────────────────────────────────────────
    Task<IReadOnlyList<BannerItem>> GetBannersAsync(CancellationToken cancellationToken = default);
    Task<HomePageBlockResult> GetHomePageBlocksAsync(bool refresh = false, string? cursor = null, CancellationToken cancellationToken = default);
    Task<HomePageDragonBall> GetHomePageDragonBallAsync(CancellationToken cancellationToken = default);

    // ── Personalized ───────────────────────────────────────────
    Task<IReadOnlyList<PlaylistSummary>> GetRecommendedPlaylistsAsync(int limit, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AlbumSummary>> GetNewAlbumsAsync(string area, int limit, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ArtistSummary>> GetTopArtistsAsync(int? type, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PlaylistSummary>> GetTopListsAsync(CancellationToken cancellationToken = default);

    // ── Auth ───────────────────────────────────────────────────
    Task<QrLoginSession> CreateQrLoginSessionAsync(CancellationToken cancellationToken = default);
    Task<QrLoginStatus> CheckQrLoginStatusAsync(string key, CancellationToken cancellationToken = default);
    Task<LoginResult> LoginCellphoneAsync(string phone, string? password = null, string? md5Password = null, string? captcha = null, string? countryCode = null, CancellationToken cancellationToken = default);
    Task<LoginResult> LoginEmailAsync(string email, string password, string? md5Password = null, CancellationToken cancellationToken = default);
    Task<LoginResult> LoginRefreshAsync(CancellationToken cancellationToken = default);
    Task<LoginStatusResult> GetLoginStatusAsync(CancellationToken cancellationToken = default);
    Task<LoginResult> AnonimousLoginAsync(CancellationToken cancellationToken = default);
    Task LogoutAsync(CancellationToken cancellationToken = default);

    // ── Captcha ────────────────────────────────────────────────
    Task<CaptchaSentResult> SendCaptchaAsync(string phone, string? ctcode = null, CancellationToken cancellationToken = default);
    Task<CaptchaVerifyResult> VerifyCaptchaAsync(string phone, string captcha, string? ctcode = null, CancellationToken cancellationToken = default);

    // ── Cellphone ──────────────────────────────────────────────
    Task<CellphoneExistenceResult> CheckCellphoneExistenceAsync(string phone, string? countryCode = null, CancellationToken cancellationToken = default);

    // ── Register ───────────────────────────────────────────────
    Task<ApiResponse<object>> RegisterCellphoneAsync(string phone, string password, string captcha, string nickname, string? countryCode = null, CancellationToken cancellationToken = default);

    // ── User ───────────────────────────────────────────────────
    Task<UserProfile?> GetCurrentUserAsync(CancellationToken cancellationToken = default);
    Task<UserAccountResult?> GetUserAccountAsync(CancellationToken cancellationToken = default);
    Task<UserProfile?> GetUserDetailAsync(long uid, CancellationToken cancellationToken = default);
    Task<UserSubCountResult?> GetUserSubCountAsync(CancellationToken cancellationToken = default);
    Task<UserLevelResult?> GetUserLevelAsync(CancellationToken cancellationToken = default);
    Task<UserPlaylistResult> GetUserPlaylistAsync(long uid, int limit = 30, int offset = 0, CancellationToken cancellationToken = default);
    Task<UserRecordResult> GetUserRecordAsync(long uid, int type = 0, CancellationToken cancellationToken = default);
    Task<UserEventResult> GetUserEventsAsync(long uid, int limit = 30, long lasttime = -1, CancellationToken cancellationToken = default);
    Task<UserFollowResult> GetUserFollowsAsync(long uid, int limit = 30, int offset = 0, CancellationToken cancellationToken = default);
    Task<UserFollowResult> GetUserFollowedsAsync(long uid, int limit = 20, int offset = 0, CancellationToken cancellationToken = default);
    Task<UserCommentHistoryResult> GetUserCommentHistoryAsync(long uid, int limit = 10, long time = 0, CancellationToken cancellationToken = default);
    Task<UserDjResult> GetUserDjAsync(long uid, CancellationToken cancellationToken = default);
    Task<UserCloudResult> GetUserCloudAsync(int limit = 30, int offset = 0, CancellationToken cancellationToken = default);
    Task<ApiResponse<object>> UpdateUserProfileAsync(Dictionary<string, string> fields, CancellationToken cancellationToken = default);
    Task<ApiResponse<object>> FollowUserAsync(long id, bool follow, CancellationToken cancellationToken = default);

    // ── Playlist ───────────────────────────────────────────────
    Task<PlaylistDetail?> GetPlaylistDetailAsync(long id, int? s = null, bool skipCache = false, CancellationToken cancellationToken = default);
    Task<List<TrackInfo>> GetPlaylistAllTracksAsync(long id, int limit = 0, int offset = 0, bool skipCache = false, CancellationToken cancellationToken = default);
    Task<PlaylistDetailDynamic?> GetPlaylistDetailDynamicAsync(long id, CancellationToken cancellationToken = default);
    Task<ApiResponse<PlaylistDetail>> CreatePlaylistAsync(string name, string? privacy = null, string? type = null, CancellationToken cancellationToken = default);
    Task<ApiResponse<object>> DeletePlaylistAsync(string ids, CancellationToken cancellationToken = default);
    Task<ApiResponse<object>> UpdatePlaylistAsync(long id, string name, string desc, string tags, CancellationToken cancellationToken = default);
    Task<ApiResponse<object>> SubscribePlaylistAsync(long id, bool subscribe, CancellationToken cancellationToken = default);
    Task<ApiResponse<object>> AddRemoveTracksAsync(string op, long pid, string tracks, CancellationToken cancellationToken = default);
    Task<PlaylistCatlistResult> GetPlaylistCatlistAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PlaylistCatInfo>> GetHotPlaylistCatsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PlaylistSummary>> GetTopPlaylistsAsync(string? order = null, string? cat = null, int limit = 50, int offset = 0, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PlaylistSummary>> GetHighqualityPlaylistsAsync(string? cat = null, int limit = 50, long? before = null, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PlaylistHighqualityTag>> GetHighqualityTagsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PlaylistSubscriber>> GetPlaylistSubscribersAsync(long id, int limit = 20, int offset = 0, CancellationToken cancellationToken = default);
    Task<ApiResponse<object>> UpdatePlaylistCoverAsync(long id, Stream imageStream, string fileName, int? imgSize = null, CancellationToken cancellationToken = default);
    Task<ApiResponse<object>> UpdatePlaylistDescAsync(long id, string desc, CancellationToken cancellationToken = default);
    Task<ApiResponse<object>> UpdatePlaylistNameAsync(long id, string name, CancellationToken cancellationToken = default);
    Task<ApiResponse<object>> UpdatePlaylistTagsAsync(long id, string tags, CancellationToken cancellationToken = default);
    Task<ApiResponse<object>> UpdatePlaylistOrderAsync(List<long> ids, CancellationToken cancellationToken = default);
    Task<ApiResponse<object>> UpdatePlaylistPlayCountAsync(long id, CancellationToken cancellationToken = default);
    Task<ApiResponse<object>> AddVideoToPlaylistAsync(long pid, string ids, CancellationToken cancellationToken = default);
    Task<ApiResponse<object>> DeleteVideoFromPlaylistAsync(long pid, string ids, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MvInfo>> GetPlaylistVideoRecentAsync(CancellationToken cancellationToken = default);

    // ── Song / Track ───────────────────────────────────────────
    Task<IReadOnlyList<TrackUrlInfo>> GetSongUrlsAsync(string ids, long br = 999000, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TrackUrlInfo>> GetSongUrlsV1Async(string ids, string level = "exhigh", CancellationToken cancellationToken = default);
    Task<SongMusicDetailResult?> GetSongMusicDetailAsync(long id, CancellationToken cancellationToken = default);
    Task<TrackDetailResult> GetTrackDetailAsync(string ids, CancellationToken cancellationToken = default);
    Task<LyricResult> GetLyricAsync(long id, CancellationToken cancellationToken = default);
    Task<NewLyricResult> GetNewLyricAsync(long id, CancellationToken cancellationToken = default);
    Task<ApiResponse<string>> CheckMusicAsync(long id, long br = 999000, CancellationToken cancellationToken = default);
    Task<ApiResponse<object>> UpdateSongOrderAsync(long pid, List<long> ids, CancellationToken cancellationToken = default);
    Task<SimiTrackResult> GetSimiTracksAsync(long id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TrackUrlInfo>> MatchSongUrlAsync(long id, string? source = null, CancellationToken cancellationToken = default);

    // ── Album ──────────────────────────────────────────────────
    Task<AlbumDetail?> GetAlbumDetailAsync(long id, CancellationToken cancellationToken = default);
    Task<AlbumDynamic?> GetAlbumDynamicAsync(long id, CancellationToken cancellationToken = default);
    Task<ApiResponse<object>> SubscribeAlbumAsync(long id, bool subscribe, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AlbumDetail>> GetSublistAlbumsAsync(int limit = 25, int offset = 0, CancellationToken cancellationToken = default);
    Task<NewestAlbumResult> GetNewestAlbumsAsync(CancellationToken cancellationToken = default);
    Task<TopAlbumResult> GetTopAlbumsAsync(string type = "new", int limit = 30, int offset = 0, CancellationToken cancellationToken = default);

    // ── Artist ─────────────────────────────────────────────────
    Task<ArtistDetail?> GetArtistDetailAsync(long id, CancellationToken cancellationToken = default);
    Task<ArtistTopSongsResult> GetArtistTopSongsAsync(long id, CancellationToken cancellationToken = default);
    Task<ArtistSongsResult> GetArtistSongsAsync(long id, string? order = null, int limit = 50, int offset = 0, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ArtistSummary>> GetArtistListAsync(int type, int area, string? initial = null, int limit = 30, int offset = 0, CancellationToken cancellationToken = default);
    Task<ApiResponse<object>> SubscribeArtistAsync(long id, bool subscribe, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ArtistSummary>> GetSublistArtistsAsync(int limit = 25, int offset = 0, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AlbumDetail>> GetArtistAlbumsAsync(long id, int limit = 30, int offset = 0, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MvInfo>> GetArtistMvsAsync(long id, int limit = 30, int offset = 0, CancellationToken cancellationToken = default);
    Task<string?> GetArtistDescAsync(long id, CancellationToken cancellationToken = default);
    Task<SimiTrackResult> GetSimiArtistsAsync(long id, CancellationToken cancellationToken = default);

    // ── MV ─────────────────────────────────────────────────────
    Task<MvInfo?> GetMvDetailAsync(long mvid, CancellationToken cancellationToken = default);
    Task<MvUrlInfo?> GetMvUrlAsync(long id, int r = 1080, CancellationToken cancellationToken = default);
    Task<ApiResponse<object>> SubscribeMvAsync(long mvid, bool subscribe, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MvInfo>> GetSublistMvsAsync(int limit = 25, int offset = 0, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MvInfo>> GetFirstMvsAsync(string? area = null, int limit = 30, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MvInfo>> GetAllMvsAsync(string? area = null, string? type = null, string? order = null, int limit = 30, int offset = 0, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MvInfo>> GetExclusiveMvsAsync(int limit = 30, int offset = 0, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<RelatedVideoInfo>> GetRelatedVideosAsync(long id, CancellationToken cancellationToken = default);
    Task<SimiMvResult> GetSimiMvsAsync(long mvid, CancellationToken cancellationToken = default);

    // ── Search ─────────────────────────────────────────────────
    Task<SearchResult> SearchAsync(string keywords, int type = 1, int limit = 30, int offset = 0, CancellationToken cancellationToken = default);
    Task<SearchResult> CloudSearchAsync(string keywords, int type = 1, int limit = 30, int offset = 0, CancellationToken cancellationToken = default);
    Task<SearchDefaultResult?> GetSearchDefaultAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SearchHotItem>> GetSearchHotAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SearchHotItem>> GetSearchHotDetailAsync(CancellationToken cancellationToken = default);
    Task<SearchSuggestResult?> GetSearchSuggestAsync(string keywords, string? type = null, CancellationToken cancellationToken = default);
    Task<SearchResult> SearchMultimatchAsync(string keywords, CancellationToken cancellationToken = default);

    // ── Comment ────────────────────────────────────────────────
    Task<CommentResult> GetCommentMusicAsync(long id, int limit = 20, int offset = 0, long? before = null, CancellationToken cancellationToken = default);
    Task<CommentResult> GetCommentAlbumAsync(long id, int limit = 20, int offset = 0, long? before = null, CancellationToken cancellationToken = default);
    Task<CommentResult> GetCommentPlaylistAsync(long id, int limit = 20, int offset = 0, long? before = null, CancellationToken cancellationToken = default);
    Task<CommentResult> GetCommentMvAsync(long id, int limit = 20, int offset = 0, long? before = null, CancellationToken cancellationToken = default);
    Task<CommentResult> GetCommentDjAsync(long id, int limit = 20, int offset = 0, long? before = null, CancellationToken cancellationToken = default);
    Task<CommentResult> GetCommentEventAsync(string threadId, int limit = 20, int offset = 0, CancellationToken cancellationToken = default);
    Task<CommentResult> GetCommentFloorAsync(long parentCommentId, long id, int type, int limit = 20, long? time = null, CancellationToken cancellationToken = default);
    Task<CommentResult> GetCommentHotAsync(long id, int type, int limit = 20, int offset = 0, CancellationToken cancellationToken = default);
    Task<CommentLikeResult> LikeCommentAsync(long id, long cid, int type, bool like, CancellationToken cancellationToken = default);
    Task<CommentResult> SendCommentAsync(long id, int type, string content, long? commentId = null, CancellationToken cancellationToken = default);

    // ── Daily Recommendations / FM ─────────────────────────────
    Task<DailyRecommendSongsResult> GetDailyRecommendSongsAsync(CancellationToken cancellationToken = default);
    Task<DailyRecommendPlaylistsResult> GetDailyRecommendPlaylistsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PersonalFmTrack>> GetPersonalFmAsync(CancellationToken cancellationToken = default);
    Task<ApiResponse<object>> ScrobbleV1Async(
        long id,
        long time,
        long? sourceId = null,
        string? source = null,
        string? name = null,
        string? artist = null,
        long? bitrate = null,
        string? level = null,
        long? total = null,
        CancellationToken cancellationToken = default);
    Task<ApiResponse<object>> SubmitPlayStateAsync(
        long id,
        string? sessionId = null,
        long? progress = null,
        string? playMode = null,
        string? type = null,
        CancellationToken cancellationToken = default);

    // ── Liked / Library ────────────────────────────────────────
    Task<IReadOnlyList<long>> GetLikedTrackIdsAsync(long uid, CancellationToken cancellationToken = default);
    Task<ApiResponse<object>> LikeTrackAsync(long id, bool like, CancellationToken cancellationToken = default);
    Task<bool> CheckMusicLikedAsync(long id, CancellationToken cancellationToken = default);

    // ── Top List ───────────────────────────────────────────────
    Task<TopListDetailResult?> GetTopListDetailAsync(long id, CancellationToken cancellationToken = default);

    // ── Top Songs (新歌速递) ────────────────────────────────────
    Task<IReadOnlyList<TrackInfo>> GetTopSongsAsync(int type = 0, CancellationToken cancellationToken = default);

    // ── Event / Share ──────────────────────────────────────────
    Task<ApiResponse<object>> ForwardEventAsync(long uid, long evId, string forwards, CancellationToken cancellationToken = default);
    Task<ApiResponse<object>> DeleteEventAsync(long evId, CancellationToken cancellationToken = default);
    Task<ApiResponse<object>> ShareResourceAsync(string id, string type, string? msg = null, CancellationToken cancellationToken = default);

    // ── Hot Topic ──────────────────────────────────────────────
    Task<IReadOnlyList<object>> GetHotTopicsAsync(int limit = 20, int offset = 0, CancellationToken cancellationToken = default);
    Task<TopicDetailResult?> GetTopicDetailAsync(long actid, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<object>> GetTopicDetailEventHotAsync(long actid, CancellationToken cancellationToken = default);

    // ── Intelligence ───────────────────────────────────────────
    Task<PlaymodeIntelligenceResult> GetPlaymodeIntelligenceAsync(long id, long pid, long? sid = null, CancellationToken cancellationToken = default);

    // ── Last.fm ────────────────────────────────────────────────
    Task<ApiResponse<object>> LastfmLoginAsync(string token, CancellationToken cancellationToken = default);
    Task<ApiResponse<object>> LastfmCallbackAsync(string token, CancellationToken cancellationToken = default);
    Task<ApiResponse<object>> LastfmScrobbleAsync(string artist, string track, long timestamp, string? album = null, CancellationToken cancellationToken = default);
    Task<ApiResponse<object>> LastfmNowPlayingAsync(string artist, string track, string? album = null, CancellationToken cancellationToken = default);

    // ── Countries ──────────────────────────────────────────────
    Task<IReadOnlyList<object>> GetCountriesCodeListAsync(CancellationToken cancellationToken = default);
}
