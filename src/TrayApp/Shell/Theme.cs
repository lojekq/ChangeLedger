using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace TrayApp.Shell;

public static class Theme
{
    /// <summary>
    /// Setting system | light | dark (--theme wins). Mica normally; for --screenshot and --autoplay a solid
    /// background, square corners and Topmost, so the capture has no wallpaper and no neighbours.
    /// </summary>
    public static void Apply(FluentWindow w, string setting)
    {
        var a = Args.Current;
        var capture = a.Screenshot is not null || a.Autoplay;
        var backdrop = capture ? WindowBackdropType.None : WindowBackdropType.Mica;
        var theme = a.Theme ?? setting;

        if (w.IsLoaded) SystemThemeWatcher.UnWatch(w); // throws before Loaded
        if (theme is "light" or "dark")
            ApplicationThemeManager.Apply(theme == "dark" ? ApplicationTheme.Dark : ApplicationTheme.Light, backdrop);
        else
        {
            ApplicationThemeManager.Apply(ApplicationThemeManager.GetSystemTheme() == SystemTheme.Dark ? ApplicationTheme.Dark : ApplicationTheme.Light, backdrop);
            SystemThemeWatcher.Watch(w, backdrop);
        }
        w.WindowBackdropType = backdrop;

        if (capture)
        {
            w.SetResourceReference(System.Windows.Controls.Control.BackgroundProperty, "ApplicationBackgroundBrush");
            w.WindowCornerPreference = WindowCornerPreference.DoNotRound;
            w.Topmost = true;
        }
    }

    public static bool IsDark => ApplicationThemeManager.GetAppTheme() == ApplicationTheme.Dark;
}
