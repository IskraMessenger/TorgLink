using System.Globalization;
using TorgLink.Localization;
using TorgLink.Maui.Services;

namespace TorgLink.Maui.Localization;

internal static class MauiLanguageBootstrap
{
    private static bool _hooked;

    public static void Initialize()
    {
        LanguageService.Configure(new MauiLanguagePreferences(), ApplyCulture);
        if (!_hooked)
        {
            _hooked = true;
            LanguageService.Changed += (_, _) =>
                AppLog.SettingChanged("Language", LanguageService.Current);
        }

        LanguageService.Load();
    }

    private static void ApplyCulture(AppLanguage language)
    {
        var culture = LanguageService.ToCulture(language);
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
    }
}
