namespace YPM.Core.Models;

public sealed class AppSettings
{
    public ApiOptions Api { get; set; } = new();

    public string SessionCookie { get; set; } = string.Empty;

    public UserProfile? CurrentUser { get; set; }

    public PlaybackSessionState? LastPlayback { get; set; }

    public int LastCookieRefreshDay { get; set; }

    // ── Language ──
    public string Lang { get; set; } = "zh-Hans";

    // ── Appearance ──
    public string Appearance { get; set; } = "auto";

    // ── Music Preference ──
    public string MusicLanguage { get; set; } = "ALL";

    // ── Music Quality ──
    public long MusicQuality { get; set; } = 999000;

    // ── Lyrics ──
    public bool ShowLyricsTranslation { get; set; } = true;
    public string LyricsBackground { get; set; } = "false";
    public int LyricFontSize { get; set; } = 22;

    // ── Proxy ──
    public string ProxyProtocol { get; set; } = "noProxy";
    public string ProxyServer { get; set; } = string.Empty;
    public int ProxyPort { get; set; } = 8080;

    // ── Real IP ──
    public bool EnableRealIP { get; set; } = true;
    public string RealIP { get; set; } = "211.161.244.70";

    // ── Cache ──
    public bool AutomaticallyCacheSongs { get; set; } = true;
    public long CacheLimit { get; set; } = 2048;
    public string CacheLocation { get; set; } = string.Empty;

    // ── Display ──
    public bool ShowPlaylistsByAppleMusic { get; set; } = true;
    public bool SubTitleDefault { get; set; }
    public bool EnableReversedMode { get; set; }
    public bool EnableAcrylic { get; set; }
}
