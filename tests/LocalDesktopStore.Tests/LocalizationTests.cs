using System.Globalization;
using LocalDesktopStore.Localization;
using Xunit;

namespace LocalDesktopStore.Tests;

public sealed class LocalizationTests
{
    [Fact]
    public void SpanishResourcesOverrideNeutralTextAndFallbackForMissingKeys()
    {
        var spanish = CultureInfo.GetCultureInfo("es");

        Assert.Equal("Configuración de descubrimiento", LocalizationProvider.Get("Settings_Title", spanish));
        Assert.Equal("LocalDesktopStore", LocalizationProvider.Get("MainWindow_Title", spanish));
    }

    [Fact]
    public void ExposesSystemEnglishAndSpanishLanguageChoices()
    {
        Assert.Equal(
            ["system", "en", "es"],
            LocalizationProvider.LanguageOptions.Select(choice => choice.Code).ToArray());
    }
}
