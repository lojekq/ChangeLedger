using System.IO;
using System.Runtime.InteropServices;

namespace TrayApp.Shell;

/// <summary>Start menu shortcut, replaces an installer (00-shared §3.2). COM WScript.Shell, no extra packages.</summary>
public static class StartMenu
{
    static string LinkPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), AppInfo.DisplayName + ".lnk");

    public static bool Exists => File.Exists(LinkPath);

    public static void Set(bool on)
    {
        if (Args.Current.Demo) return;
        if (!on) { File.Delete(LinkPath); return; }
        dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!;
        try
        {
            var lnk = shell.CreateShortcut(LinkPath);
            lnk.TargetPath = AppInfo.ExePath;
            lnk.WorkingDirectory = Path.GetDirectoryName(AppInfo.ExePath);
            lnk.Description = AppInfo.DisplayName;
            lnk.Save();
        }
        finally { Marshal.FinalReleaseComObject(shell); }
    }

    /// <summary>The exe moved: re-point the shortcut. Call only when Install.IsInstalled.</summary>
    public static void FixPath()
    {
        if (!Exists) return;
        dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!;
        try
        {
            if (!string.Equals((string)shell.CreateShortcut(LinkPath).TargetPath, AppInfo.ExePath, StringComparison.OrdinalIgnoreCase)) Set(true);
        }
        finally { Marshal.FinalReleaseComObject(shell); }
    }
}
