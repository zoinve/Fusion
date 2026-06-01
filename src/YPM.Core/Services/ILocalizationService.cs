namespace YPM.Core.Services;

public enum AppLanguage
{
    Chinese,
    English,
    TraditionalChinese,
    Turkish,
}

public interface ILocalizationService
{
    event EventHandler<AppLanguage>? LanguageChanged;

    AppLanguage CurrentLanguage { get; }

    string GetString(string resourceKey);

    void SetLanguage(AppLanguage language);

    IReadOnlyList<AppLanguage> SupportedLanguages { get; }

    string GetLanguageName(AppLanguage language);
}
