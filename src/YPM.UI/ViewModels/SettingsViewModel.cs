using System.Collections.ObjectModel;
using YPM.Core.Models;
using YPM.Core.Mvvm;
using YPM.Core.Services;
using YPM.UI.Services;

namespace YPM.UI.ViewModels;

public sealed class SettingsViewModel : ObservableObject
{
    private bool _isLoading;
    private string _errorMessage = string.Empty;

    public bool IsLoading
    {
        get => _isLoading;
        set => SetProperty(ref _isLoading, value);
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        set => SetProperty(ref _errorMessage, value);
    }

    // ── Language ──
    public ObservableCollection<LanguageOption> LanguageOptions { get; } = new()
    {
        new LanguageOption(AppLanguage.Chinese, "简体中文"),
        new LanguageOption(AppLanguage.English, "English"),
        new LanguageOption(AppLanguage.TraditionalChinese, "繁體中文"),
        new LanguageOption(AppLanguage.Turkish, "Türkçe"),
    };

    private LanguageOption _selectedLanguage = null!;
    public LanguageOption SelectedLanguage
    {
        get => _selectedLanguage;
        set
        {
            if (SetProperty(ref _selectedLanguage, value) && value is not null)
            {
                App.Settings.Lang = ResourceLocalizationService.GetLanguageTag(value.Language);
                App.LocalizationService?.SetLanguage(value.Language);
                _ = SaveSettingsAsync();
            }
        }
    }

    // ── Appearance ──
    public ObservableCollection<AppearanceOption> AppearanceOptions { get; } = new()
    {
        new AppearanceOption("auto", "跟随系统"),
        new AppearanceOption("light", "浅色"),
        new AppearanceOption("dark", "深色"),
    };

    private AppearanceOption _selectedAppearance = null!;
    public AppearanceOption SelectedAppearance
    {
        get => _selectedAppearance;
        set
        {
            if (SetProperty(ref _selectedAppearance, value) && value is not null)
            {
                App.Settings.Appearance = value.Value;
                _ = SaveSettingsAsync();
                ApplyTheme();
            }
        }
    }

    // ── Music Language ──
    public ObservableCollection<MusicLanguageOption> MusicLanguageOptions { get; } = new()
    {
        new MusicLanguageOption("ALL", "全部"),
        new MusicLanguageOption("ZH", "华语"),
        new MusicLanguageOption("EA", "欧美"),
        new MusicLanguageOption("JP", "日语"),
        new MusicLanguageOption("KR", "韩语"),
    };

    private MusicLanguageOption _selectedMusicLanguage = null!;
    public MusicLanguageOption SelectedMusicLanguage
    {
        get => _selectedMusicLanguage;
        set
        {
            if (SetProperty(ref _selectedMusicLanguage, value) && value is not null)
            {
                App.Settings.MusicLanguage = value.Value;
                _ = SaveSettingsAsync();
            }
        }
    }

    // ── Music Quality ──
    public ObservableCollection<QualityOption> QualityOptions { get; } = new()
    {
        new QualityOption(128000, "标准 (128 kbps)"),
        new QualityOption(192000, "较高 (192 kbps)"),
        new QualityOption(320000, "极高 (320 kbps)"),
        new QualityOption(999000, "无损 (FLAC)"),
    };

    private QualityOption _selectedQuality = null!;
    public QualityOption SelectedQuality
    {
        get => _selectedQuality;
        set
        {
            if (SetProperty(ref _selectedQuality, value) && value is not null)
            {
                App.Settings.MusicQuality = value.Bitrate;
                _ = SaveSettingsAsync();
            }
        }
    }

    // ── Proxy ──
    public ObservableCollection<ProxyProtocolOption> ProxyProtocolOptions { get; } = new()
    {
        new ProxyProtocolOption("noProxy", "不使用代理"),
        new ProxyProtocolOption("HTTP", "HTTP"),
        new ProxyProtocolOption("HTTPS", "HTTPS"),
    };

    private ProxyProtocolOption _selectedProxyProtocol = null!;
    public ProxyProtocolOption SelectedProxyProtocol
    {
        get => _selectedProxyProtocol;
        set
        {
            if (SetProperty(ref _selectedProxyProtocol, value) && value is not null)
            {
                App.Settings.ProxyProtocol = value.Value;
                _ = SaveSettingsAsync();
            }
        }
    }

    private string _proxyServer = string.Empty;
    public string ProxyServer
    {
        get => _proxyServer;
        set
        {
            if (SetProperty(ref _proxyServer, value))
            {
                App.Settings.ProxyServer = value;
                _ = SaveSettingsAsync();
            }
        }
    }

    private int _proxyPort = 8080;
    public int ProxyPort
    {
        get => _proxyPort;
        set
        {
            if (SetProperty(ref _proxyPort, value))
            {
                App.Settings.ProxyPort = value;
                _ = SaveSettingsAsync();
            }
        }
    }

