using Microsoft.Win32;

namespace TrayApp.Shell;

/// <summary>HKCU Run value &lt;App&gt; = "&lt;exe&gt;" --background. Written only by the user's toggle, or fixed at start when Install.IsInstalled.</summary>
public static class Autostart
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string ApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";

    static string Command => $"\"{AppInfo.ExePath}\" --background";
    static string? Value => Registry.CurrentUser.OpenSubKey(RunKey)?.GetValue(AppInfo.Name) as string;

    public static bool IsEnabled => Value is not null;

    /// <summary>
    /// Switched off in Task Manager: settings show it and do not override the user's choice.
    /// Spec says "first byte != 0x02"; 0x06 also means enabled in the wild, so an odd first byte = disabled.
    /// </summary>
    public static bool IsDisabledByUser =>
        Registry.CurrentUser.OpenSubKey(ApprovedKey)?.GetValue(AppInfo.Name) is byte[] { Length: > 0 } b && (b[0] & 1) == 1;

    public static void Set(bool on)
    {
        if (Args.Current.Demo) return;
        using var k = Registry.CurrentUser.CreateSubKey(RunKey);
        if (on) k.SetValue(AppInfo.Name, Command);
        else k.DeleteValue(AppInfo.Name, throwOnMissingValue: false);
    }

    /// <summary>The exe moved: point Run at the current path. Call only when Install.IsInstalled.</summary>
    public static void FixPath()
    {
        if (IsEnabled && Value != Command) Set(true);
    }
}
