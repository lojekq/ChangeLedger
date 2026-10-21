using System.IO;
using Windows.Win32;
using Windows.Win32.UI.Shell;

namespace TrayApp.Shell;

public static unsafe class Paths
{
    /// <summary>
    /// path is root itself or inside it. Both go through GetFullPath (so ".." is resolved), \\?\ is dropped,
    /// and the compare is OrdinalIgnoreCase with a trailing \, so C:\dev2 is not under C:\dev.
    /// </summary>
    public static bool IsUnder(string root, string path) =>
        WithSlash(path).StartsWith(WithSlash(root), StringComparison.OrdinalIgnoreCase);

    /// <summary>The real Downloads folder (FOLDERID_Downloads): the user may have moved it, and Environment.SpecialFolder has no entry for it.</summary>
    public static string Downloads
    {
        get
        {
            PInvoke.SHGetKnownFolderPath(PInvoke.FOLDERID_Downloads, KNOWN_FOLDER_FLAG.KF_FLAG_DEFAULT, null, out var p).ThrowOnFailure();
            try { return p.ToString(); }
            finally { PInvoke.CoTaskMemFree(p.Value); }
        }
    }

    /// <summary>Symlink, junction or any other reparse point (OneDrive placeholders too): don't follow or touch it. Throws if path doesn't exist.</summary>
    public static bool IsReparsePoint(string path) => File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint);

    static string WithSlash(string p)
    {
        if (p.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase)) p = @"\\" + p[8..];
        else if (p.StartsWith(@"\\?\")) p = p[4..];
        p = Path.GetFullPath(p);
        return Path.EndsInDirectorySeparator(p) ? p : p + '\\';
    }
}
