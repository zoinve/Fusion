using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Web;
using YPM.Api.Abstractions;
using YPM.Core.Models;
using YPM.Core.Services;

namespace YPM.Api;

public sealed class NeteaseApiClient : INeteaseApiClient, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly HttpClient _noRedirectHttpClient;
    private readonly CookieContainer _cookies;
    private readonly ApiOptions _options;
    private readonly ILocalCacheService? _cacheService;
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly SemaphoreSlim _restartLock = new(1, 1);
    private static DateTimeOffset _lastRestartTime = DateTimeOffset.MinValue;

    // The MUSIC_U cookie value, used as a query parameter on every request
    // (matching the original YesPlayMusic project's request interceptor).
    private string _musicU = string.Empty;

    public NeteaseApiClient(ApiOptions options, string? sessionCookie = null, ILocalCacheService? cacheService = null)
    {
        _options = options;
        _cacheService = cacheService;
        _cookies = new CookieContainer();

        var handler = new HttpClientHandler
        {
            CookieContainer = _cookies,
            UseCookies = true,
            AllowAutoRedirect = true,
            AutomaticDecompression = DecompressionMethods.All,
        };

        _httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri(options.BaseUrl.TrimEnd('/') + "/"),
            Timeout = TimeSpan.FromSeconds(15),
        };

        var noRedirectHandler = new HttpClientHandler
        {
            CookieContainer = _cookies,
            UseCookies = true,
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.All,
        };

        _noRedirectHttpClient = new HttpClient(noRedirectHandler)
        {
            BaseAddress = _httpClient.BaseAddress,
            Timeout = TimeSpan.FromSeconds(15),
        };

        EnsureDefaultClientCookies();

        if (!string.IsNullOrWhiteSpace(sessionCookie))
        {
            SetSessionCookie(sessionCookie);
        }
    }

    public string ExportSessionCookie() => _cookies.GetCookieHeader(_httpClient.BaseAddress!);

    public string ExportMusicU() => _musicU;

    public void SetSessionCookie(string cookie)
    {
        if (string.IsNullOrWhiteSpace(cookie))
        {
            return;
        }

        var parts = SplitCookieParts(cookie);

        foreach (var part in parts)
        {
            var trimmed = part.Trim();
            if (trimmed.Length == 0) continue;

            try
            {
                _cookies.SetCookies(_httpClient.BaseAddress!, trimmed);
            }
            catch
            {
                // Ignore invalid cookie parts
            }
            ExtractMusicU(trimmed);
        }

        EnsureDefaultClientCookies();
    }

    private void EnsureDefaultClientCookies()
    {
        var baseUri = _httpClient.BaseAddress;
        if (baseUri is null)
        {
            return;
        }

        try
        {
            _cookies.Add(baseUri, new Cookie("os", "pc", "/"));
        }
        catch
        {
            // Ignore failures setting compatibility cookies.
        }
    }

    private static IEnumerable<string> SplitCookieParts(string cookie)
    {
        var normalized = cookie.Replace(" HTTPOnly", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace(";HTTPOnly", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("; HttpOnly", string.Empty, StringComparison.OrdinalIgnoreCase);

        if (normalized.Contains(";;", StringComparison.Ordinal))
        {
            return normalized.Split([";;"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(ExtractCookieKeyValue)
                .Where(static part => !string.IsNullOrWhiteSpace(part));
        }

        return normalized.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(static part => part.Trim())
            .Where(static part => part.Contains('='))
            .Where(static part => !IsCookieAttribute(part))
            .Select(ExtractCookieKeyValue)
            .Where(static part => !string.IsNullOrWhiteSpace(part));
    }

    private static string ExtractCookieKeyValue(string part)
    {
        var firstSegment = part.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
        return firstSegment ?? string.Empty;
    }

    private static bool IsCookieAttribute(string part)
    {
        var eq = part.IndexOf('=');
        var key = eq > 0 ? part[..eq].Trim() : part.Trim();
        return key.Equals("path", StringComparison.OrdinalIgnoreCase)
            || key.Equals("domain", StringComparison.OrdinalIgnoreCase)
            || key.Equals("expires", StringComparison.OrdinalIgnoreCase)
            || key.Equals("max-age", StringComparison.OrdinalIgnoreCase)
            || key.Equals("samesite", StringComparison.OrdinalIgnoreCase)
            || key.Equals("secure", StringComparison.OrdinalIgnoreCase)
            || key.Equals("httponly", StringComparison.OrdinalIgnoreCase);
    }

    private void ExtractMusicU(string cookiePart)
    {
        // A part might still contain multiple cookies if they were semicolon-separated and we didn't split correctly
        // but we already split by ';' above. So each part is likely "name=value".
        var eq = cookiePart.IndexOf('=');
        if (eq > 0)
        {
            var key = cookiePart[..eq].Trim();
            if (key == "MUSIC_U")
            {
                var val = cookiePart[(eq + 1)..].Trim();
                // Strip trailing attributes if any (though unlikely if already split by ';')
                var sc = val.IndexOf(';');
                _musicU = sc > 0 ? val[..sc].Trim() : val;
            }
        }
    }

    // ═══════════════════════════════════════════════════════════
    //  Banner & Home
    // ═══════════════════════════════════════════════════════════

    public async Task<IReadOnlyList<BannerItem>> GetBannersAsync(CancellationToken cancellationToken = default)
    {
        var root = await GetJsonAsync("banner", null, cancellationToken);
        return root["banners"]?.AsArray()
            .Select(item => new BannerItem
            {
                ImageUrl = item?["imageUrl"]?.GetValue<string>(),
                TitleColor = item?["titleColor"]?.GetValue<string>(),
                TypeTitle = item?["typeTitle"]?.GetValue<string>(),
                Url = item?["url"]?.GetValue<string>(),
                TargetId = item?["targetId"]?.ToString(),
            })
            .Where(item => !string.IsNullOrWhiteSpace(item.ImageUrl))
            .ToList() ?? [];
    }

    public async Task<HomePageBlockResult> GetHomePageBlocksAsync(bool refresh = false, string? cursor = null, CancellationToken cancellationToken = default)
    {
        var query = new Dictionary<string, string?> { ["refresh"] = refresh.ToString().ToLowerInvariant() };
        if (cursor is not null) query["cursor"] = cursor;
        var root = await GetJsonAsync("homepage/block/page", query, cancellationToken);
        return DeserializeFromNode<HomePageBlockResult>(root["data"]) ?? new HomePageBlockResult();
    }

    public async Task<HomePageDragonBall> GetHomePageDragonBallAsync(CancellationToken cancellationToken = default)
    {
        var root = await GetJsonAsync("homepage/dragon/ball", null, cancellationToken);
        return DeserializeFromNode<HomePageDragonBall>(root["data"]) ?? new HomePageDragonBall();
    }

    // ═══════════════════════════════════════════════════════════
    //  Personalized
    // ═══════════════════════════════════════════════════════════

    public async Task<IReadOnlyList<PlaylistSummary>> GetRecommendedPlaylistsAsync(int limit, CancellationToken cancellationToken = default)
    {
        var root = await GetJsonAsync("personalized", new Dictionary<string, string?>
        {
            ["limit"] = limit.ToString(),
        }, cancellationToken);

        return root["result"]?.AsArray()
            .Select(MapPlaylist)
            .Where(static item => item is not null)
            .Cast<PlaylistSummary>()
            .ToList() ?? [];
    }

    public async Task<IReadOnlyList<AlbumSummary>> GetNewAlbumsAsync(string area, int limit, CancellationToken cancellationToken = default)
    {
        var root = await GetJsonAsync("album/new", new Dictionary<string, string?>
        {
            ["area"] = area,
            ["limit"] = limit.ToString(),
        }, cancellationToken);

        return root["albums"]?.AsArray()
            .Select(item => new AlbumSummary
            {
                Id = item?["id"]?.GetValue<long>() ?? 0,
                Name = item?["name"]?.GetValue<string>() ?? string.Empty,
                CoverUrl = item?["picUrl"]?.GetValue<string>() ?? string.Empty,
                ArtistName = item?["artist"]?["name"]?.GetValue<string>()
                    ?? item?["artists"]?.AsArray().FirstOrDefault()?["name"]?.GetValue<string>()
                    ?? string.Empty,
            })
            .Where(item => item.Id != 0)
            .ToList() ?? [];
    }

    public async Task<IReadOnlyList<ArtistSummary>> GetTopArtistsAsync(int? type, CancellationToken cancellationToken = default)
    {
        var query = new Dictionary<string, string?>();
        if (type.HasValue) query["type"] = type.Value.ToString();
        var root = await GetJsonAsync("toplist/artist", query, cancellationToken);
        return root["list"]?["artists"]?.AsArray()
            .Select(item => new ArtistSummary
            {
                Id = item?["id"]?.GetValue<long>() ?? 0,
                Name = item?["name"]?.GetValue<string>() ?? string.Empty,
                CoverUrl = item?["picUrl"]?.GetValue<string>() ?? string.Empty,
            })
            .Where(item => item.Id != 0)
            .Take(6)
            .ToList() ?? [];
    }

    public async Task<IReadOnlyList<PlaylistSummary>> GetTopListsAsync(CancellationToken cancellationToken = default)
    {
        var ids = new HashSet<long> { 19723756, 180106, 60198, 3812895, 60131 };
        var root = await GetJsonAsync("toplist", null, cancellationToken);
        return root["list"]?.AsArray()
            .Where(item => ids.Contains(item?["id"]?.GetValue<long>() ?? 0))
            .Select(MapPlaylist)
            .Where(static item => item is not null)
            .Cast<PlaylistSummary>()
            .ToList() ?? [];
    }

    // ═══════════════════════════════════════════════════════════
    //  Auth
    // ═══════════════════════════════════════════════════════════

    public async Task<QrLoginSession> CreateQrLoginSessionAsync(CancellationToken cancellationToken = default)
    {
        var keyRoot = await GetJsonAsync("login/qr/key", TimestampQuery(), cancellationToken);
        var key = keyRoot["data"]?["unikey"]?.GetValue<string>() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(key))
            throw new InvalidOperationException("QR login key was not returned by the API.");

        var createQuery = TimestampQuery();
        createQuery["key"] = key;
        createQuery["qrimg"] = "true";

        var createRoot = await GetJsonAsync("login/qr/create", createQuery, cancellationToken);
        return new QrLoginSession
        {
            Key = key,
            QrImageBase64 = createRoot["data"]?["qrimg"]?.GetValue<string>() ?? string.Empty,
            QrUrl = createRoot["data"]?["qrurl"]?.GetValue<string>() ?? string.Empty,
        };
    }

    public async Task<QrLoginStatus> CheckQrLoginStatusAsync(string key, CancellationToken cancellationToken = default)
    {
        var query = TimestampQuery();
        query["key"] = key;
        var root = await GetJsonAsync("login/qr/check", query, cancellationToken);
        return new QrLoginStatus
        {
            Code = root["code"]?.GetValue<int>() ?? -1,
            Message = root["message"]?.GetValue<string>() ?? string.Empty,
            Cookie = root["cookie"]?.GetValue<string>(),
        };
    }

    public async Task<LoginResult> LoginCellphoneAsync(string phone, string? password = null, string? md5Password = null, string? captcha = null, string? countryCode = null, CancellationToken cancellationToken = default)
    {
        var query = new Dictionary<string, string?> { ["phone"] = phone };
        if (password is not null) query["password"] = password;
        if (md5Password is not null) query["md5_password"] = md5Password;
        if (captcha is not null) query["captcha"] = captcha;
        if (countryCode is not null) query["countrycode"] = countryCode;

        var root = await GetJsonAsync("login/cellphone", query, cancellationToken);
        return ParseLoginResult(root);
    }

    public async Task<LoginResult> LoginEmailAsync(string email, string password, string? md5Password = null, CancellationToken cancellationToken = default)
    {
        var query = new Dictionary<string, string?> { ["email"] = email };
        query["password"] = md5Password ?? password;
        if (md5Password is not null) query["md5_password"] = md5Password;

        var root = await GetJsonAsync("login", query, cancellationToken);
        return ParseLoginResult(root);
    }

    public async Task<LoginResult> LoginRefreshAsync(CancellationToken cancellationToken = default)
    {
        var root = await GetJsonAsync("login/refresh", TimestampQuery(), cancellationToken);
        return ParseLoginResult(root);
    }

    public async Task<LoginStatusResult> GetLoginStatusAsync(CancellationToken cancellationToken = default)
    {
        var root = await GetJsonAsync("login/status", TimestampQuery(), cancellationToken);
        return new LoginStatusResult
        {
            Code = root["code"]?.GetValue<int>() ?? -1,
            Profile = DeserializeFromNode<UserProfile>(root["data"]?["profile"]),
            Account = DeserializeFromNode<UserAccountResult>(root["data"]?["account"]),
        };
    }

    public async Task<LoginResult> AnonimousLoginAsync(CancellationToken cancellationToken = default)
    {
        var root = await GetJsonAsync("register/anonimous", TimestampQuery(), cancellationToken);
        return ParseLoginResult(root);
    }

    public async Task LogoutAsync(CancellationToken cancellationToken = default)
    {
        await GetJsonAsync("logout", TimestampQuery(), cancellationToken);
    }

    // ═══════════════════════════════════════════════════════════
    //  Captcha
    // ═══════════════════════════════════════════════════════════

    public async Task<CaptchaSentResult> SendCaptchaAsync(string phone, string? ctcode = null, CancellationToken cancellationToken = default)
    {
        var query = new Dictionary<string, string?> { ["phone"] = phone };
        if (ctcode is not null) query["ctcode"] = ctcode;
        var root = await GetJsonAsync("captcha/sent", query, cancellationToken);
        return new CaptchaSentResult
        {
            Code = root["code"]?.GetValue<int>() ?? -1,
            Data = root["data"]?.GetValue<bool>() ?? false,
            Message = root["message"]?.GetValue<string>(),
        };
    }

    public async Task<CaptchaVerifyResult> VerifyCaptchaAsync(string phone, string captcha, string? ctcode = null, CancellationToken cancellationToken = default)
    {
        var query = new Dictionary<string, string?> { ["phone"] = phone, ["captcha"] = captcha };
        if (ctcode is not null) query["ctcode"] = ctcode;
        var root = await GetJsonAsync("captcha/verify", query, cancellationToken);
        return new CaptchaVerifyResult
        {
            Code = root["code"]?.GetValue<int>() ?? -1,
            Data = root["data"]?.GetValue<bool>() ?? false,
            Message = root["message"]?.GetValue<string>(),
        };
    }

    // ═══════════════════════════════════════════════════════════
    //  Cellphone / Register
    // ═══════════════════════════════════════════════════════════

    public async Task<CellphoneExistenceResult> CheckCellphoneExistenceAsync(string phone, string? countryCode = null, CancellationToken cancellationToken = default)
    {
        var query = new Dictionary<string, string?> { ["phone"] = phone };
        if (countryCode is not null) query["countrycode"] = countryCode;
        var root = await GetJsonAsync("cellphone/existence/check", query, cancellationToken);
        return new CellphoneExistenceResult
        {
            Code = root["code"]?.GetValue<int>() ?? -1,
            Exists = root["exist"]?.GetValue<int>() == 1,
            Message = root["message"]?.GetValue<string>(),
        };
    }

    public async Task<ApiResponse<object>> RegisterCellphoneAsync(string phone, string password, string captcha, string nickname, string? countryCode = null, CancellationToken cancellationToken = default)
    {
        var query = new Dictionary<string, string?>
        {
            ["phone"] = phone, ["password"] = password, ["captcha"] = captcha, ["nickname"] = nickname,
        };
        if (countryCode is not null) query["countrycode"] = countryCode;
        var root = await GetJsonAsync("register/cellphone", query, cancellationToken);
        return ParseApiResponse(root);
    }

    // ═══════════════════════════════════════════════════════════
    //  User
    // ═══════════════════════════════════════════════════════════

    public async Task<UserProfile?> GetCurrentUserAsync(CancellationToken cancellationToken = default)
    {
        var root = await GetJsonAsync("user/account", TimestampQuery(), cancellationToken);
        var profile = root["profile"];
        if (profile is null) return null;
        return new UserProfile
        {
            UserId = profile["userId"]?.GetValue<long>() ?? 0,
            Nickname = profile["nickname"]?.GetValue<string>() ?? string.Empty,
            AvatarUrl = profile["avatarUrl"]?.GetValue<string>() ?? string.Empty,
        };
    }

    public async Task<UserAccountResult?> GetUserAccountAsync(CancellationToken cancellationToken = default)
    {
        var root = await GetJsonAsync("user/account", TimestampQuery(), cancellationToken);
        return DeserializeFromNode<UserAccountResult>(root);
    }

    public async Task<UserProfile?> GetUserDetailAsync(long uid, CancellationToken cancellationToken = default)
    {
        var root = await GetJsonAsync("user/detail", new Dictionary<string, string?> { ["uid"] = uid.ToString() }, cancellationToken);
        return DeserializeFromNode<UserProfile>(root["profile"]);
    }

    public async Task<UserSubCountResult?> GetUserSubCountAsync(CancellationToken cancellationToken = default)
    {
        var root = await GetJsonAsync("user/subcount", TimestampQuery(), cancellationToken);
        return DeserializeFromNode<UserSubCountResult>(root);
    }

    public async Task<UserLevelResult?> GetUserLevelAsync(CancellationToken cancellationToken = default)
    {
        var root = await GetJsonAsync("user/level", TimestampQuery(), cancellationToken);
        return DeserializeFromNode<UserLevelResult>(root["data"]);
    }

    public async Task<UserPlaylistResult> GetUserPlaylistAsync(long uid, int limit = 30, int offset = 0, CancellationToken cancellationToken = default)
    {
        var root = await GetJsonAsync("user/playlist", new Dictionary<string, string?>
        {
            ["uid"] = uid.ToString(), ["limit"] = limit.ToString(), ["offset"] = offset.ToString(),
        }, cancellationToken);
        return new UserPlaylistResult
        {
            Playlist = root["playlist"]?.AsArray().Select(MapPlaylist).Where(x => x is not null).Cast<PlaylistSummary>().ToList() ?? [],
            More = root["more"]?.GetValue<bool>() ?? false,
            Total = root["total"]?.GetValue<int>() ?? 0,
        };
    }

    public async Task<UserRecordResult> GetUserRecordAsync(long uid, int type = 0, CancellationToken cancellationToken = default)
    {
        var root = await GetJsonAsync("user/record", new Dictionary<string, string?>
        {
            ["uid"] = uid.ToString(), ["type"] = type.ToString(),
        }, cancellationToken);
        return new UserRecordResult
        {
            WeekData = root["weekData"]?.AsArray().Select(MapRecordItemFromNode).Where(x => x is not null).Cast<RecordItem>().ToList() ?? [],
            AllData = root["allData"]?.AsArray().Select(MapRecordItemFromNode).Where(x => x is not null).Cast<RecordItem>().ToList() ?? [],
        };
    }

    public async Task<UserEventResult> GetUserEventsAsync(long uid, int limit = 30, long lasttime = -1, CancellationToken cancellationToken = default)
    {
        var root = await GetJsonAsync("user/event", new Dictionary<string, string?>
        {
            ["uid"] = uid.ToString(), ["limit"] = limit.ToString(), ["lasttime"] = lasttime.ToString(),
        }, cancellationToken);
        return DeserializeFromNode<UserEventResult>(root) ?? new UserEventResult();
    }

    public async Task<UserFollowResult> GetUserFollowsAsync(long uid, int limit = 30, int offset = 0, CancellationToken cancellationToken = default)
    {
        var root = await GetJsonAsync("user/follows", new Dictionary<string, string?>
        {
            ["uid"] = uid.ToString(), ["limit"] = limit.ToString(), ["offset"] = offset.ToString(),
        }, cancellationToken);
        return ParseFollowResult(root);
    }

    public async Task<UserFollowResult> GetUserFollowedsAsync(long uid, int limit = 20, int offset = 0, CancellationToken cancellationToken = default)
    {
        var root = await GetJsonAsync("user/followeds", new Dictionary<string, string?>
        {
            ["uid"] = uid.ToString(), ["limit"] = limit.ToString(), ["offset"] = offset.ToString(),
        }, cancellationToken);
        return ParseFollowResult(root);
    }

    public async Task<UserCommentHistoryResult> GetUserCommentHistoryAsync(long uid, int limit = 10, long time = 0, CancellationToken cancellationToken = default)
    {
        var root = await GetJsonAsync("user/comment/history", new Dictionary<string, string?>
        {
            ["uid"] = uid.ToString(), ["limit"] = limit.ToString(), ["time"] = time.ToString(),
        }, cancellationToken);
        return DeserializeFromNode<UserCommentHistoryResult>(root["data"]) ?? new UserCommentHistoryResult();
    }

    public async Task<UserDjResult> GetUserDjAsync(long uid, CancellationToken cancellationToken = default)
    {
        var root = await GetJsonAsync("user/dj", new Dictionary<string, string?> { ["uid"] = uid.ToString() }, cancellationToken);
        return new UserDjResult
        {
            DjRadios = root["data"]?["djRadios"]?.AsArray().Select(x => (object)x!).ToList() ?? [],
            Count = root["count"]?.GetValue<int>() ?? 0,
            HasMore = root["hasMore"]?.GetValue<bool>() ?? false,
        };
    }

    public async Task<UserCloudResult> GetUserCloudAsync(int limit = 30, int offset = 0, CancellationToken cancellationToken = default)
    {
        var query = new Dictionary<string, string?>
        {
            ["limit"] = limit.ToString(), ["offset"] = offset.ToString(), ["timestamp"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(),
        };
        var root = await GetJsonAsync("user/cloud", query, cancellationToken);
        return new UserCloudResult
        {
            Data = root["data"]?.AsArray().Select(MapTrackFromNode).Where(x => x is not null).Cast<TrackInfo>().ToList() ?? [],
            Count = GetInt32(root["count"]),
            MaxSize = GetInt64(root["maxSize"]),
            Size = GetInt64(root["size"]),
        };
    }

    public async Task<ApiResponse<object>> UpdateUserProfileAsync(Dictionary<string, string> fields, CancellationToken cancellationToken = default)
    {
        var query = new Dictionary<string, string?>(fields.Select(kv => new KeyValuePair<string, string?>(kv.Key, kv.Value)));
        var root = await GetJsonAsync("user/update", query, cancellationToken);
        return ParseApiResponse(root);
    }

    public async Task<ApiResponse<object>> FollowUserAsync(long id, bool follow, CancellationToken cancellationToken = default)
    {
        var root = await GetJsonAsync("follow", new Dictionary<string, string?>
        {
            ["id"] = id.ToString(), ["t"] = follow ? "1" : "0",
        }, cancellationToken);
        return ParseApiResponse(root);
    }

    // ═══════════════════════════════════════════════════════════
    //  Playlist
    // ═══════════════════════════════════════════════════════════

    public async Task<PlaylistDetail?> GetPlaylistDetailAsync(long id, int? s = null, bool skipCache = false, CancellationToken cancellationToken = default)
    {
        var query = new Dictionary<string, string?> { ["id"] = id.ToString() };
        if (s.HasValue) query["s"] = s.Value.ToString();
        var root = await GetJsonAsync("playlist/detail", query, cancellationToken, skipCache);
        return MapPlaylistDetail(root["playlist"]);
    }

    public async Task<List<TrackInfo>> GetPlaylistAllTracksAsync(long id, int limit = 0, int offset = 0, bool skipCache = false, CancellationToken cancellationToken = default)
    {
        var query = new Dictionary<string, string?> { ["id"] = id.ToString() };
        if (limit > 0) query["limit"] = limit.ToString();
        if (offset > 0) query["offset"] = offset.ToString();
        var root = await GetJsonAsync("playlist/track/all", query, cancellationToken, skipCache);
        return root["songs"]?.AsArray().Select(MapTrackFromNode).Where(t => t is not null).Cast<TrackInfo>().ToList() ?? [];
    }

    public async Task<PlaylistDetailDynamic?> GetPlaylistDetailDynamicAsync(long id, CancellationToken cancellationToken = default)
    {
        var root = await GetJsonAsync("playlist/detail/dynamic", new Dictionary<string, string?> { ["id"] = id.ToString() }, cancellationToken);
        return DeserializeFromNode<PlaylistDetailDynamic>(root);
    }

    public async Task<ApiResponse<PlaylistDetail>> CreatePlaylistAsync(string name, string? privacy = null, string? type = null, CancellationToken cancellationToken = default)
    {
        var query = new Dictionary<string, string?> { ["name"] = name };
        if (privacy is not null) query["privacy"] = privacy;
        if (type is not null) query["type"] = type;
        var root = await GetJsonAsync("playlist/create", query, cancellationToken);
        var playlist = MapPlaylistDetail(root["playlist"]);
        if (playlist is not null)
        {
            await InvalidatePlaylistCacheAsync(playlist.Id);
        }
        return new ApiResponse<PlaylistDetail>
        {
            Code = root["code"]?.GetValue<int>() ?? -1,
            Data = playlist,
        };
    }

    public async Task<ApiResponse<object>> DeletePlaylistAsync(string ids, CancellationToken cancellationToken = default)
    {
        var root = await GetJsonAsync("playlist/delete", new Dictionary<string, string?> { ["id"] = ids }, cancellationToken);
        foreach (var playlistId in ids.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (long.TryParse(playlistId, out var parsedId))
            {
                await InvalidatePlaylistCacheAsync(parsedId);
            }
        }
        return ParseApiResponse(root);
    }

    public async Task<ApiResponse<object>> UpdatePlaylistAsync(long id, string name, string desc, string tags, CancellationToken cancellationToken = default)
    {
        var root = await GetJsonAsync("playlist/update", new Dictionary<string, string?>
        {
            ["id"] = id.ToString(), ["name"] = name, ["desc"] = desc, ["tags"] = tags,
        }, cancellationToken);
        await InvalidatePlaylistCacheAsync(id);
        return ParseApiResponse(root);
    }

    public async Task<ApiResponse<object>> SubscribePlaylistAsync(long id, bool subscribe, CancellationToken cancellationToken = default)
    {
        var query = TimestampQuery();
        query["id"] = id.ToString();
        query["t"] = subscribe ? "1" : "2";
        var root = await GetJsonAsync("playlist/subscribe", query, cancellationToken);
        await InvalidatePlaylistCacheAsync(id);
        return ParseApiResponse(root);
    }

    public async Task<ApiResponse<object>> AddRemoveTracksAsync(string op, long pid, string tracks, CancellationToken cancellationToken = default)
    {
        var root = await GetJsonAsync("playlist/tracks", new Dictionary<string, string?>
        {
            ["op"] = op, ["pid"] = pid.ToString(), ["tracks"] = tracks,
        }, cancellationToken);
        await InvalidatePlaylistCacheAsync(pid);
        return ParseApiResponse(root);
    }

    public async Task<PlaylistCatlistResult> GetPlaylistCatlistAsync(CancellationToken cancellationToken = default)
    {
        var root = await GetJsonAsync("playlist/catlist", null, cancellationToken);
        return new PlaylistCatlistResult
        {
            All = root["all"]?["name"] != null ? new List<string>() : [],
            Sub = root["sub"]?.AsArray().Select(x => x!.GetValue<string>() ?? "").ToList() ?? [],
            Categories = root["categories"] != null
                ? root["categories"].Deserialize<Dictionary<string, string>>(_jsonOptions) ?? new()
                : new(),
        };
    }

    public async Task<IReadOnlyList<PlaylistCatInfo>> GetHotPlaylistCatsAsync(CancellationToken cancellationToken = default)
    {
        var root = await GetJsonAsync("playlist/hot", null, cancellationToken);
        return root["tags"]?.AsArray()
            .Select(item => new PlaylistCatInfo
            {
                Name = item?["name"]?.GetValue<string>() ?? "",
                Category = item?["category"]?.GetValue<string>() ?? "",
                Hot = item?["hot"]?.GetValue<bool>() ?? false,
            })
            .ToList() ?? [];
    }

    public async Task<IReadOnlyList<PlaylistSummary>> GetTopPlaylistsAsync(string? order = null, string? cat = null, int limit = 50, int offset = 0, CancellationToken cancellationToken = default)
    {
        var query = new Dictionary<string, string?>
        {
            ["limit"] = limit.ToString(), ["offset"] = offset.ToString(),
        };
        if (order is not null) query["order"] = order;
        if (cat is not null) query["cat"] = cat;
        var root = await GetJsonAsync("top/playlist", query, cancellationToken);
        return root["playlists"]?.AsArray()
            .Select(MapPlaylist).Where(x => x is not null).Cast<PlaylistSummary>().ToList() ?? [];
    }

    public async Task<IReadOnlyList<PlaylistSummary>> GetHighqualityPlaylistsAsync(string? cat = null, int limit = 50, long? before = null, CancellationToken cancellationToken = default)
    {
        var query = new Dictionary<string, string?> { ["limit"] = limit.ToString() };
        if (cat is not null) query["cat"] = cat;
        if (before.HasValue) query["before"] = before.Value.ToString();
        var root = await GetJsonAsync("top/playlist/highquality", query, cancellationToken);
        return root["playlists"]?.AsArray()
            .Select(MapPlaylist).Where(x => x is not null).Cast<PlaylistSummary>().ToList() ?? [];
    }

    public async Task<IReadOnlyList<PlaylistHighqualityTag>> GetHighqualityTagsAsync(CancellationToken cancellationToken = default)
    {
        var root = await GetJsonAsync("playlist/highquality/tags", null, cancellationToken);
        return root["tags"]?.AsArray()
            .Select(item => new PlaylistHighqualityTag
            {
                Id = item?["id"]?.GetValue<long>() ?? 0,
                Name = item?["name"]?.GetValue<string>() ?? "",
                Type = item?["type"]?.GetValue<int>() ?? 0,
                Category = item?["category"]?.GetValue<int>() ?? 0,
                Hot = item?["hot"]?.GetValue<bool>() ?? false,
            })
            .ToList() ?? [];
    }

    public async Task<IReadOnlyList<PlaylistSubscriber>> GetPlaylistSubscribersAsync(long id, int limit = 20, int offset = 0, CancellationToken cancellationToken = default)
    {
        var root = await GetJsonAsync("playlist/subscribers", new Dictionary<string, string?>
        {
            ["id"] = id.ToString(), ["limit"] = limit.ToString(), ["offset"] = offset.ToString(),
        }, cancellationToken);
        return root["subscribers"]?.AsArray()
            .Select(item => new PlaylistSubscriber
            {
                UserId = item?["userId"]?.GetValue<long>() ?? 0,
                Nickname = item?["nickname"]?.GetValue<string>() ?? "",
                AvatarUrl = item?["avatarUrl"]?.GetValue<string>() ?? "",
                Signature = item?["signature"]?.GetValue<string>() ?? "",
                Time = item?["time"]?.GetValue<long>() ?? 0,
            })
            .ToList() ?? [];
    }

    public async Task<ApiResponse<object>> UpdatePlaylistCoverAsync(long id, Stream imageStream, string fileName, int? imgSize = null, CancellationToken cancellationToken = default)
    {
        using var content = new MultipartFormDataContent();
        var streamContent = new StreamContent(imageStream);
        streamContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg");
        content.Add(streamContent, "imgFile", fileName);

        var query = new Dictionary<string, string?> { ["id"] = id.ToString() };
        if (imgSize.HasValue) query["imgSize"] = imgSize.Value.ToString();

        var root = await PostMultipartAsync("playlist/cover/update", query, content, cancellationToken);
        await InvalidatePlaylistCacheAsync(id);
        return ParseApiResponse(root);
    }

    public async Task<ApiResponse<object>> UpdatePlaylistDescAsync(long id, string desc, CancellationToken cancellationToken = default)
    {
        var root = await GetJsonAsync("playlist/desc/update", new Dictionary<string, string?>
        {
            ["id"] = id.ToString(), ["desc"] = desc,
        }, cancellationToken);
        await InvalidatePlaylistCacheAsync(id);
        return ParseApiResponse(root);
    }

    public async Task<ApiResponse<object>> UpdatePlaylistNameAsync(long id, string name, CancellationToken cancellationToken = default)
    {
        var root = await GetJsonAsync("playlist/name/update", new Dictionary<string, string?>
        {
            ["id"] = id.ToString(), ["name"] = name,
        }, cancellationToken);
        await InvalidatePlaylistCacheAsync(id);
        return ParseApiResponse(root);
    }

    public async Task<ApiResponse<object>> UpdatePlaylistTagsAsync(long id, string tags, CancellationToken cancellationToken = default)
    {
        var root = await GetJsonAsync("playlist/tags/update", new Dictionary<string, string?>
        {
            ["id"] = id.ToString(), ["tags"] = tags,
        }, cancellationToken);
        await InvalidatePlaylistCacheAsync(id);
        return ParseApiResponse(root);
    }

    public async Task<ApiResponse<object>> UpdatePlaylistOrderAsync(List<long> ids, CancellationToken cancellationToken = default)
    {
        var idsJson = JsonSerializer.Serialize(ids, _jsonOptions);
        var root = await GetJsonAsync("playlist/order/update", new Dictionary<string, string?> { ["ids"] = idsJson }, cancellationToken);
        return ParseApiResponse(root);
    }

    public async Task<ApiResponse<object>> UpdatePlaylistPlayCountAsync(long id, CancellationToken cancellationToken = default)
    {
        var root = await GetJsonAsync("playlist/update/playcount", new Dictionary<string, string?> { ["id"] = id.ToString() }, cancellationToken);
        await InvalidatePlaylistCacheAsync(id);
        return ParseApiResponse(root);
    }

    public async Task<ApiResponse<object>> AddVideoToPlaylistAsync(long pid, string ids, CancellationToken cancellationToken = default)
    {
        var root = await GetJsonAsync("playlist/track/add", new Dictionary<string, string?>
        {
            ["pid"] = pid.ToString(), ["ids"] = ids,
        }, cancellationToken);
        return ParseApiResponse(root);
    }

    public async Task<ApiResponse<object>> DeleteVideoFromPlaylistAsync(long pid, string ids, CancellationToken cancellationToken = default)
    {
        var root = await GetJsonAsync("playlist/track/delete", new Dictionary<string, string?>
        {
            ["pid"] = pid.ToString(), ["ids"] = ids,
        }, cancellationToken);
        return ParseApiResponse(root);
    }

    public async Task<IReadOnlyList<MvInfo>> GetPlaylistVideoRecentAsync(CancellationToken cancellationToken = default)
    {
        var root = await GetJsonAsync("playlist/video/recent", null, cancellationToken);
        return root["data"]?["list"]?.AsArray().Select(MapMvFromNode).Where(x => x is not null).Cast<MvInfo>().ToList() ?? [];
    }

    // ═══════════════════════════════════════════════════════════
    //  Song / Track
    // ═══════════════════════════════════════════════════════════

    public async Task<IReadOnlyList<TrackUrlInfo>> GetSongUrlsAsync(string ids, long br = 999000, CancellationToken cancellationToken = default)
    {
        var root = await GetJsonAsync("song/url", new Dictionary<string, string?>
        {
            ["id"] = ids, ["br"] = br.ToString(),
        }, cancellationToken);
        return root["data"]?.AsArray().Select(MapTrackUrl).Where(x => x is not null).Cast<TrackUrlInfo>().ToList() ?? [];
    }

    public async Task<IReadOnlyList<TrackUrlInfo>> GetSongUrlsV1Async(string ids, string level = "exhigh", CancellationToken cancellationToken = default)
    {
        var root = await GetJsonAsync("song/url/v1", new Dictionary<string, string?>
        {
            ["id"] = ids, ["level"] = level,
        }, cancellationToken);
        return root["data"]?.AsArray().Select(MapTrackUrl).Where(x => x is not null).Cast<TrackUrlInfo>().ToList() ?? [];
    }

    public async Task<string?> GetSongUrlV1RedirectAsync(long id, string level = "exhigh", bool unblock = true, CancellationToken cancellationToken = default)
    {
        var query = new Dictionary<string, string?>
        {
            ["id"] = id.ToString(),
            ["level"] = level,
            ["unblock"] = unblock ? "true" : "false",
        };

        const int maxAttempts = 3;
        Exception? lastError = null;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                var requestUri = BuildUri("song/url/v1/302", query);
                using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
                using var response = await _noRedirectHttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

                if (response.StatusCode is HttpStatusCode.Moved or HttpStatusCode.Redirect or HttpStatusCode.RedirectMethod or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect)
                {
                    var location = response.Headers.Location;
                    if (location is null)
                    {
                        return null;
                    }

                    var resolved = location.IsAbsoluteUri ? location : new Uri(requestUri, location);
                    return resolved.ToString();
                }

                if (IsTransientStatusCode(response.StatusCode) && attempt < maxAttempts)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(250 * attempt), cancellationToken);
                    continue;
                }

                response.EnsureSuccessStatusCode();

                var finalUri = response.RequestMessage?.RequestUri;
                if (finalUri is not null && finalUri != requestUri)
                {
                    return finalUri.ToString();
                }

                return null;
            }
            catch (Exception ex) when (attempt < maxAttempts && IsTransientException(ex))
            {
                lastError = ex;
                if (IsConnectionRefused(ex))
                {
                    await TryRestartBackendAsync();
                }

                await Task.Delay(TimeSpan.FromMilliseconds(250 * attempt), cancellationToken);
            }
            catch (Exception ex)
            {
                lastError = ex;
                if (IsConnectionRefused(ex))
                {
                    await TryRestartBackendAsync();
                }

                break;
            }
        }

        if (lastError is not null)
        {
            throw lastError;
        }

        return null;
    }

    public async Task<SongMusicDetailResult?> GetSongMusicDetailAsync(long id, CancellationToken cancellationToken = default)
    {
        var root = await GetJsonAsync("song/music/detail", new Dictionary<string, string?>
        {
            ["id"] = id.ToString(),
        }, cancellationToken);
        return MapSongMusicDetail(root["data"]);
    }

    public async Task<TrackDetailResult> GetTrackDetailAsync(string ids, CancellationToken cancellationToken = default)
    {
        var root = await GetJsonAsync("song/detail", new Dictionary<string, string?> { ["ids"] = ids }, cancellationToken);
        return new TrackDetailResult
        {
            Songs = root["songs"]?.AsArray().Select(MapTrackFromNode).Where(x => x is not null).Cast<TrackInfo>().ToList() ?? [],
            Total = root["total"]?.GetValue<int>() ?? 0,
        };
    }

    public async Task<LyricResult> GetLyricAsync(long id, CancellationToken cancellationToken = default)
    {
        var root = await GetJsonAsync("lyric", new Dictionary<string, string?> { ["id"] = id.ToString() }, cancellationToken);
        return new LyricResult
        {
            Lyric = root["lrc"]?["lyric"]?.GetValue<string>(),
            TranslatedLyric = root["tlyric"]?["lyric"]?.GetValue<string>(),
            RomanLyric = root["romalrc"]?["lyric"]?.GetValue<string>(),
            YrcLyric = root["yrc"]?["lyric"]?.GetValue<string>(),
            Version = root["sgc"]?.GetValue<int>() == 1 ? 0 : 1,
            IsPureMusic = (root["lrc"]?["lyric"]?.GetValue<string>() ?? "").Contains("[0:0.000]纯音乐") || root["nolyric"]?.GetValue<bool>() == true,
        };
    }

    public async Task<NewLyricResult> GetNewLyricAsync(long id, CancellationToken cancellationToken = default)
    {
        var root = await GetJsonAsync("lyric/new", new Dictionary<string, string?> { ["id"] = id.ToString() }, cancellationToken);
        return new NewLyricResult
        {
            Lrc = root["lrc"]?["lyric"]?.GetValue<string>(),
            Tlyric = root["tlyric"]?["lyric"]?.GetValue<string>(),
            Romalrc = root["romalrc"]?["lyric"]?.GetValue<string>(),
            Yrc = root["yrc"]?["lyric"]?.GetValue<string>(),
            Version = root["lrc"]?["version"]?.GetValue<int>() ?? 0,
        };
    }

    public async Task<ApiResponse<string>> CheckMusicAsync(long id, long br = 999000, CancellationToken cancellationToken = default)
    {
        var root = await GetJsonAsync("check/music", new Dictionary<string, string?>
        {
            ["id"] = id.ToString(), ["br"] = br.ToString(),
        }, cancellationToken);
        return new ApiResponse<string>
        {
            Code = root["code"]?.GetValue<int>() ?? -1,
            Message = root["message"]?.GetValue<string>(),
            Data = root["success"]?.GetValue<bool>() == true ? "ok" : "unavailable",
        };
    }

    public async Task<ApiResponse<object>> UpdateSongOrderAsync(long pid, List<long> ids, CancellationToken cancellationToken = default)
    {
        var idsJson = JsonSerializer.Serialize(ids, _jsonOptions);
        var root = await GetJsonAsync("song/order/update", new Dictionary<string, string?>
        {
            ["pid"] = pid.ToString(), ["ids"] = idsJson,
        }, cancellationToken);
        await InvalidatePlaylistCacheAsync(pid);
        return ParseApiResponse(root);
    }

    public async Task<SimiTrackResult> GetSimiTracksAsync(long id, CancellationToken cancellationToken = default)
    {
        var root = await GetJsonAsync("simi/song", new Dictionary<string, string?> { ["id"] = id.ToString() }, cancellationToken);
        return new SimiTrackResult
        {
            Songs = root["songs"]?.AsArray().Select(MapTrackFromNode).Where(x => x is not null).Cast<TrackInfo>().ToList() ?? [],
        };
    }

    public async Task<IReadOnlyList<TrackUrlInfo>> MatchSongUrlAsync(long id, string? source = null, CancellationToken cancellationToken = default)
    {
        var query = new Dictionary<string, string?> { ["id"] = id.ToString() };
        if (source is not null) query["source"] = source;
        var root = await GetJsonAsync("song/url/match", query, cancellationToken);
        return root["data"]?.AsArray().Select(MapTrackUrl).Where(x => x is not null).Cast<TrackUrlInfo>().ToList() ?? [];
    }

    // ═══════════════════════════════════════════════════════════
    //  Album
    // ═══════════════════════════════════════════════════════════

    public async Task<AlbumDetail?> GetAlbumDetailAsync(long id, CancellationToken cancellationToken = default)
    {
        var root = await GetJsonAsync("album", new Dictionary<string, string?> { ["id"] = id.ToString() }, cancellationToken);
        var album = MapAlbumDetail(root["album"]);
        if (album is not null && root["songs"] is JsonArray songs)
        {
            album.Tracks = songs.Select(MapTrackFromNode).Where(x => x is not null).Cast<TrackInfo>().ToList();
        }

        return album;
    }

    public async Task<AlbumDynamic?> GetAlbumDynamicAsync(long id, CancellationToken cancellationToken = default)
    {
        var root = await GetJsonAsync("album/detail/dynamic", new Dictionary<string, string?> { ["id"] = id.ToString() }, cancellationToken);
        return DeserializeFromNode<AlbumDynamic>(root);
    }

    public async Task<ApiResponse<object>> SubscribeAlbumAsync(long id, bool subscribe, CancellationToken cancellationToken = default)
    {
        var query = new Dictionary<string, string?> { ["id"] = id.ToString(), ["t"] = subscribe ? "1" : "0" };
        var root = await GetJsonAsync("album/sub", query, cancellationToken);
        await InvalidateAlbumCacheAsync(id);
        return ParseApiResponse(root);
    }

    public async Task<IReadOnlyList<AlbumDetail>> GetSublistAlbumsAsync(int limit = 25, int offset = 0, CancellationToken cancellationToken = default)
    {
        var root = await GetJsonAsync("album/sublist", new Dictionary<string, string?>
        {
            ["limit"] = limit.ToString(), ["offset"] = offset.ToString(),
        }, cancellationToken);
        return root["data"]?.AsArray().Select(MapAlbumDetail).Where(x => x is not null).Cast<AlbumDetail>().ToList() ?? [];
    }

    public async Task<NewestAlbumResult> GetNewestAlbumsAsync(CancellationToken cancellationToken = default)
    {
        var root = await GetJsonAsync("album/newest", null, cancellationToken);
        return new NewestAlbumResult
        {
            Albums = root["albums"]?.AsArray().Select(MapAlbumDetail).Where(x => x is not null).Cast<AlbumDetail>().ToList() ?? [],
        };
    }

    public async Task<TopAlbumResult> GetTopAlbumsAsync(string type = "new", int limit = 30, int offset = 0, CancellationToken cancellationToken = default)
    {
        var root = await GetJsonAsync("top/album", new Dictionary<string, string?>
        {
            ["type"] = type, ["limit"] = limit.ToString(), ["offset"] = offset.ToString(),
        }, cancellationToken);
        return new TopAlbumResult
        {
            MonthData = root["monthData"]?.AsArray().Select(MapAlbumDetail).Where(x => x is not null).Cast<AlbumDetail>().ToList() ?? [],
            Total = root["total"]?.GetValue<int>() ?? 0,
        };
    }

    // ═══════════════════════════════════════════════════════════
    //  Artist
    // ═══════════════════════════════════════════════════════════

    public async Task<ArtistDetail?> GetArtistDetailAsync(long id, CancellationToken cancellationToken = default)
    {
        var root = await GetJsonAsync("artist/detail", new Dictionary<string, string?> { ["id"] = id.ToString() }, cancellationToken);
        var data = root["data"]?["artist"];
        if (data is null) return null;
        return new ArtistDetail
        {
            Id = data["id"]?.GetValue<long>() ?? 0,
            Name = data["name"]?.GetValue<string>() ?? "",
            CoverUrl = data["cover"]?.GetValue<string>() ?? data["picUrl"]?.GetValue<string>() ?? "",
            Alias = data["alias"]?.AsArray().Select(a => a!.GetValue<string>() ?? "").ToList() ?? [],
            AlbumSize = data["albumSize"]?.GetValue<long>() ?? 0,
            MusicSize = data["musicSize"]?.GetValue<long>() ?? 0,
            MvSize = data["mvSize"]?.GetValue<long>() ?? 0,
            BriefDesc = data["briefDesc"]?.GetValue<string>() ?? "",
            Followed = data["followed"]?.GetValue<bool>() ?? false,
            Identity = data["identity"]?.GetValue<int>() ?? 0,
            Identities = data["identities"]?.AsArray().Select(i => i!.GetValue<string>() ?? "").ToList() ?? [],
        };
    }

    public async Task<ArtistTopSongsResult> GetArtistTopSongsAsync(long id, CancellationToken cancellationToken = default)
    {
        var root = await GetJsonAsync("artist/top/song", new Dictionary<string, string?> { ["id"] = id.ToString() }, cancellationToken);
        return new ArtistTopSongsResult
        {
            Songs = root["songs"]?.AsArray().Select(MapTrackFromNode).Where(x => x is not null).Cast<TrackInfo>().ToList() ?? [],
        };
    }

    public async Task<ArtistSongsResult> GetArtistSongsAsync(long id, string? order = null, int limit = 50, int offset = 0, CancellationToken cancellationToken = default)
    {
        var query = new Dictionary<string, string?>
        {
            ["id"] = id.ToString(), ["limit"] = limit.ToString(), ["offset"] = offset.ToString(),
        };
        if (order is not null) query["order"] = order;
        var root = await GetJsonAsync("artist/songs", query, cancellationToken);
        return new ArtistSongsResult
        {
            Songs = root["songs"]?.AsArray().Select(MapTrackFromNode).Where(x => x is not null).Cast<TrackInfo>().ToList() ?? [],
            Total = root["total"]?.GetValue<int>() ?? 0,
            More = root["more"]?.GetValue<bool>() ?? false,
        };
    }

    public async Task<IReadOnlyList<ArtistSummary>> GetArtistListAsync(int type, int area, string? initial = null, int limit = 30, int offset = 0, CancellationToken cancellationToken = default)
    {
        var query = new Dictionary<string, string?>
        {
            ["type"] = type.ToString(), ["area"] = area.ToString(),
            ["limit"] = limit.ToString(), ["offset"] = offset.ToString(),
        };
        if (initial is not null) query["initial"] = initial;
        var root = await GetJsonAsync("artist/list", query, cancellationToken);
        return root["artists"]?.AsArray()
            .Select(item => new ArtistSummary
            {
                Id = item?["id"]?.GetValue<long>() ?? 0,
                Name = item?["name"]?.GetValue<string>() ?? "",
                CoverUrl = item?["picUrl"]?.GetValue<string>() ?? "",
            })
            .Where(item => item.Id != 0)
            .ToList() ?? [];
    }

    public async Task<ApiResponse<object>> SubscribeArtistAsync(long id, bool subscribe, CancellationToken cancellationToken = default)
    {
        var root = await GetJsonAsync("artist/sub", new Dictionary<string, string?>
        {
            ["id"] = id.ToString(), ["t"] = subscribe ? "1" : "0",
        }, cancellationToken);
        return ParseApiResponse(root);
    }

    public async Task<IReadOnlyList<ArtistSummary>> GetSublistArtistsAsync(int limit = 25, int offset = 0, CancellationToken cancellationToken = default)
    {
        var root = await GetJsonAsync("artist/sublist", new Dictionary<string, string?>
        {
            ["limit"] = limit.ToString(), ["offset"] = offset.ToString(),
        }, cancellationToken);
        return root["data"]?.AsArray()
            .Select(item => new ArtistSummary
            {
                Id = item?["id"]?.GetValue<long>() ?? 0,
                Name = item?["name"]?.GetValue<string>() ?? "",
                CoverUrl = item?["picUrl"]?.GetValue<string>() ?? "",
            })
            .Where(item => item.Id != 0)
            .ToList() ?? [];
    }

    public async Task<IReadOnlyList<AlbumDetail>> GetArtistAlbumsAsync(long id, int limit = 30, int offset = 0, CancellationToken cancellationToken = default)
    {
        var root = await GetJsonAsync("artist/album", new Dictionary<string, string?>
        {
            ["id"] = id.ToString(), ["limit"] = limit.ToString(), ["offset"] = offset.ToString(),
        }, cancellationToken);
        return root["hotAlbums"]?.AsArray().Select(MapAlbumDetail).Where(x => x is not null).Cast<AlbumDetail>().ToList() ?? [];
    }

    public async Task<IReadOnlyList<MvInfo>> GetArtistMvsAsync(long id, int limit = 30, int offset = 0, CancellationToken cancellationToken = default)
    {
        var root = await GetJsonAsync("artist/mv", new Dictionary<string, string?>
        {
            ["id"] = id.ToString(), ["limit"] = limit.ToString(), ["offset"] = offset.ToString(),
        }, cancellationToken);
        return root["mvs"]?.AsArray().Select(MapMvFromNode).Where(x => x is not null).Cast<MvInfo>().ToList() ?? [];
    }

    public async Task<string?> GetArtistDescAsync(long id, CancellationToken cancellationToken = default)
    {
        var root = await GetJsonAsync("artist/desc", new Dictionary<string, string?> { ["id"] = id.ToString() }, cancellationToken);
        return root["briefDesc"]?.GetValue<string>();
    }

    public async Task<SimiTrackResult> GetSimiArtistsAsync(long id, CancellationToken cancellationToken = default)
    {
        var root = await GetJsonAsync("simi/artist", new Dictionary<string, string?> { ["id"] = id.ToString() }, cancellationToken);
        return new SimiTrackResult
        {
            Songs = root["artists"]?.AsArray().Select(MapTrackFromNode).Where(x => x is not null).Cast<TrackInfo>().ToList() ?? [],
        };
    }

    // ═══════════════════════════════════════════════════════════
    //  MV
    // ═══════════════════════════════════════════════════════════

    public async Task<MvInfo?> GetMvDetailAsync(long mvid, CancellationToken cancellationToken = default)
    {
        var root = await GetJsonAsync("mv/detail", new Dictionary<string, string?> { ["mvid"] = mvid.ToString() }, cancellationToken);
        return MapMvFromNode(root["data"]);
    }

    public async Task<MvUrlInfo?> GetMvUrlAsync(long id, int r = 1080, CancellationToken cancellationToken = default)
    {
        var root = await GetJsonAsync("mv/url", new Dictionary<string, string?>
        {
            ["id"] = id.ToString(), ["r"] = r.ToString(),
        }, cancellationToken);
        return MapMvUrl(root["data"]);
    }

    public async Task<ApiResponse<object>> SubscribeMvAsync(long mvid, bool subscribe, CancellationToken cancellationToken = default)
    {
        var root = await GetJsonAsync("mv/sub", new Dictionary<string, string?>
        {
            ["mvid"] = mvid.ToString(), ["t"] = subscribe ? "1" : "0",
        }, cancellationToken);
        return ParseApiResponse(root);
    }

    public async Task<IReadOnlyList<MvInfo>> GetSublistMvsAsync(int limit = 25, int offset = 0, CancellationToken cancellationToken = default)
    {
        var root = await GetJsonAsync("mv/sublist", new Dictionary<string, string?>
        {
            ["limit"] = limit.ToString(), ["offset"] = offset.ToString(),
        }, cancellationToken);
        return root["data"]?.AsArray().Select(MapMvFromNode).Where(x => x is not null).Cast<MvInfo>().ToList() ?? [];
    }

    public async Task<IReadOnlyList<MvInfo>> GetFirstMvsAsync(string? area = null, int limit = 30, CancellationToken cancellationToken = default)
    {
        var query = new Dictionary<string, string?> { ["limit"] = limit.ToString() };
        if (area is not null) query["area"] = area;
        var root = await GetJsonAsync("mv/first", query, cancellationToken);
        return root["data"]?.AsArray().Select(MapMvFromNode).Where(x => x is not null).Cast<MvInfo>().ToList() ?? [];
    }

    public async Task<IReadOnlyList<MvInfo>> GetAllMvsAsync(string? area = null, string? type = null, string? order = null, int limit = 30, int offset = 0, CancellationToken cancellationToken = default)
    {
        var query = new Dictionary<string, string?>
        {
            ["limit"] = limit.ToString(), ["offset"] = offset.ToString(),
        };
        if (area is not null) query["area"] = area;
        if (type is not null) query["type"] = type;
        if (order is not null) query["order"] = order;
        var root = await GetJsonAsync("mv/all", query, cancellationToken);
        return root["data"]?.AsArray().Select(MapMvFromNode).Where(x => x is not null).Cast<MvInfo>().ToList() ?? [];
    }

    public async Task<IReadOnlyList<MvInfo>> GetExclusiveMvsAsync(int limit = 30, int offset = 0, CancellationToken cancellationToken = default)
    {
        var root = await GetJsonAsync("mv/exclusive/rcmd", new Dictionary<string, string?>
        {
            ["limit"] = limit.ToString(), ["offset"] = offset.ToString(),
        }, cancellationToken);
        return root["data"]?.AsArray().Select(MapMvFromNode).Where(x => x is not null).Cast<MvInfo>().ToList() ?? [];
    }

    public async Task<IReadOnlyList<RelatedVideoInfo>> GetRelatedVideosAsync(long id, CancellationToken cancellationToken = default)
    {
        var root = await GetJsonAsync("related/allvideo", new Dictionary<string, string?> { ["id"] = id.ToString() }, cancellationToken);
        return root["data"]?.AsArray()
            .Select(item => new RelatedVideoInfo
            {
                Vid = item?["vid"]?.GetValue<long>() ?? 0,
                Type = item?["type"]?.GetValue<int>() ?? 0,
                Title = item?["title"]?.GetValue<string>() ?? "",
                CoverUrl = item?["coverUrl"]?.GetValue<string>() ?? "",
                DurationMs = item?["durationms"]?.GetValue<long>() ?? 0,
                PlayTime = item?["playTime"]?.GetValue<long>() ?? 0,
                Creator = item?["creator"]?.AsArray().Select(c => new ArtistSummary
                {
                    Id = c?["userId"]?.GetValue<long>() ?? 0,
                    Name = c?["userName"]?.GetValue<string>() ?? "",
                }).ToList() ?? [],
            })
            .ToList() ?? [];
    }

    public async Task<SimiMvResult> GetSimiMvsAsync(long mvid, CancellationToken cancellationToken = default)
    {
        var root = await GetJsonAsync("simi/mv", new Dictionary<string, string?> { ["mvid"] = mvid.ToString() }, cancellationToken);
        return new SimiMvResult
        {
            Mvs = root["mvs"]?.AsArray().Select(MapMvFromNode).Where(x => x is not null).Cast<MvInfo>().ToList() ?? [],
        };
    }

    // ═══════════════════════════════════════════════════════════
    //  Search
    // ═══════════════════════════════════════════════════════════

    public async Task<SearchResult> SearchAsync(string keywords, int type = 1, int limit = 30, int offset = 0, CancellationToken cancellationToken = default)
    {
        var root = await GetJsonAsync("search", new Dictionary<string, string?>
        {
            ["keywords"] = keywords, ["type"] = type.ToString(),
            ["limit"] = limit.ToString(), ["offset"] = offset.ToString(),
        }, cancellationToken);
        return ParseSearchResult(root["result"]);
    }

    public async Task<SearchResult> CloudSearchAsync(string keywords, int type = 1, int limit = 30, int offset = 0, CancellationToken cancellationToken = default)
    {
        var root = await GetJsonAsync("cloudsearch", new Dictionary<string, string?>
        {
            ["keywords"] = keywords, ["type"] = type.ToString(),
            ["limit"] = limit.ToString(), ["offset"] = offset.ToString(),
        }, cancellationToken);
        return ParseSearchResult(root["result"]);
    }

    public async Task<SearchDefaultResult?> GetSearchDefaultAsync(CancellationToken cancellationToken = default)
    {
        var root = await GetJsonAsync("search/default", null, cancellationToken);
        return DeserializeFromNode<SearchDefaultResult>(root["data"]);
    }

    public async Task<IReadOnlyList<SearchHotItem>> GetSearchHotAsync(CancellationToken cancellationToken = default)
    {
        var root = await GetJsonAsync("search/hot", null, cancellationToken);
        return root["data"]?.AsArray()
            .Select(item => new SearchHotItem
            {
                SearchWord = item?["searchWord"]?.GetValue<string>() ?? "",
                Score = item?["score"]?.GetValue<int>() ?? 0,
                Content = item?["content"]?.GetValue<string>() ?? "",
                IconType = item?["iconType"]?.GetValue<int>() ?? 0,
            })
            .ToList() ?? [];
    }

    public async Task<IReadOnlyList<SearchHotItem>> GetSearchHotDetailAsync(CancellationToken cancellationToken = default)
    {
        var root = await GetJsonAsync("search/hot/detail", null, cancellationToken);
        return root["data"]?.AsArray()
            .Select(item => new SearchHotItem
            {
                SearchWord = item?["searchWord"]?.GetValue<string>() ?? "",
                Score = item?["score"]?.GetValue<int>() ?? 0,
                Content = item?["content"]?.GetValue<string>() ?? "",
                IconType = item?["iconType"]?.GetValue<int>() ?? 0,
            })
            .ToList() ?? [];
    }

    public async Task<SearchSuggestResult?> GetSearchSuggestAsync(string keywords, string? type = null, CancellationToken cancellationToken = default)
    {
        var query = new Dictionary<string, string?> { ["keywords"] = keywords };
        if (type is not null) query["type"] = type;
        var root = await GetJsonAsync("search/suggest", query, cancellationToken);
        return ParseSearchSuggestResult(root["result"]);
    }

    public async Task<SearchResult> SearchMultimatchAsync(string keywords, CancellationToken cancellationToken = default)
    {
        var root = await GetJsonAsync("search/multimatch", new Dictionary<string, string?> { ["keywords"] = keywords }, cancellationToken);
        return ParseSearchResult(root["result"]);
    }

    // ═══════════════════════════════════════════════════════════
    //  Comment
    // ═══════════════════════════════════════════════════════════

    public async Task<CommentResult> GetCommentMusicAsync(long id, int limit = 20, int offset = 0, long? before = null, CancellationToken cancellationToken = default)
        => await GetCommentAsync("comment/music", id, limit, offset, before, cancellationToken);

    public async Task<CommentResult> GetCommentAlbumAsync(long id, int limit = 20, int offset = 0, long? before = null, CancellationToken cancellationToken = default)
        => await GetCommentAsync("comment/album", id, limit, offset, before, cancellationToken);

    public async Task<CommentResult> GetCommentPlaylistAsync(long id, int limit = 20, int offset = 0, long? before = null, CancellationToken cancellationToken = default)
        => await GetCommentAsync("comment/playlist", id, limit, offset, before, cancellationToken);

    public async Task<CommentResult> GetCommentMvAsync(long id, int limit = 20, int offset = 0, long? before = null, CancellationToken cancellationToken = default)
        => await GetCommentAsync("comment/mv", id, limit, offset, before, cancellationToken);

    public async Task<CommentResult> GetCommentDjAsync(long id, int limit = 20, int offset = 0, long? before = null, CancellationToken cancellationToken = default)
        => await GetCommentAsync("comment/dj", id, limit, offset, before, cancellationToken);

    public async Task<CommentResult> GetCommentEventAsync(string threadId, int limit = 20, int offset = 0, CancellationToken cancellationToken = default)
    {
        var root = await GetJsonAsync("comment/event", new Dictionary<string, string?>
        {
            ["threadId"] = threadId, ["limit"] = limit.ToString(), ["offset"] = offset.ToString(),
        }, cancellationToken);
        return ParseCommentResult(root);
    }

    public async Task<CommentResult> GetCommentFloorAsync(long parentCommentId, long id, int type, int limit = 20, long? time = null, CancellationToken cancellationToken = default)
    {
        var query = new Dictionary<string, string?>
        {
            ["parentCommentId"] = parentCommentId.ToString(), ["id"] = id.ToString(),
            ["type"] = type.ToString(), ["limit"] = limit.ToString(),
        };
        if (time.HasValue) query["time"] = time.Value.ToString();
        var root = await GetJsonAsync("comment/floor", query, cancellationToken);
        return ParseCommentResult(root["data"]);
    }

    public async Task<CommentResult> GetCommentHotAsync(long id, int type, int limit = 20, int offset = 0, CancellationToken cancellationToken = default)
    {
        var root = await GetJsonAsync("comment/hot", new Dictionary<string, string?>
        {
            ["id"] = id.ToString(), ["type"] = type.ToString(),
            ["limit"] = limit.ToString(), ["offset"] = offset.ToString(),
        }, cancellationToken);
        return ParseCommentResult(root);
    }

    public async Task<CommentLikeResult> LikeCommentAsync(long id, long cid, int type, bool like, CancellationToken cancellationToken = default)
    {
        var query = new Dictionary<string, string?>
        {
            ["id"] = id.ToString(), ["cid"] = cid.ToString(), ["type"] = type.ToString(),
            ["t"] = like ? "1" : "0",
        };
        var root = await GetJsonAsync("comment/like", query, cancellationToken);
        return new CommentLikeResult
        {
            Code = root["code"]?.GetValue<int>() ?? -1,
            Message = root["message"]?.GetValue<string>(),
        };
    }

    public async Task<CommentResult> SendCommentAsync(long id, int type, string content, long? commentId = null, CancellationToken cancellationToken = default)
    {
        var query = new Dictionary<string, string?>
        {
            ["id"] = id.ToString(), ["type"] = type.ToString(), ["content"] = content,
        };
        if (commentId.HasValue) query["commentId"] = commentId.Value.ToString();
        var root = await GetJsonAsync("comment", query, cancellationToken);
        return ParseCommentResult(root);
    }

    private async Task<CommentResult> GetCommentAsync(string path, long id, int limit, int offset, long? before, CancellationToken cancellationToken)
    {
        var query = new Dictionary<string, string?>
        {
            ["id"] = id.ToString(), ["limit"] = limit.ToString(), ["offset"] = offset.ToString(),
        };
        if (before.HasValue) query["before"] = before.Value.ToString();
        var root = await GetJsonAsync(path, query, cancellationToken);
        return ParseCommentResult(root);
    }

    // ═══════════════════════════════════════════════════════════
    //  Daily Recommendations / FM
    // ═══════════════════════════════════════════════════════════

    public async Task<DailyRecommendSongsResult> GetDailyRecommendSongsAsync(CancellationToken cancellationToken = default)
    {
        var root = await GetJsonAsync("recommend/songs", TimestampQuery(), cancellationToken);
        return new DailyRecommendSongsResult
        {
            DailySongs = root["data"]?["dailySongs"]?.AsArray().Select(MapTrackFromNode).Where(x => x is not null).Cast<TrackInfo>().ToList() ?? [],
            RecommendReasons = root["data"]?["recommendReasons"]?.AsArray()
                .Select(item => new PlaylistSummary
                {
                    Id = item?["songId"]?.GetValue<long>() ?? 0,
                    Name = item?["reason"]?.GetValue<string>() ?? "",
                }).ToList() ?? [],
        };
    }

    public async Task<DailyRecommendPlaylistsResult> GetDailyRecommendPlaylistsAsync(CancellationToken cancellationToken = default)
    {
        var root = await GetJsonAsync("recommend/resource", TimestampQuery(), cancellationToken);
        return new DailyRecommendPlaylistsResult
        {
            Recommend = root["recommend"]?.AsArray().Select(MapPlaylist).Where(x => x is not null).Cast<PlaylistSummary>().ToList() ?? [],
        };
    }

    public async Task<IReadOnlyList<PersonalFmTrack>> GetPersonalFmAsync(CancellationToken cancellationToken = default)
    {
        var root = await GetJsonAsync("personal_fm", TimestampQuery(), cancellationToken);
        return root["data"]?.AsArray()
            .Select(item =>
            {
                var track = MapTrackFromNode(item);
                if (track is null) return null;
                var fm = new PersonalFmTrack
                {
                    Id = track.Id, Name = track.Name,
                    Artists = track.Artists, Album = track.Album,
                    Duration = track.Duration, IsLiked = track.IsLiked,
                    Fee = track.Fee, Mp3Url = track.Mp3Url, Br = track.Br,
                    Reason = item?["reason"]?.GetValue<string>(),
                };
                return fm;
            })
            .Where(x => x is not null).Cast<PersonalFmTrack>().ToList() ?? [];
    }

    public async Task<ApiResponse<object>> ScrobbleV1Async(
        long id,
        long time,
        long? sourceId = null,
        string? source = null,
        string? name = null,
        string? artist = null,
        long? bitrate = null,
        string? level = null,
        long? total = null,
        CancellationToken cancellationToken = default)
    {
        var query = new Dictionary<string, string?>
        {
            ["id"] = id.ToString(),
            ["time"] = time.ToString(),
        };

        if (sourceId.HasValue) query["sourceid"] = sourceId.Value.ToString();
        if (!string.IsNullOrWhiteSpace(source)) query["source"] = source;
        if (!string.IsNullOrWhiteSpace(name)) query["name"] = name;
        if (!string.IsNullOrWhiteSpace(artist)) query["artist"] = artist;
        if (bitrate.HasValue) query["bitrate"] = bitrate.Value.ToString();
        if (!string.IsNullOrWhiteSpace(level)) query["level"] = level;
        if (total.HasValue) query["total"] = total.Value.ToString();

        var root = await GetJsonAsync("scrobble/v1", query, cancellationToken);
        return ParseApiResponse(root);
    }

    public async Task<ApiResponse<object>> SubmitPlayStateAsync(
        long id,
        string? sessionId = null,
        long? progress = null,
        string? playMode = null,
        string? type = null,
        CancellationToken cancellationToken = default)
    {
        var query = new Dictionary<string, string?>
        {
            ["id"] = id.ToString(),
        };

        if (!string.IsNullOrWhiteSpace(sessionId)) query["sessionId"] = sessionId;
        if (progress.HasValue) query["progress"] = progress.Value.ToString();
        if (!string.IsNullOrWhiteSpace(playMode)) query["playMode"] = playMode;
        if (!string.IsNullOrWhiteSpace(type)) query["type"] = type;

        var root = await GetJsonAsync("relay/play/state/submit", query, cancellationToken);
        return ParseApiResponse(root);
    }

    // ═══════════════════════════════════════════════════════════
    //  Liked / Library
    // ═══════════════════════════════════════════════════════════

    public async Task<IReadOnlyList<long>> GetLikedTrackIdsAsync(long uid, CancellationToken cancellationToken = default)
    {
        var root = await GetJsonAsync("likelist", new Dictionary<string, string?>
        {
            ["uid"] = uid.ToString(), ["timestamp"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(),
        }, cancellationToken);
        return root["ids"]?.AsArray().Select(id => id!.GetValue<long>()).ToList() ?? [];
    }

    public async Task<ApiResponse<object>> LikeTrackAsync(long id, bool like, CancellationToken cancellationToken = default)
    {
        var query = TimestampQuery();
        query["id"] = id.ToString();
        query["like"] = like ? "true" : "false";
        var root = await GetJsonAsync("like", query, cancellationToken);
        return ParseApiResponse(root);
    }

    public async Task<bool> CheckMusicLikedAsync(long id, CancellationToken cancellationToken = default)
    {
        var root = await GetJsonAsync("check/music/liked", new Dictionary<string, string?> { ["id"] = id.ToString() }, cancellationToken);
        return root["data"]?["liked"]?.GetValue<bool>() ?? false;
    }

    // ═══════════════════════════════════════════════════════════
    //  Top List
    // ═══════════════════════════════════════════════════════════

    public async Task<TopListDetailResult?> GetTopListDetailAsync(long id, CancellationToken cancellationToken = default)
    {
        var root = await GetJsonAsync("toplist/detail", new Dictionary<string, string?> { ["id"] = id.ToString() }, cancellationToken);
        return DeserializeFromNode<TopListDetailResult>(root);
    }

    // ═══════════════════════════════════════════════════════════
    //  Top Songs
    // ═══════════════════════════════════════════════════════════

    public async Task<IReadOnlyList<TrackInfo>> GetTopSongsAsync(int type = 0, CancellationToken cancellationToken = default)
    {
        var root = await GetJsonAsync("top/song", new Dictionary<string, string?> { ["type"] = type.ToString() }, cancellationToken);
        return root["data"]?.AsArray().Select(MapTrackFromNode).Where(x => x is not null).Cast<TrackInfo>().ToList() ?? [];
    }

    // ═══════════════════════════════════════════════════════════
    //  Event / Share
    // ═══════════════════════════════════════════════════════════

    public async Task<ApiResponse<object>> ForwardEventAsync(long uid, long evId, string forwards, CancellationToken cancellationToken = default)
    {
        var root = await GetJsonAsync("event/forward", new Dictionary<string, string?>
        {
            ["uid"] = uid.ToString(), ["evId"] = evId.ToString(), ["forwards"] = forwards,
        }, cancellationToken);
        return ParseApiResponse(root);
    }

    public async Task<ApiResponse<object>> DeleteEventAsync(long evId, CancellationToken cancellationToken = default)
    {
        var root = await GetJsonAsync("event/del", new Dictionary<string, string?> { ["evId"] = evId.ToString() }, cancellationToken);
        return ParseApiResponse(root);
    }

    public async Task<ApiResponse<object>> ShareResourceAsync(string id, string type, string? msg = null, CancellationToken cancellationToken = default)
    {
        var query = new Dictionary<string, string?> { ["id"] = id, ["type"] = type };
        if (msg is not null) query["msg"] = msg;
        var root = await GetJsonAsync("share/resource", query, cancellationToken);
        return ParseApiResponse(root);
    }

    // ═══════════════════════════════════════════════════════════
    //  Hot Topic
    // ═══════════════════════════════════════════════════════════

    public async Task<IReadOnlyList<object>> GetHotTopicsAsync(int limit = 20, int offset = 0, CancellationToken cancellationToken = default)
    {
        var root = await GetJsonAsync("hot/topic", new Dictionary<string, string?>
        {
            ["limit"] = limit.ToString(), ["offset"] = offset.ToString(),
        }, cancellationToken);
        return root["data"]?.AsArray().Select(x => (object)x!).ToList() ?? [];
    }

    public async Task<TopicDetailResult?> GetTopicDetailAsync(long actid, CancellationToken cancellationToken = default)
    {
        var root = await GetJsonAsync("topic/detail", new Dictionary<string, string?> { ["actid"] = actid.ToString() }, cancellationToken);
        return DeserializeFromNode<TopicDetailResult>(root);
    }

    public async Task<IReadOnlyList<object>> GetTopicDetailEventHotAsync(long actid, CancellationToken cancellationToken = default)
    {
        var root = await GetJsonAsync("topic/detail/event/hot", new Dictionary<string, string?> { ["actid"] = actid.ToString() }, cancellationToken);
        return root["data"]?.AsArray().Select(x => (object)x!).ToList() ?? [];
    }

    // ═══════════════════════════════════════════════════════════
    //  Intelligence
    // ═══════════════════════════════════════════════════════════

    public async Task<PlaymodeIntelligenceResult> GetPlaymodeIntelligenceAsync(long id, long pid, long? sid = null, CancellationToken cancellationToken = default)
    {
        var query = new Dictionary<string, string?>
        {
            ["id"] = id.ToString(), ["pid"] = pid.ToString(),
        };
        if (sid.HasValue) query["sid"] = sid.Value.ToString();
        var root = await GetJsonAsync("playmode/intelligence/list", query, cancellationToken);
        return new PlaymodeIntelligenceResult
        {
            Data = root["data"]?.AsArray().Select(MapTrackFromNode).Where(x => x is not null).Cast<TrackInfo>().ToList() ?? [],
            Code = root["code"]?.GetValue<int>() ?? -1,
        };
    }

    // ═══════════════════════════════════════════════════════════
    //  Last.fm
    // ═══════════════════════════════════════════════════════════

    public async Task<ApiResponse<object>> LastfmLoginAsync(string token, CancellationToken cancellationToken = default)
    {
        var root = await GetJsonAsync("third/lastfm/login", new Dictionary<string, string?> { ["token"] = token }, cancellationToken);
        return ParseApiResponse(root);
    }

    public async Task<ApiResponse<object>> LastfmCallbackAsync(string token, CancellationToken cancellationToken = default)
    {
        var root = await GetJsonAsync("third/lastfm/callback", new Dictionary<string, string?> { ["token"] = token }, cancellationToken);
        return ParseApiResponse(root);
    }

    public async Task<ApiResponse<object>> LastfmScrobbleAsync(string artist, string track, long timestamp, string? album = null, CancellationToken cancellationToken = default)
    {
        var query = new Dictionary<string, string?>
        {
            ["artist"] = artist, ["track"] = track, ["timestamp"] = timestamp.ToString(),
        };
        if (album is not null) query["album"] = album;
        var root = await GetJsonAsync("third/lastfm/scrobble", query, cancellationToken);
        return ParseApiResponse(root);
    }

    public async Task<ApiResponse<object>> LastfmNowPlayingAsync(string artist, string track, string? album = null, CancellationToken cancellationToken = default)
    {
        var query = new Dictionary<string, string?>
        {
            ["artist"] = artist, ["track"] = track,
        };
        if (album is not null) query["album"] = album;
        var root = await GetJsonAsync("third/lastfm/nowplaying", query, cancellationToken);
        return ParseApiResponse(root);
    }

    // ═══════════════════════════════════════════════════════════
    //  Countries
    // ═══════════════════════════════════════════════════════════

    public async Task<IReadOnlyList<object>> GetCountriesCodeListAsync(CancellationToken cancellationToken = default)
    {
        var root = await GetJsonAsync("countries/code/list", null, cancellationToken);
        return root["data"]?.AsArray().Select(x => (object)x!).ToList() ?? [];
    }

    // ═══════════════════════════════════════════════════════════
    //  HTTP Helpers
    // ═══════════════════════════════════════════════════════════

    private async Task<JsonNode> GetJsonAsync(string relativePath, IDictionary<string, string?>? query, CancellationToken cancellationToken, bool skipCache = false)
    {
        return await SendAsync(HttpMethod.Get, relativePath, query, null, skipCache, cancellationToken);
    }

    private async Task<JsonNode> PostJsonAsync(string relativePath, IDictionary<string, string?>? query, object body, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(body, _jsonOptions);
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        return await SendAsync(HttpMethod.Post, relativePath, query, content, cancellationToken: cancellationToken);
    }

    private async Task<JsonNode> PostFormAsync(string relativePath, IDictionary<string, string?>? formData, IDictionary<string, string?>? query, CancellationToken cancellationToken)
    {
        var content = new FormUrlEncodedContent(
            (formData ?? new Dictionary<string, string?>())
            .Where(p => p.Value is not null)
            .Select(p => new KeyValuePair<string, string>(p.Key, p.Value!)));

        return await SendAsync(HttpMethod.Post, relativePath, query, content, cancellationToken: cancellationToken);
    }

    private async Task<JsonNode> PostMultipartAsync(string relativePath, IDictionary<string, string?>? query, MultipartFormDataContent content, CancellationToken cancellationToken)
    {
        return await SendAsync(HttpMethod.Post, relativePath, query, content, cancellationToken: cancellationToken);
    }

    private async Task<JsonNode> SendAsync(HttpMethod method, string relativePath, IDictionary<string, string?>? query, HttpContent? content, bool skipCache = false, CancellationToken cancellationToken = default)
    {
        var canCache = method == HttpMethod.Get
            && _cacheService is not null
            && IsCacheableRequest(relativePath, query);

        if (canCache && !skipCache)
        {
            var cacheKey = BuildCacheKey(relativePath, query);
            var cachedJson = await _cacheService!.GetAsync<CachedApiResponse>(cacheKey);
            if (cachedJson is not null)
            {
                return JsonNode.Parse(cachedJson.Data) ?? throw new InvalidOperationException($"Empty cached JSON for '{relativePath}'.");
            }
        }

        const int maxAttempts = 3;
        Exception? lastError = null;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                var uri = BuildUri(relativePath, query);
                using var request = new HttpRequestMessage(method, uri);
                if (content is not null)
                {
                    request.Content = content;
                }

                using var response = await _httpClient.SendAsync(request, cancellationToken);

                if (IsTransientStatusCode(response.StatusCode) && attempt < maxAttempts)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(250 * attempt), cancellationToken);
                    continue;
                }

                response.EnsureSuccessStatusCode();

                var json = await response.Content.ReadAsStringAsync(cancellationToken);
                var result = JsonNode.Parse(json) ?? throw new InvalidOperationException($"Empty JSON payload for '{relativePath}'.");

                if (canCache)
                {
                    var cacheKey = BuildCacheKey(relativePath, query);
                    await _cacheService!.SetAsync(cacheKey, new CachedApiResponse { Data = json }, TimeSpan.FromMinutes(30));
                }

                return result;
            }
            catch (Exception ex) when (attempt < maxAttempts && IsTransientException(ex))
            {
                lastError = ex;
                if (IsConnectionRefused(ex))
                {
                    await TryRestartBackendAsync();
                }
                await Task.Delay(TimeSpan.FromMilliseconds(250 * attempt), cancellationToken);
            }
            catch (Exception ex)
            {
                lastError = ex;
                if (IsConnectionRefused(ex))
                {
                    await TryRestartBackendAsync();
                }
                break;
            }
        }

        throw lastError ?? new InvalidOperationException($"Request failed for '{relativePath}'.");
    }

    private static bool IsConnectionRefused(Exception ex)
    {
        if (ex is HttpRequestException { InnerException: SocketException { SocketErrorCode: SocketError.ConnectionRefused } })
            return true;
        if (ex is SocketException { SocketErrorCode: SocketError.ConnectionRefused })
            return true;
        return false;
    }

    private async Task TryRestartBackendAsync()
    {
        if (await _restartLock.WaitAsync(0))
        {
            try
            {
                // Throttle restarts to once every 30 seconds
                if (DateTimeOffset.UtcNow - _lastRestartTime < TimeSpan.FromSeconds(30))
                {
                    return;
                }

                _lastRestartTime = DateTimeOffset.UtcNow;
                
                // We use dynamic/reflection or a well-known static bridge to reach the UI project's BackendHostService
                // In this architecture, we can assume existence of YPM.UI.App.BackendHostService if it's running in that context
                var backendService = GetBackendService();
                if (backendService != null)
                {
                    await backendService.StartAsync();
                }
            }
            catch (Exception restartEx)
            {
                Debug.WriteLine($"Failed to auto-restart backend: {restartEx.Message}");
            }
            finally
            {
                _restartLock.Release();
            }
        }
    }

    private static IBackendHostService? GetBackendService()
    {
        try
        {
            // Use reflection to access App.BackendHostService from YPM.UI assembly
            // to avoid circular dependency if YPM.Api doesn't reference YPM.UI
            var appType = Type.GetType("YPM.UI.App, YPM.UI");
            if (appType != null)
            {
                var prop = appType.GetProperty("BackendHostService", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                return prop?.GetValue(null) as IBackendHostService;
            }
        }
        catch { }
        return null;
    }

    private static bool IsCacheableRequest(string relativePath, IDictionary<string, string?>? query)
    {
        var path = relativePath.TrimStart('/').ToLowerInvariant();
        return path is
            "banner"
            or "album/new"
            or "album"
            or "album/detail/dynamic"
            or "toplist"
            or "toplist/artist"
            or "playlist/detail"
            or "playlist/detail/dynamic"
            or "playlist/track/all"
            or "playlist/catlist"
            or "playlist/hot"
            or "top/playlist"
            or "top/playlist/highquality"
            or "playlist/highquality/tags"
            or "countries/code/list"
            or "search/default"
            or "search/hot"
            or "search/hot/detail"
            or "user/account"
            or "user/subcount"
            or "user/level"
            or "user/event"
            or "user/follows"
            or "user/followeds"
            or "user/dj"
            or "user/comment/history"
            or "recommend/songs"
            or "recommend/resource"
            or "personal_fm"
            or "playlist/subscribers";
    }

    private async Task InvalidatePlaylistCacheAsync(long id)
    {
        if (_cacheService is null)
        {
            return;
        }

        await _cacheService.RemoveAsync(BuildCacheKey("playlist/detail", new Dictionary<string, string?> { ["id"] = id.ToString() }));
        await _cacheService.RemoveAsync(BuildCacheKey("playlist/detail/dynamic", new Dictionary<string, string?> { ["id"] = id.ToString() }));
        await _cacheService.RemoveAsync(BuildCacheKey("playlist/track/all", new Dictionary<string, string?> { ["id"] = id.ToString() }));
    }

    private async Task InvalidateAlbumCacheAsync(long id)
    {
        if (_cacheService is null)
        {
            return;
        }

        await _cacheService.RemoveAsync(BuildCacheKey("album", new Dictionary<string, string?> { ["id"] = id.ToString() }));
        await _cacheService.RemoveAsync(BuildCacheKey("album/detail/dynamic", new Dictionary<string, string?> { ["id"] = id.ToString() }));
    }

    public async Task ClearApiCacheAsync()
    {
        if (_cacheService is not null)
        {
            await _cacheService.ClearAllAsync();
        }
    }

    private static string BuildCacheKey(string relativePath, IDictionary<string, string?>? query)
    {
        var normalizedPath = relativePath.TrimStart('/');

        var significantParams = (query ?? new Dictionary<string, string?>())
            .Where(static p => p.Value is not null && p.Key != "timestamp")
            .OrderBy(static p => p.Key)
            .Select(static p => $"{p.Key}={p.Value}")
            .ToList();

        if (significantParams.Count == 0)
        {
            return $"GET:{normalizedPath}";
        }

        return $"GET:{normalizedPath}?{string.Join("&", significantParams)}";
    }

    private Uri BuildUri(string relativePath, IDictionary<string, string?>? query)
    {
        var values = new List<string>();
        if (query is not null)
        {
            foreach (var (key, value) in query.Where(static pair => !string.IsNullOrWhiteSpace(pair.Value)))
            {
                values.Add($"{Uri.EscapeDataString(key)}={Uri.EscapeDataString(value!)}");
            }
        }

        if (_options.EnableRealIp && !relativePath.Contains("login", StringComparison.OrdinalIgnoreCase))
        {
            values.Add($"realIP={Uri.EscapeDataString(_options.RealIp)}");
        }

        if (!string.IsNullOrWhiteSpace(_options.Proxy))
        {
            values.Add($"proxy={Uri.EscapeDataString(_options.Proxy)}");
        }

        var path = relativePath.TrimStart('/');
        var builder = new UriBuilder(new Uri(_httpClient.BaseAddress!, path));
        if (values.Count > 0)
        {
            builder.Query = string.Join("&", values);
        }

        return builder.Uri;
    }

    // ═══════════════════════════════════════════════════════════
    //  JSON Mapping Helpers
    // ═══════════════════════════════════════════════════════════

    private static T? DeserializeFromNode<T>(JsonNode? node) where T : class
    {
        if (node is null) return null;
        return node.Deserialize<T>(new JsonSerializerOptions(JsonSerializerDefaults.Web));
    }

    private static SearchSuggestResult? ParseSearchSuggestResult(JsonNode? root)
    {
        if (root is null)
        {
            return null;
        }

        return new SearchSuggestResult
        {
            Order = root["order"]?.AsArray().Select(item => GetString(item) ?? string.Empty).ToList(),
            Albums = ParseSearchSuggestSection(root["albums"]),
            Artists = ParseSearchSuggestSection(root["artists"]),
            Songs = ParseSearchSuggestSection(root["songs"]),
            Playlists = ParseSearchSuggestSection(root["playlists"]),
        };
    }

    private static SearchSuggestSection? ParseSearchSuggestSection(JsonNode? node)
    {
        if (node is null)
        {
            return null;
        }

        var items = node["items"]?.AsArray() ?? node.AsArray();
        return new SearchSuggestSection
        {
            Items = items.Select(ParseSearchSuggestItem).Where(static item => item is not null).Cast<SearchSuggestItem>().ToList(),
        };
    }

    private static SearchSuggestItem? ParseSearchSuggestItem(JsonNode? node)
    {
        if (node is null)
        {
            return null;
        }

        return new SearchSuggestItem
        {
            Id = GetInt64(node["id"]),
            Name = GetString(node["name"]) ?? string.Empty,
            Artist = node["artist"] is { } artist ? new ArtistSummary
            {
                Id = GetInt64(artist["id"]),
                Name = GetString(artist["name"]) ?? string.Empty,
                CoverUrl = GetString(artist["picUrl"]) ?? GetString(artist["img1v1Url"]) ?? string.Empty,
            } : null,
            Artists = node["artists"]?.AsArray().Select(item => new ArtistSummary
            {
                Id = GetInt64(item?["id"]),
                Name = GetString(item?["name"]) ?? string.Empty,
                CoverUrl = GetString(item?["picUrl"]) ?? GetString(item?["img1v1Url"]) ?? string.Empty,
            }).ToList(),
            Album = node["album"] is { } album ? new AlbumSummary
            {
                Id = GetInt64(album["id"]),
                Name = GetString(album["name"]) ?? string.Empty,
                ArtistName = GetString(album["artist"]?["name"])
                    ?? album["artists"]?.AsArray().Select(a => GetString(a?["name"]) ?? string.Empty).FirstOrDefault(static n => !string.IsNullOrWhiteSpace(n))
                    ?? string.Empty,
                CoverUrl = GetString(album["picUrl"]) ?? GetString(album["blurPicUrl"]) ?? GetString(album["coverImgUrl"]) ?? string.Empty,
            } : null,
        };
    }

    private static Dictionary<string, string?> TimestampQuery() => new()
    {
        ["timestamp"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(),
    };

    private static ApiResponse<object> ParseApiResponse(JsonNode root)
    {
        return new ApiResponse<object>
        {
            Code = root["code"]?.GetValue<int>() ?? -1,
            Message = root["message"]?.GetValue<string>(),
        };
    }

    private static LoginResult ParseLoginResult(JsonNode root)
    {
        return new LoginResult
        {
            Code = root["code"]?.GetValue<int>() ?? -1,
            Cookie = root["cookie"]?.GetValue<string>(),
            Token = root["token"]?.GetValue<string>(),
            Message = root["message"]?.GetValue<string>(),
            Profile = DeserializeFromNode<UserProfile>(root["profile"]),
        };
    }

    private static PlaylistSummary? MapPlaylist(JsonNode? item)
    {
        var id = item?["id"]?.GetValue<long>() ?? 0;
        if (id == 0) return null;
        return new PlaylistSummary
        {
            Id = id,
            Name = item?["name"]?.GetValue<string>() ?? string.Empty,
            CoverUrl = item?["picUrl"]?.GetValue<string>()
                ?? item?["coverImgUrl"]?.GetValue<string>()
                ?? string.Empty,
            Description = item?["description"]?.GetValue<string>(),
            Copywriter = item?["copywriter"]?.GetValue<string>()
                ?? item?["updateFrequency"]?.GetValue<string>(),
        };
    }

    private static PlaylistDetail? MapPlaylistDetail(JsonNode? item)
    {
        if (item is null) return null;
        return new PlaylistDetail
        {
            Id = GetInt64(item["id"]),
            Name = GetString(item["name"]) ?? "",
            CoverUrl = GetString(item["coverImgUrl"]) ?? GetString(item["picUrl"]) ?? "",
            Description = GetString(item["description"]),
            Creator = item["creator"] is { } c ? new UserProfile
            {
                UserId = GetInt64(c["userId"]),
                Nickname = GetString(c["nickname"]) ?? "",
                AvatarUrl = GetString(c["avatarUrl"]) ?? "",
            } : null,
            CreateTime = GetInt64(item["createTime"]),
            UpdateTime = GetInt64(item["updateTime"]),
            TrackCount = GetInt64(item["trackCount"]),
            PlayCount = GetInt64(item["playCount"]),
            SubscribedCount = GetInt64(item["subscribedCount"]),
            ShareCount = GetInt64(item["shareCount"]),
            CommentCount = GetInt64(item["commentCount"]),
            Subscribed = GetBoolean(item["subscribed"]),
            Tags = item["tags"]?.AsArray().Select(t => GetString(t) ?? "").ToList() ?? [],
            Tracks = item["tracks"]?.AsArray().Select(MapTrackFromNode).Where(x => x is not null).Cast<TrackInfo>().ToList() ?? [],
            TrackIds = item["trackIds"]?.AsArray().Select(t => GetInt64(t?["id"])).ToList() ?? [],
        };
    }

    private static AlbumDetail? MapAlbumDetail(JsonNode? item)
    {
        if (item is null) return null;
        return new AlbumDetail
        {
            Id = GetInt64(item["id"]),
            Name = GetString(item["name"]) ?? "",
            CoverUrl = GetString(item["picUrl"]) ?? GetString(item["blurPicUrl"]) ?? GetString(item["coverImgUrl"]) ?? "",
            Artist = item["artist"] is { } a ? new ArtistSummary
            {
                Id = GetInt64(a["id"]),
                Name = GetString(a["name"]) ?? "",
                CoverUrl = GetString(a["picUrl"]) ?? GetString(a["img1v1Url"]) ?? "",
            } : null,
            Artists = item["artists"]?.AsArray().Select(ar => new ArtistSummary
            {
                Id = GetInt64(ar?["id"]),
                Name = GetString(ar?["name"]) ?? "",
            }).ToList() ?? [],
            PublishTime = GetInt64(item["publishTime"]),
            CompanyId = GetInt64(item["companyId"]),
            Company = GetString(item["company"]),
            Description = GetString(item["description"]),
            SubType = GetString(item["subType"]),
            Type = GetString(item["type"]),
            Size = GetInt64(item["size"]),
            Subscribed = GetBoolean(item["isSub"]),
            Tracks = item["songs"]?.AsArray().Select(MapTrackFromNode).Where(x => x is not null).Cast<TrackInfo>().ToList() ?? [],
        };
    }

    private static RecordItem? MapRecordItemFromNode(JsonNode? item)
    {
        if (item is null) return null;
        return new RecordItem
        {
            PlayCount = GetInt32(item["playCount"]),
            Score = GetInt32(item["score"]),
            Song = MapTrackFromNode(item["song"]),
        };
    }

    private static TrackInfo? MapTrackFromNode(JsonNode? item)
    {
        if (item is null) return null;
        return new TrackInfo
        {
            Id = GetInt64(item["id"]),
            Name = GetString(item["name"]) ?? "",
            Alias = item["alia"]?.AsArray().Select(a => GetString(a) ?? "").Where(static s => s.Length > 0).ToList()
                ?? item["alias"]?.AsArray().Select(a => GetString(a) ?? "").Where(static s => s.Length > 0).ToList()
                ?? item["tns"]?.AsArray().Select(a => GetString(a) ?? "").Where(static s => s.Length > 0).ToList()
                ?? [],
            Artists = item["ar"]?.AsArray().Select(ar => new ArtistSummary
            {
                Id = GetInt64(ar?["id"]),
                Name = GetString(ar?["name"]) ?? "",
            }).ToList() ?? item["artists"]?.AsArray().Select(ar => new ArtistSummary
            {
                Id = GetInt64(ar?["id"]),
                Name = GetString(ar?["name"]) ?? "",
            }).ToList() ?? [],
            Album = item["al"] is { } al ? new AlbumSummary
            {
                Id = GetInt64(al["id"]),
                Name = GetString(al["name"]) ?? "",
                CoverUrl = GetString(al["picUrl"]) ?? GetString(al["pic"]) ?? GetString(al["blurPicUrl"]) ?? "",
            } : null,
            Duration = GetInt64(item["dt"]) != 0 ? GetInt64(item["dt"]) : GetInt64(item["duration"]),
            IsLiked = GetBoolean(item["liked"]),
            Fee = GetInt32(item["fee"]),
            Mp3Url = GetString(item["mp3Url"]),
            Br = GetInt64(item["br"]),
            DiscNumber = GetInt32(item["cd"], 1),
            TrackNumber = GetInt32(item["no"]),
        };
    }

    private static string? GetString(JsonNode? node)
    {
        if (node is null)
        {
            return null;
        }

        if (node is JsonValue value && value.TryGetValue<string>(out var text))
        {
            return text;
        }

        return node.ToString();
    }

    private static int GetInt32(JsonNode? node, int fallback = 0)
    {
        if (node is null)
        {
            return fallback;
        }

        if (node is JsonValue value)
        {
            if (value.TryGetValue<int>(out var intValue))
            {
                return intValue;
            }

            if (value.TryGetValue<long>(out var longValue))
            {
                return (int)longValue;
            }

            if (value.TryGetValue<string>(out var stringValue) && int.TryParse(stringValue, out var parsed))
            {
                return parsed;
            }
        }

        return fallback;
    }

    private static long GetInt64(JsonNode? node, long fallback = 0)
    {
        if (node is null)
        {
            return fallback;
        }

        if (node is JsonValue value)
        {
            if (value.TryGetValue<long>(out var longValue))
            {
                return longValue;
            }

            if (value.TryGetValue<int>(out var intValue))
            {
                return intValue;
            }

            if (value.TryGetValue<string>(out var stringValue) && long.TryParse(stringValue, out var parsed))
            {
                return parsed;
            }
        }

        return fallback;
    }

    private static bool GetBoolean(JsonNode? node, bool fallback = false)
    {
        if (node is null)
        {
            return fallback;
        }

        if (node is JsonValue value)
        {
            if (value.TryGetValue<bool>(out var boolValue))
            {
                return boolValue;
            }

            if (value.TryGetValue<int>(out var intValue))
            {
                return intValue != 0;
            }

            if (value.TryGetValue<long>(out var longValue))
            {
                return longValue != 0;
            }

            if (value.TryGetValue<string>(out var stringValue))
            {
                if (bool.TryParse(stringValue, out var parsedBool))
                {
                    return parsedBool;
                }

                if (long.TryParse(stringValue, out var parsedLong))
                {
                    return parsedLong != 0;
                }
            }
        }

        return fallback;
    }

    private static MvInfo? MapMvFromNode(JsonNode? item)
    {
        if (item is null) return null;
        return new MvInfo
        {
            Id = item["id"]?.GetValue<long>() ?? 0,
            Name = item["name"]?.GetValue<string>() ?? item["title"]?.GetValue<string>() ?? "",
            CoverUrl = item["cover"]?.GetValue<string>() ?? item["imgurl"]?.GetValue<string>() ?? item["picUrl"]?.GetValue<string>() ?? "",
            PlayCount = item["playCount"]?.GetValue<long>() ?? item["playcount"]?.GetValue<long>() ?? 0,
            Duration = item["duration"]?.GetValue<long>() ?? item["durationms"]?.GetValue<long>() ?? 0,
            PublishTime = item["publishTime"]?.GetValue<long>() ?? 0,
            BriefDesc = item["desc"]?.GetValue<string>() ?? item["briefDesc"]?.GetValue<string>(),
            Artists = item["artists"]?.AsArray().Select(ar => new ArtistSummary
            {
                Id = ar!["id"]?.GetValue<long>() ?? 0,
                Name = ar["name"]?.GetValue<string>() ?? "",
            }).ToList() ?? new List<ArtistSummary>
            {
                new() { Id = item["artistId"]?.GetValue<long>() ?? 0, Name = item["artistName"]?.GetValue<string>() ?? "" },
            },
            ArtistName = item["artistName"]?.GetValue<string>() ?? "",
            Subbed = item["subed"]?.GetValue<bool>() ?? false,
            SubCount = item["subCount"]?.GetValue<long>() ?? 0,
            ShareCount = item["shareCount"]?.GetValue<long>() ?? 0,
            CommentCount = item["commentCount"]?.GetValue<long>() ?? 0,
        };
    }

    private static TrackUrlInfo? MapTrackUrl(JsonNode? item)
    {
        if (item is null) return null;
        return new TrackUrlInfo
        {
            Id = item["id"]?.GetValue<long>() ?? 0,
            Url = item["url"]?.GetValue<string>(),
            Br = item["br"]?.GetValue<long>() ?? 0,
            Size = item["size"]?.GetValue<long>() ?? 0,
            Type = item["type"]?.GetValue<string>(),
            Code = item["code"]?.GetValue<int>() ?? -1,
            Expi = item["expi"]?.GetValue<long>() ?? 0,
            Fee = item["fee"]?.GetValue<int>() ?? 0,
            Payed = item["payed"]?.GetValue<int>() ?? 0,
            CanExtend = item["canExtend"]?.GetValue<bool>() ?? false,
            Md5 = item["md5"]?.GetValue<string>(),
            Level = item["level"]?.GetValue<string>(),
            Sr = item["sr"]?.GetValue<long>() ?? 0,
        };
    }

    private static SongMusicDetailResult? MapSongMusicDetail(JsonNode? node)
    {
        if (node is not JsonObject obj)
        {
            return null;
        }

        var result = new SongMusicDetailResult();
        foreach (var pair in obj)
        {
            if (pair.Value is not JsonObject qualityNode)
            {
                continue;
            }

            var bitrate = GetInt64(qualityNode["br"]);
            var sampleRate = GetInt64(qualityNode["sr"]);
            if (bitrate <= 0 && sampleRate <= 0)
            {
                continue;
            }

            result.Qualities[pair.Key] = new TrackAudioQualityInfo
            {
                Level = pair.Key,
                Bitrate = bitrate,
                SampleRate = sampleRate,
                Size = GetInt64(qualityNode["size"]),
                VolumeDelta = qualityNode["vd"]?.GetValue<double>() ?? 0,
            };
        }

        return result;
    }

    private static MvUrlInfo? MapMvUrl(JsonNode? item)
    {
        if (item is null) return null;
        return new MvUrlInfo
        {
            Id = item["id"]?.GetValue<long>() ?? 0,
            Url = item["url"]?.GetValue<string>(),
            R = item["r"]?.GetValue<int>() ?? 0,
            Size = item["size"]?.GetValue<long>() ?? 0,
            Code = item["code"]?.GetValue<int>() ?? -1,
            Expi = item["expi"]?.GetValue<long>() ?? 0,
            Md5 = item["md5"]?.GetValue<string>(),
        };
    }

    private static SearchResult ParseSearchResult(JsonNode? root)
    {
        if (root is null) return new SearchResult();
        return new SearchResult
        {
            Songs = ParseSearchSection(root["songs"], root["songCount"]?.GetValue<int>() ?? 0),
            Albums = ParseSearchSection(root["albums"], root["albumCount"]?.GetValue<int>() ?? 0),
            Artists = ParseSearchSection(root["artists"], root["artistCount"]?.GetValue<int>() ?? 0),
            Playlists = ParseSearchSection(root["playlists"], root["playlistCount"]?.GetValue<int>() ?? 0),
            Mvs = ParseSearchSection(root["mvs"], root["mvCount"]?.GetValue<int>() ?? 0),
            Videos = ParseSearchSection(root["videos"], root["videoCount"]?.GetValue<int>() ?? 0),
            DjRadios = ParseSearchSection(root["djRadios"], root["djRadiosCount"]?.GetValue<int>() ?? 0),
            Users = ParseSearchSection(root["userprofiles"], root["userprofileCount"]?.GetValue<int>() ?? 0),
            TotalCount = root["songCount"]?.GetValue<int>() ?? 0,
        };
    }

    private static SearchSection? ParseSearchSection(JsonNode? node, int total)
    {
        if (node is null) return null;

        // Handle direct array response (e.g., "songs": [...])
        if (node is JsonArray arr)
        {
            return new SearchSection
            {
                More = false,
                Total = total > 0 ? total : arr.Count,
                Items = arr.Select(x => (object)x!).ToList(),
            };
        }

        // Handle object response (e.g., "songs": { "more": ..., "item": {...}, "songs": [...] })
        if (node is JsonObject obj)
        {
            var more = obj.TryGetPropertyValue("more", out var moreNode) && moreNode?.GetValue<bool>() == true;
            var sectionTotal = total;
            if (sectionTotal <= 0 && obj.TryGetPropertyValue("count", out var countNode))
            {
                sectionTotal = countNode?.GetValue<int>() ?? 0;
            }

            List<object> items;
            if (obj.TryGetPropertyValue("item", out var itemNode) && itemNode is not null)
            {
                items = [itemNode];
            }
            else if (obj.TryGetPropertyValue("songs", out var songsNode) && songsNode is JsonArray songsArr)
            {
                items = songsArr.Select(x => (object)x!).ToList();
            }
            else if (obj.TryGetPropertyValue("albums", out var albumsNode) && albumsNode is JsonArray albumsArr)
            {
                items = albumsArr.Select(x => (object)x!).ToList();
            }
            else if (obj.TryGetPropertyValue("artists", out var artistsNode) && artistsNode is JsonArray artistsArr)
            {
                items = artistsArr.Select(x => (object)x!).ToList();
            }
            else
            {
                items = [];
            }

            return new SearchSection
            {
                More = more,
                Total = sectionTotal,
                Items = items,
            };
        }

        return null;
    }

    private static CommentResult ParseCommentResult(JsonNode? root)
    {
        if (root is null) return new CommentResult();
        var data = root["data"] ?? root;
        return new CommentResult
        {
            Comments = (data["comments"]?.AsArray() ?? Enumerable.Empty<JsonNode>())
                .Select(ParseComment).Where(c => c is not null).Cast<CommentInfo>().ToList(),
            HotComments = (data["hotComments"]?.AsArray() ?? Enumerable.Empty<JsonNode>())
                .Select(ParseComment).Where(c => c is not null).Cast<CommentInfo>().ToList(),
            Total = data["totalCount"]?.GetValue<long>() ?? data["total"]?.GetValue<long>() ?? 0,
            More = data["more"]?.GetValue<bool>() ?? false,
            Cursor = data["cursor"]?.GetValue<long>() ?? 0,
        };
    }

    private static CommentInfo? ParseComment(JsonNode? item)
    {
        if (item is null) return null;
        var user = item["user"];
        return new CommentInfo
        {
            CommentId = item["commentId"]?.GetValue<long>() ?? 0,
            Content = item["content"]?.GetValue<string>() ?? "",
            Time = item["time"]?.GetValue<long>() ?? 0,
            LikedCount = item["likedCount"]?.GetValue<long>() ?? 0,
            Liked = item["liked"]?.GetValue<bool>() ?? false,
            User = user is null ? null : new CommentUser
            {
                UserId = user["userId"]?.GetValue<long>() ?? 0,
                Nickname = user["nickname"]?.GetValue<string>() ?? "",
                AvatarUrl = user["avatarUrl"]?.GetValue<string>() ?? "",
                UserType = user["userType"]?.GetValue<int>() ?? 0,
                AuthStatus = user["authStatus"]?.GetValue<int>() ?? 0,
                VipType = user["vipType"]?.GetValue<string>(),
            },
            Replies = item["beReplied"]?.AsArray().Select(r =>
            {
                var ru = r!["user"];
                return new CommentInfo
                {
                    CommentId = 0,
                    Content = r["content"]?.GetValue<string>() ?? "",
                    User = ru is null ? null : new CommentUser
                    {
                        UserId = ru["userId"]?.GetValue<long>() ?? 0,
                        Nickname = ru["nickname"]?.GetValue<string>() ?? "",
                        AvatarUrl = ru["avatarUrl"]?.GetValue<string>() ?? "",
                    },
                };
            }).ToList(),
            ParentCommentId = item["parentCommentId"]?.GetValue<long>() ?? 0,
        };
    }

    private static UserFollowResult ParseFollowResult(JsonNode root)
    {
        return new UserFollowResult
        {
            Follow = (root["follow"]?.AsArray() ?? Enumerable.Empty<JsonNode>())
                .Select(f => new UserProfile
                {
                    UserId = f!["userId"]?.GetValue<long>() ?? 0,
                    Nickname = f["nickname"]?.GetValue<string>() ?? "",
                    AvatarUrl = f["avatarUrl"]?.GetValue<string>() ?? "",
                }).ToList(),
            More = root["more"]?.GetValue<bool>() ?? false,
            Total = root["total"]?.GetValue<int>() ?? 0,
        };
    }

    private static bool IsTransientStatusCode(HttpStatusCode statusCode) => statusCode is
        HttpStatusCode.BadGateway or
        HttpStatusCode.ServiceUnavailable or
        HttpStatusCode.GatewayTimeout;

    private static bool IsTransientException(Exception exception) => exception switch
    {
        HttpRequestException httpRequestException when httpRequestException.StatusCode is null
            or HttpStatusCode.BadGateway
            or HttpStatusCode.ServiceUnavailable
            or HttpStatusCode.GatewayTimeout => true,
        TaskCanceledException => true,
        _ => false,
    };

    public void Dispose()
    {
        _noRedirectHttpClient.Dispose();
        _httpClient.Dispose();
    }
}

internal sealed class CachedApiResponse
{
    public string Data { get; set; } = string.Empty;
}
