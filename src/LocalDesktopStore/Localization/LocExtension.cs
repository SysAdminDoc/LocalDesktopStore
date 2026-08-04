using System.Windows.Data;
using System.Windows.Markup;
using System.Windows;

namespace LocalDesktopStore.Localization;

[MarkupExtensionReturnType(typeof(string))]
public sealed class LocExtension : MarkupExtension
{
    public LocExtension()
    {
    }

    public LocExtension(string key)
    {
        Key = key;
    }

    public string Key { get; set; } = string.Empty;

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        if (string.IsNullOrWhiteSpace(Key))
            return string.Empty;

        return new Binding
        {
            Source = LocalizationProvider.Instance,
            Path = new PropertyPath($"[{Key}]"),
            Mode = BindingMode.OneWay
        }.ProvideValue(serviceProvider);
    }
}
