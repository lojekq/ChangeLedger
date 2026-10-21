using System.IO;
using System.Reflection;
using System.Security.Principal;

namespace TrayApp.Shell;

public static class AppInfo
{
    public const string Name = "TrayApp";                 // repo, exe, namespace, data folder, mutex, Run value
    public const string DisplayName = "TrayApp";          // for people, with spaces: "VRAM Bouncer"
    public const string Repo = "lojekq/TrayAppTemplate";  // tools/rename.ps1 makes it lojekq/<App>

    /// <summary>InformationalVersion without the +commit tail: "0.1.0", "0.2.0-beta.1", "0.0.0-dev".</summary>
    public static readonly string Version =
        (typeof(AppInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0").Split('+')[0];

    public static readonly string DataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), Name);
    public static readonly string LocalDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Name);

    /// <summary>Single-file builds have no Assembly.Location, so the exe path comes from here.</summary>
    public static string ExePath => Environment.ProcessPath!;

    public static readonly bool IsElevated = new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
}
