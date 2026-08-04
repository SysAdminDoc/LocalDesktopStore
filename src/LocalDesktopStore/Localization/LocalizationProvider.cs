using System.ComponentModel;
using System.Globalization;
using System.Resources;

namespace LocalDesktopStore.Localization;

public sealed record LanguageChoice(string Code, string DisplayName);

public sealed class LocalizationProvider : INotifyPropertyChanged
{
    private static readonly ResourceManager Resources = new(
        "LocalDesktopStore.Localization.Strings",
        typeof(LocalizationProvider).Assembly);

    private CultureInfo _culture = CultureInfo.GetCultureInfo("en");

    private LocalizationProvider()
    {
    }

    public static LocalizationProvider Instance { get; } = new();

    public static IReadOnlyList<LanguageChoice> LanguageOptions { get; } =
    [
        new("system", "System default"),
        new("en", "English"),
        new("es", "Español")
    ];

    public event PropertyChangedEventHandler? PropertyChanged;

    public string this[string key] => Resources.GetString(key, _culture) ?? key;

    public string CurrentLanguage => _culture.Name;

    public void SetLanguage(string? code)
    {
        var normalized = string.IsNullOrWhiteSpace(code) ? "en" : code.Trim().ToLowerInvariant();
        var culture = normalized switch
        {
            "en" => CultureInfo.GetCultureInfo("en"),
            "es" => CultureInfo.GetCultureInfo("es"),
            "system" => CultureInfo.InstalledUICulture,
            _ => CultureInfo.GetCultureInfo("en")
        };

        _culture = culture;
        CultureInfo.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentLanguage)));
    }

    public static string Get(string key, CultureInfo? culture = null)
        => Resources.GetString(key, culture ?? Instance._culture) ?? key;
}