    // ── Real IP ──
    private bool _enableRealIP = true;
    public bool EnableRealIP
    {
        get => _enableRealIP;
        set
        {
            if (SetProperty(ref _enableRealIP, value))
            {
                App.Settings.EnableRealIP = value;
                _ = SaveSettingsAsync();
            }
        }
    }

    private string _realIP = string.Empty;
    public string RealIP
    {
        get => _realIP;
        set
        {
            if (SetProperty(ref _realIP, value))
            {
                App.Settings.RealIP = value;
                _ = SaveSettingsAsync();
            }
        }
    }

    // ── Cache ──
    private bool _autoCacheSongs;
    public bool AutoCacheSongs
    {
        get => _autoCacheSongs;
        set
        {
            if (SetProperty(ref _autoCacheSongs, value))
            {
                App.Settings.AutomaticallyCacheSongs = value;
                _ = SaveSettingsAsync();
            }
        }
    }

    private long _cacheLimit;
    public long CacheLimit
    {
        get => _cacheLimit;
        set
        {
            if (SetProperty(ref _cacheLimit, value))
            {
                App.Settings.CacheLimit = value;
                _ = SaveSettingsAsync();
                OnPropertyChanged(nameof(CacheLimitText));
                OnPropertyChanged(nameof(CacheLimitValue));
                App.ReinitializeMusicCacheService();
                _ = RefreshCacheSizeAsync();
            }
        }
    }

    private string _cacheLocation = string.Empty;
    public string CacheLocation
    {
        get => _cacheLocation;
        set
        {
            if (SetProperty(ref _cacheLocation, value))
            {
                App.Settings.CacheLocation = value;
                _ = SaveSettingsAsync();
                App.ReinitializeMusicCacheService();
                _ = RefreshCacheSizeAsync();
            }
        }
    }

    private long _currentCacheSize;
    public long CurrentCacheSize
    {
        get => _currentCacheSize;
        set => SetProperty(ref _currentCacheSize, value);
    }

    public string CurrentCacheSizeText => FormatSize(_currentCacheSize);

    public string DefaultCacheLocationText => FileBasedMusicCacheService.GetDefaultCachePath();

    public string CacheLocationPlaceholder => DefaultCacheLocationText;

    public string CacheLimitText => $"{CacheLimit} MB";

    public double CacheLimitValue
    {
        get => CacheLimit;
        set => CacheLimit = (long)Math.Round(value);
    }

    // ── Display ──
    private bool _showPlaylistsByAppleMusic = true;
    public bool ShowPlaylistsByAppleMusic
    {
        get => _showPlaylistsByAppleMusic;
        set
        {
            if (SetProperty(ref _showPlaylistsByAppleMusic, value))
            {
                App.Settings.ShowPlaylistsByAppleMusic = value;
                _ = SaveSettingsAsync();
            }
        }
    }

    private bool _enableAcrylic;
    public bool EnableAcrylic
    {
        get => _enableAcrylic;
        set
        {
            if (SetProperty(ref _enableAcrylic, value))
            {
                App.Settings.EnableAcrylic = value;
                _ = SaveSettingsAsync();
                ApplyAcrylic(value);
            }
        }
    }

    private static void ApplyAcrylic(bool enable)
    {
        App.MainWindow.ApplyAcrylic(enable);
    }

    // ── User Info ──
    public string? CurrentUserName => App.Settings.CurrentUser?.Nickname;
    public bool IsLoggedIn => App.Settings.CurrentUser is not null;

    // ── App Version ──
    public string AppVersion => "1.0.0 (WinUI 3)";

