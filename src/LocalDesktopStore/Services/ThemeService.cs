using System.Windows;
using System.Windows.Media;
using LocalDesktopStore.Models;

namespace LocalDesktopStore.Services;

public static class ThemeService
{
    private const string DarkTheme = "Themes/DarkTheme.xaml";
    private const string LightTheme = "Themes/LightTheme.xaml";

    public static void Apply(AppSettings settings)
        => Apply(settings.UseLightTheme, settings.UseSystemAccent);

    public static void Apply(bool useLightTheme, bool useSystemAccent)
    {
        var application = Application.Current;
        if (application is null)
            return;

        var dictionary = new ResourceDictionary
        {
            Source = new Uri(useLightTheme ? LightTheme : DarkTheme, UriKind.Relative)
        };
        var merged = application.Resources.MergedDictionaries;
        if (merged.Count == 0)
            merged.Add(dictionary);
        else
        {
            merged.RemoveAt(0);
            merged.Insert(0, dictionary);
        }

        application.Resources.Remove("MauveBrush");
        application.Resources.Remove("FocusRingBrush");
        if (useSystemAccent)
            ApplySystemAccent(application.Resources);
    }

    private static void ApplySystemAccent(ResourceDictionary resources)
    {
        var accent = SystemParameters.WindowGlassBrush?.CloneCurrentValue();
        if (accent is null)
            return;

        if (accent.CanFreeze)
            accent.Freeze();
        resources["MauveBrush"] = accent;

        var focusRing = accent.CloneCurrentValue();
        if (focusRing.CanFreeze)
            focusRing.Freeze();
        resources["FocusRingBrush"] = focusRing;
    }
}
