using System.IO;

namespace TrayApp.Shell;

/// <summary>
/// "Installed" = the exe lives in %LOCALAPPDATA%\Programs\&lt;App&gt;\, where README asks to put it.
/// Only then do the shell and the app write or repair the exe path on their own (Run, Start menu, task, Explorer menu; 00-shared §2.4),
/// so Debug builds in bin\, exes in Downloads and the PingWhen-dev copy never land in autostart.
/// </summary>
public static class Install
{
    public static readonly string Dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", AppInfo.Name);

    public static bool IsInstalled => Paths.IsUnder(Dir, AppInfo.ExePath);
}