    // ── Load / Save ──
    public void LoadFromSettings()
    {
        var s = App.Settings;

        // Language
        var langTag = s.Lang ?? "zh-Hans";
        var lang = langTag switch
        {
            "en-US" => AppLanguage.English,
            "zh-Hant" => AppLanguage.TraditionalChinese,
            "tr-TR" => AppLanguage.Turkish,
            _ => AppLanguage.Chinese,
        };
        _selectedLanguage = LanguageOptions.FirstOrDefault(l => l.Language == lang) ?? LanguageOptions[0];
        OnPropertyChanged(nameof(SelectedLanguage));

        // Appearance
        _selectedAppearance = AppearanceOptions.FirstOrDefault(a => a.Value == s.Appearance) ?? AppearanceOptions[0];
        OnPropertyChanged(nameof(SelectedAppearance));

        // Music Language
        _selectedMusicLanguage = MusicLanguageOptions.FirstOrDefault(m => m.Value == s.MusicLanguage) ?? MusicLanguageOptions[0];
        OnPropertyChanged(nameof(SelectedMusicLanguage));

        // Music Quality
        _selectedQuality = QualityOptions.FirstOrDefault(q => q.Bitrate == s.MusicQuality) ?? QualityOptions[2];
        OnPropertyChanged(nameof(SelectedQuality));

        // Proxy
        _selectedProxyProtocol = ProxyProtocolOptions.FirstOrDefault(p => p.Value == s.ProxyProtocol) ?? ProxyProtocolOptions[0];
        OnPropertyChanged(nameof(SelectedProxyProtocol));
        _proxyServer = s.ProxyServer;
        OnPropertyChanged(nameof(ProxyServer));
        _proxyPort = s.ProxyPort;
        OnPropertyChanged(nameof(ProxyPort));

        // Real IP
        _enableRealIP = s.EnableRealIP;
        OnPropertyChanged(nameof(EnableRealIP));
        _realIP = s.RealIP;
        OnPropertyChanged(nameof(RealIP));

        // Cache
        _autoCacheSongs = s.AutomaticallyCacheSongs;
        OnPropertyChanged(nameof(AutoCacheSongs));
        _cacheLimit = s.CacheLimit > 0 ? s.CacheLimit : 2048;
        OnPropertyChanged(nameof(CacheLimit));
        OnPropertyChanged(nameof(CacheLimitText));
        OnPropertyChanged(nameof(CacheLimitValue));
        _cacheLocation = s.CacheLocation;
        OnPropertyChanged(nameof(CacheLocation));

        // Display
        _showPlaylistsByAppleMusic = s.ShowPlaylistsByAppleMusic;
        OnPropertyChanged(nameof(ShowPlaylistsByAppleMusic));

        _enableAcrylic = s.EnableAcrylic;
        OnPropertyChanged(nameof(EnableAcrylic));
    }

    private async Task SaveSettingsAsync()
    {
        try
        {
            if (App.SettingsService is not null)
                await App.SettingsService.SaveAsync(App.Settings);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"保存设置失败: {ex.Message}";
        }
    }

    public async Task RefreshCacheSizeAsync()
    {
        if (App.MusicCacheService is null) return;
        CurrentCacheSize = await App.MusicCacheService.GetCacheSizeAsync();
        OnPropertyChanged(nameof(CurrentCacheSizeText));
    }

    public async Task ClearCacheAsync()
    {
        if (App.MusicCacheService is null) return;
        await App.MusicCacheService.ClearAllAsync();
        App.ReinitializeMusicCacheService();
        await RefreshCacheSizeAsync();
    }

    public async Task UpdateCacheLocationAsync(string newLocation)
    {
        CacheLocation = newLocation;
    }

    private static string FormatSize(long bytes)
    {
        return bytes switch
        {
            >= 1024 * 1024 * 1024 => $"{bytes / (1024.0 * 1024 * 1024):F1} GB",
            >= 1024 * 1024 => $"{bytes / (1024.0 * 1024):F1} MB",
            >= 1024 => $"{bytes / 1024.0:F1} KB",
            _ => $"{bytes} B",
        };
    }

    private void ApplyTheme()
    {
        var theme = App.Settings.Appearance switch
        {
            "light" => Microsoft.UI.Xaml.ElementTheme.Light,
            "dark" => Microsoft.UI.Xaml.ElementTheme.Dark,
            _ => Microsoft.UI.Xaml.ElementTheme.Default,
        };

        if (App.MainWindow?.Content is Microsoft.UI.Xaml.FrameworkElement fe)
        {
            fe.RequestedTheme = theme;
        }
    }
}

// ── Setting Option Types ──
public sealed class LanguageOption
{
    public AppLanguage Language { get; }
    public string DisplayName { get; }
    public LanguageOption(AppLanguage language, string displayName)
    {
        Language = language;
        DisplayName = displayName;
    }

    public override string ToString() => DisplayName;
}

public sealed class AppearanceOption
{
    public string Value { get; }
    public string DisplayName { get; }
    public AppearanceOption(string value, string displayName)
    {
        Value = value;
        DisplayName = displayName;
    }

    public override string ToString() => DisplayName;
}

public sealed class MusicLanguageOption
{
    public string Value { get; }
    public string DisplayName { get; }
    public MusicLanguageOption(string value, string displayName)
    {
        Value = value;
        DisplayName = displayName;
    }

    public override string ToString() => DisplayName;
}

public sealed class QualityOption
{
    public long Bitrate { get; }
    public string DisplayName { get; }
    public QualityOption(long bitrate, string displayName)
    {
        Bitrate = bitrate;
        DisplayName = displayName;
    }

    public override string ToString() => DisplayName;
}

public sealed class ProxyProtocolOption
{
    public string Value { get; }
    public string DisplayName { get; }
    public ProxyProtocolOption(string value, string displayName)
    {
        Value = value;
        DisplayName = displayName;
    }

    public override string ToString() => DisplayName;
}
