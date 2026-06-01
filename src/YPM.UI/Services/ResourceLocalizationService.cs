using Windows.ApplicationModel.Resources;
using YPM.Core.Services;

namespace YPM.UI.Services;

public sealed class ResourceLocalizationService : ILocalizationService
{
    private ResourceLoader? _resourceLoader;
    private AppLanguage _currentLanguage = AppLanguage.Chinese;

    public event EventHandler<AppLanguage>? LanguageChanged;

    public AppLanguage CurrentLanguage => _currentLanguage;

    public IReadOnlyList<AppLanguage> SupportedLanguages { get; } = new[]
    {
        AppLanguage.Chinese,
        AppLanguage.English,
        AppLanguage.TraditionalChinese,
        AppLanguage.Turkish,
    };

    private ResourceLoader Loader => _resourceLoader ??= ResourceLoader.GetForViewIndependentUse();

    public string GetString(string resourceKey)
    {
        try { return Loader.GetString(resourceKey); }
        catch { return resourceKey; }
    }

    public void SetLanguage(AppLanguage language)
    {
        if (_currentLanguage == language) return;

        Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride = GetLanguageTag(language);
        _currentLanguage = language;
        LanguageChanged?.Invoke(this, language);
    }

    public string GetLanguageName(AppLanguage language) => language switch
    {
        AppLanguage.Chinese => "简体中文",
        AppLanguage.English => "English",
        AppLanguage.TraditionalChinese => "繁體中文",
        AppLanguage.Turkish => "Türkçe",
        _ => "Unknown",
    };

    public static string GetLanguageTag(AppLanguage language) => language switch
    {
        AppLanguage.Chinese => "zh-Hans",
        AppLanguage.English => "en-US",
        AppLanguage.TraditionalChinese => "zh-Hant",
        AppLanguage.Turkish => "tr-TR",
        _ => "en-US",
    };
}
