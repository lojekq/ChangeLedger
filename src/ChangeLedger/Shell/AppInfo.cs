using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;

namespace ChangeLedger.Shell;

public static class AppInfo
{
    public const string Name = "ChangeLedger";                 // repo, exe, namespace, data folder, mutex, Run value
    public const string DisplayName = "Change Ledger";          // for people, with spaces: "VRAM Bouncer"
    public const string Repo = "lojekq/ChangeLedger";  // tools/rename.ps1 makes it lojekq/<App>

    /// <summary>CHANGELEDGER_DATA_DIR (or --data-dir, which Program.Main turns into it): a Release build checked apart from the installed copy.</summary>
    public static readonly string DataDirVariable = Name.ToUpperInvariant() + "_DATA_DIR";

#if DEBUG
    const bool DebugBuild = true;
#else
    const bool DebugBuild = false;
#endif

    /// <summary>InformationalVersion without the +commit tail: "0.1.0", "0.2.0-beta.1", "0.0.0-dev".</summary>
    public static readonly string Version =
        (typeof(AppInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0").Split('+')[0];

    static readonly (string Suffix, string Data, string Local) Where = Resolve(DebugBuild, Environment.GetEnvironmentVariable(DataDirVariable),
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));

    /// <summary>
    /// "" for the real (Release) app; "-dev" for Debug builds and tests; "-&lt;hash&gt;" with CHANGELEDGER_DATA_DIR.
    /// A dev instance never meets the installed copy: own data folders, mutex, channels, pipes and AUMID (InstanceName);
    /// it writes no Run value, shortcut or Apps entry and shows no installer (00-shared §9).
    /// </summary>
    public static readonly string Suffix = Where.Suffix;
    public static bool IsDev => Suffix != "";

    /// <summary>Name + Suffix: for the mutex, named events, pipes and the toast AUMID, so dev and installed copies never talk.</summary>
    public static readonly string InstanceName = Name + Suffix;

    /// <summary>For window titles and the tray tooltip: "VRAM Bouncer (dev)" marks a dev instance.</summary>
    public static readonly string Title = DisplayName + (IsDev ? " (dev)" : "");

    public static readonly string DataDir = Where.Data;
    public static readonly string LocalDir = Where.Local;

    /// <summary>Single-file builds have no Assembly.Location, so the exe path comes from here.</summary>
    public static string ExePath => Environment.ProcessPath!;

    public static readonly bool IsElevated = new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);

    internal static (string Suffix, string Data, string Local) Resolve(bool debug, string? dataDir, string appData, string localAppData)
    {
        if (!string.IsNullOrWhiteSpace(dataDir))
        {
            var full = Path.GetFullPath(dataDir).TrimEnd('\\');
            var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(full.ToUpperInvariant())))[..8];
            return ("-" + hash, full, Path.Combine(full, "local"));
        }
        var suffix = debug ? "-dev" : "";
        return (suffix, Path.Combine(appData, Name + suffix), Path.Combine(localAppData, Name + suffix));
    }
}
