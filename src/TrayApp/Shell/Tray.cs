using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using H.NotifyIcon;
using Microsoft.Win32;

namespace TrayApp.Shell;

/// <summary>
/// Tray icon: left click opens the window, right click shows "Open / app items / Exit".
/// The icon follows the taskbar theme: tray-light.ico (dark glyph) on a light taskbar, tray-dark.ico on a dark one.
/// Notifications are balloons (Windows 11 shows them as toasts without buttons); a click opens the given page.
/// </summary>
public static class Tray
{
    static TaskbarIcon? icon;
    static string? balloonPage;

    public static void Create(Action<string?> open, Action exit, IEnumerable<Control>? appItems = null)
    {
        var menu = new ContextMenu();
        menu.Items.Add(Item(Loc.T("Tray.Open"), () => open(null)));
        if (appItems?.ToList() is { Count: > 0 } items)
        {
            menu.Items.Add(new Separator());
            foreach (var i in items) menu.Items.Add(i);
        }
        menu.Items.Add(new Separator());
        menu.Items.Add(Item(Loc.T("Tray.Exit"), exit));

        icon = new TaskbarIcon { ToolTipText = AppInfo.DisplayName, ContextMenu = menu, NoLeftClickDelay = true };
        icon.TrayLeftMouseUp += (_, _) => open(null);
        icon.TrayBalloonTipClicked += (_, _) => open(balloonPage);
        UpdateIcon();
        MessageWindow.WndProc += (nint _, int msg, nint _, nint lParam, ref bool _) =>
        {
            if (msg == MessageWindow.WM_SETTINGCHANGE) UpdateIcon(); // "ImmersiveColorSet" and friends
            return 0;
        };
        icon.ForceCreate(enablesEfficiencyMode: false); // Efficiency mode would throttle the whole process
    }

    public static MenuItem Item(string header, Action click)
    {
        var m = new MenuItem { Header = header };
        m.Click += (_, _) => click();
        return m;
    }

    public static void Notify(string title, string text, string? page = null)
    {
        balloonPage = page;
        icon?.ShowNotification(title, text);
    }

    public static void Dispose() => icon?.Dispose();

    static void UpdateIcon()
    {
        if (icon is null) return;
        var light = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "SystemUsesLightTheme", 0) is 1;
        var name = light ? "tray-light" : "tray-dark";
        if (Equals(icon.Tag, name)) return;
        icon.Tag = name;
        icon.IconSource = new BitmapImage(new Uri($"pack://application:,,,/Assets/{name}.ico"));
    }
}
