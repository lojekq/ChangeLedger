using System.Globalization;
using System.Resources;
using System.Windows.Markup;

namespace TrayApp.Shell;

/// <summary>Resources/Strings.resx (EN, neutral) and Strings.ru.resx. C#: Loc.T("Key"), XAML: {l:T Key}.</summary>
public static class Loc
{
    static readonly ResourceManager Rm = new("TrayApp.Resources.Strings", typeof(Loc).Assembly);

    public static string T(string key) => Rm.GetString(key) ?? $"[{key}]";

    /// <summary>Before any window: setting system | en | ru, --lang wins. A change needs a restart.</summary>
    public static void Apply(string setting)
    {
        var lang = Args.Current.Lang ?? setting;
        if (lang is not ("en" or "ru")) lang = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ru" ? "ru" : "en";
        var c = CultureInfo.GetCultureInfo(lang);
        CultureInfo.DefaultThreadCurrentUICulture = c;
        Thread.CurrentThread.CurrentUICulture = c;
    }
}

/// <summary>{l:T Key} with xmlns:l="clr-namespace:TrayApp.Shell".</summary>
[MarkupExtensionReturnType(typeof(string))]
public sealed class TExtension(string key) : MarkupExtension
{
    public override object ProvideValue(IServiceProvider serviceProvider) => Loc.T(key);
}
