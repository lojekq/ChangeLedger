using System.Windows.Interop;

namespace TrayApp.Shell;

/// <summary>
/// One hidden window per process for WM_HOTKEY, WM_DISPLAYCHANGE, WM_SETTINGCHANGE, WM_POWERBROADCAST and the shell hook.
/// A hidden top-level tool window, not HWND_MESSAGE: message-only windows get no broadcasts.
/// Create and subscribe on the UI thread.
/// </summary>
public static class MessageWindow
{
    public const int WM_DISPLAYCHANGE = 0x007E, WM_SETTINGCHANGE = 0x001A, WM_POWERBROADCAST = 0x0218, WM_HOTKEY = 0x0312;

    static HwndSource? source;
    static HwndSourceHook? handlers;
    static readonly HwndSourceHook Hook = OnMessage; // kept in a field: the source must not lose it to GC

    public static nint Handle => (source ??= Create()).Handle;

    /// <summary>Handlers run in subscription order until one sets handled.</summary>
    public static event HwndSourceHook WndProc
    {
        add { _ = Handle; handlers += value; }
        remove => handlers -= value;
    }

    static HwndSource Create()
    {
        const int WS_POPUP = unchecked((int)0x80000000), WS_EX_TOOLWINDOW = 0x80;
        var s = new HwndSource(new HwndSourceParameters($"{AppInfo.Name}.MessageWindow")
        {
            WindowStyle = WS_POPUP, ExtendedWindowStyle = WS_EX_TOOLWINDOW, Width = 0, Height = 0,
        });
        s.AddHook(Hook);
        return s;
    }

    static nint OnMessage(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (handlers is null) return 0;
        foreach (HwndSourceHook h in handlers.GetInvocationList())
        {
            var r = h(hwnd, msg, wParam, lParam, ref handled);
            if (handled) return r;
        }
        return 0;
    }
}
