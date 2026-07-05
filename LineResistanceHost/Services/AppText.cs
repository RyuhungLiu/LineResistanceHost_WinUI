using System.Globalization;
using Microsoft.Windows.ApplicationModel.Resources;
using Microsoft.Windows.Globalization;

namespace LineResistanceHost.Services;

public enum AppLanguage
{
    SimplifiedChinese,
    TraditionalChinese,
    English
}

public sealed record AppLanguageOption(AppLanguage Language, string NativeName);

public static class AppText
{
    private static readonly string LanguageSettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LineResistanceHost",
        "language.txt");

    private static ResourceLoader? _resourceLoader;

    public static IReadOnlyList<AppLanguageOption> Languages { get; } =
    [
        new(AppLanguage.SimplifiedChinese, "简体中文"),
        new(AppLanguage.TraditionalChinese, "繁體中文"),
        new(AppLanguage.English, "English")
    ];

    public static event EventHandler? LanguageChanged;

    public static AppLanguage CurrentLanguage { get; private set; } = LoadLanguage();

    static AppText()
    {
        ApplyCulture(CurrentLanguage);
    }

    public static string Get(string key)
    {
        try
        {
            _resourceLoader ??= new ResourceLoader();
            var value = _resourceLoader.GetString(key);
            if (!string.IsNullOrEmpty(value))
            {
                return value;
            }
        }
        catch
        {
            // A missing PRI should not prevent hardware logging or startup.
        }

        return key;
    }

    public static string Format(string key, params object?[] args)
    {
        return string.Format(CultureInfo.CurrentCulture, Get(key), args);
    }

    public static void SetLanguage(AppLanguage language)
    {
        if (CurrentLanguage == language)
        {
            return;
        }

        CurrentLanguage = language;
        ApplyCulture(language);
        SaveLanguage(language);
        LanguageChanged?.Invoke(null, EventArgs.Empty);
    }

    private static void ApplyCulture(AppLanguage language)
    {
        var cultureName = CultureName(language);
        var culture = CultureInfo.GetCultureInfo(cultureName);
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
        try
        {
            ApplicationLanguages.PrimaryLanguageOverride = cultureName;
            _resourceLoader = new ResourceLoader();
        }
        catch
        {
            _resourceLoader = null;
        }
    }

    private static string CultureName(AppLanguage language)
    {
        return language switch
        {
            AppLanguage.TraditionalChinese => "zh-Hant",
            AppLanguage.English => "en-US",
            _ => "zh-Hans"
        };
    }

    private static AppLanguage LoadLanguage()
    {
        try
        {
            if (File.Exists(LanguageSettingsPath)
                && Enum.TryParse<AppLanguage>(File.ReadAllText(LanguageSettingsPath).Trim(), out var language))
            {
                return language;
            }
        }
        catch
        {
            // Localization should never prevent app startup.
        }

        return AppLanguage.SimplifiedChinese;
    }

    private static void SaveLanguage(AppLanguage language)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LanguageSettingsPath)!);
            File.WriteAllText(LanguageSettingsPath, language.ToString());
        }
        catch
        {
            // Localization should keep working even if settings cannot be saved.
        }
    }
}
