using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Dwm;
using Windows.Win32.UI.WindowsAndMessaging;

namespace TrayApp.Shell;

public sealed record TopWindow(nint Hwnd, int Pid, string Class, string Title, bool Cloaked);

/// <summary>
/// Top-level windows with the base filter only. Size, age, owned windows, cloaked and exclusions are the app's call (00-shared §2.4).
/// </summary>
public static unsafe class TopWindows
{
    static readonly HashSet<string> ShellClasses = ["Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd"];

    /// <summary>In Z order, topmost first.</summary>
    public static List<TopWindow> List()
    {
        var list = new List<TopWindow>();
        PInvoke.EnumWindows((h, _) =>
        {
            if (Passes(h)) list.Add(new(h, Pid(h), ClassOf(h), TitleOf(h), IsCloaked(h)));
            return true;
        }, 0);
        return list;
    }

    /// <summary>Visible, not a tool window, unowned or WS_EX_APPWINDOW, not the shell (desktop, taskbars), not our own process.</summary>
    public static bool Passes(nint hwnd)
    {
        var h = (HWND)hwnd;
        if (!PInvoke.IsWindowVisible(h)) return false;
        var ex = (WINDOW_EX_STYLE)PInvoke.GetWindowLong(h, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE);
        if (ex.HasFlag(WINDOW_EX_STYLE.WS_EX_TOOLWINDOW)) return false;
        if (PInvoke.GetWindow(h, GET_WINDOW_CMD.GW_OWNER) != HWND.Null && !ex.HasFlag(WINDOW_EX_STYLE.WS_EX_APPWINDOW)) return false;
        return Pid(hwnd) != Environment.ProcessId && !ShellClasses.Contains(ClassOf(hwnd));
    }

    public static int Pid(nint hwnd)
    {
        PInvoke.GetWindowThreadProcessId((HWND)hwnd, out var pid);
        return (int)pid;
    }

    public static string ClassOf(nint hwnd)
    {
        Span<char> buf = stackalloc char[256];
        return buf[..PInvoke.GetClassName((HWND)hwnd, buf)].ToString();
    }

    // ponytail: titles are cut at 1023 chars; GetWindowTextLength would cost one more call per window
    public static string TitleOf(nint hwnd)
    {
        Span<char> buf = stackalloc char[1024];
        return buf[..PInvoke.GetWindowText((HWND)hwnd, buf)].ToString();
    }

    /// <summary>DWMWA_CLOAKED: on another virtual desktop, a suspended UWP app, hidden by its own app.</summary>
    public static bool IsCloaked(nint hwnd)
    {
        int cloaked = 0;
        return PInvoke.DwmGetWindowAttribute((HWND)hwnd, DWMWINDOWATTRIBUTE.DWMWA_CLOAKED, &cloaked, sizeof(int)).Succeeded && cloaked != 0;
    }
}
