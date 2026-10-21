using System.IO;
using Microsoft.Win32;

namespace TrayApp.Shell;

/// <summary>Hive: "HKLM" (64-bit view), "HKLM32" (WOW6432Node) or "HKCU". SubKey of an MSI entry is its ProductCode. InstallDate is raw ("yyyyMMdd" when set).</summary>
public sealed record UninstallEntry(string Hive, string SubKey, string? DisplayName, string? DisplayVersion, string? Publisher,
    string? InstallLocation, string? InstallDate, bool SystemComponent);

/// <summary>All Uninstall keys, unfiltered (updates and components without DisplayName too): filtering is the app's call.</summary>
public static class UninstallEntries
{
    const string Key = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";

    public static List<UninstallEntry> List()
    {
        var list = new List<UninstallEntry>();
        Read("HKLM", RegistryHive.LocalMachine, Key, list);
        Read("HKLM32", RegistryHive.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall", list);
        Read("HKCU", RegistryHive.CurrentUser, Key, list);
        return list;
    }

    static void Read(string hive, RegistryHive root, string path, List<UninstallEntry> list)
    {
        using var baseKey = RegistryKey.OpenBaseKey(root, RegistryView.Registry64);
        using var key = baseKey.OpenSubKey(path);
        if (key is null) return;
        foreach (var name in key.GetSubKeyNames())
        {
            try
            {
                using var k = key.OpenSubKey(name);
                if (k is null) continue;
                list.Add(new(hive, name, Str(k, "DisplayName"), Str(k, "DisplayVersion"), Str(k, "Publisher"),
                    Str(k, "InstallLocation"), Str(k, "InstallDate"), k.GetValue("SystemComponent") is int i && i == 1));
            }
            catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException or IOException) { } // one unreadable key doesn't hide the rest
        }
    }

    static string? Str(RegistryKey k, string name) => k.GetValue(name)?.ToString();
}
