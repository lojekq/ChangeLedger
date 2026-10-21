using Windows.Win32;

namespace TrayApp.Shell;

/// <summary>
/// SHQueryUserNotificationState: is a fullscreen game, a fullscreen app or presentation mode on?
/// Windows' own "Do not disturb" isn't reported here (not verified); balloons are muted by Windows itself then.
/// </summary>
public static class UserBusy
{
    public enum State { NotPresent = 1, Busy, D3DFullScreen, Presentation, AcceptsNotifications, QuietTime, App }

    /// <summary>NotPresent = locked, screen saver or another user; QuietTime = the first hour after Windows setup.</summary>
    public static State Current =>
        PInvoke.SHQueryUserNotificationState(out var s).Succeeded ? (State)s : State.AcceptsNotifications;

    /// <summary>Fullscreen app or game, presentation mode or quiet time: don't pop up or start heavy work.</summary>
    public static bool Now => Current is State.Busy or State.D3DFullScreen or State.Presentation or State.QuietTime or State.App;
}
