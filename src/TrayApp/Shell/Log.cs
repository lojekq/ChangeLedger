using System.IO;
using System.Text.RegularExpressions;

namespace TrayApp.Shell;

/// <summary>
/// %APPDATA%\&lt;App&gt;\logs\&lt;App&gt;-yyyyMMdd[-verb].log, the newest 14 files of each kind are kept.
/// Never log user data (window titles, paths, command lines, URLs): logs get attached to public issues.
/// </summary>
// ponytail: the file is opened for every line; fine up to ~100 lines/min
public static partial class Log
{
    static readonly Lock Gate = new();
    static bool cleaned;

    /// <summary>Set by Program.Main for verb processes, so two processes never share a file.</summary>
    public static string? Verb { get; set; }
    public static string Dir => Path.Combine(AppInfo.DataDir, "logs");

    /// <summary>Set after "Remove data": no write may recreate the data folder.</summary>
    public static bool Off { get; set; }

    public static void Info(string message) => Write("INFO ", message, null);
    public static void Warn(string message, Exception? e = null) => Write("WARN ", message, e);
    public static void Error(string message, Exception? e = null) => Write("ERROR", message, e);

    static void Write(string level, string message, Exception? e)
    {
        if (Off || Args.Current.Demo) return; // --demo writes no data files
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Dir);
                var file = Path.Combine(Dir, $"{AppInfo.Name}-{DateTime.Now:yyyyMMdd}{(Verb is null ? "" : "-" + Verb)}.log");
                using (var w = new StreamWriter(new FileStream(file, FileMode.Append, FileAccess.Write, FileShare.ReadWrite)))
                    w.WriteLine($"{DateTime.Now:HH:mm:ss.fff} {level} {message}{(e is null ? "" : Environment.NewLine + e)}");
                if (!cleaned) { cleaned = true; Cleanup(); }
            }
        }
        catch { } // logging never takes the app down
    }

    static void Cleanup()
    {
        foreach (var kind in new DirectoryInfo(Dir).GetFiles("*.log").GroupBy(f => DatePart().Replace(f.Name, "")))
            foreach (var old in kind.OrderByDescending(f => f.Name).Skip(14))
                try { old.Delete(); } catch { }
    }

    [GeneratedRegex(@"-\d{8}")] private static partial Regex DatePart();
}
