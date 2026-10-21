using System.IO;

namespace ChangeLedger.Shell;

/// <summary>
/// "Installed" = this process is %LOCALAPPDATA%\Programs\&lt;App&gt;\&lt;App&gt;.exe, where Installer puts it.
/// Only then do the shell and the app write or repair the exe path on their own (Run, Start menu, task, Explorer menu; 00-shared §2.4),
/// so Debug builds in bin\, exes in Downloads and the PingWhen-dev copy never land in autostart.
/// </summary>
public static class Install
{
    public static readonly string Dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", AppInfo.Name);
    public static readonly string Exe = Path.Combine(Dir, AppInfo.Name + ".exe");

    public static bool IsInstalled => IsAt(AppInfo.ExePath);

    /// <summary>The installed exe, compared as Windows does: full path, any case.</summary>
    public static bool IsAt(string exePath) => string.Equals(Path.GetFullPath(exePath), Exe, StringComparison.OrdinalIgnoreCase);
}
