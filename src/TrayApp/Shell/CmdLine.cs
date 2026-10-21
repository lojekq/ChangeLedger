using System.ComponentModel;
using Windows.Win32;
using Windows.Win32.Foundation;

namespace TrayApp.Shell;

/// <summary>
/// Splitting a full command line (exe first, as <see cref="ProcessInfo.CommandLine"/> returns it).
/// The way back is ProcessStartInfo.ArgumentList: it quotes each argument so that Args() gives it back.
/// </summary>
public static unsafe class CmdLine
{
    /// <summary>argv the way the process itself sees it (CommandLineToArgvW). Blank → [].</summary>
    public static string[] Args(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return []; // CommandLineToArgvW("") returns our own exe path
        int n;
        PWSTR* argv;
        fixed (char* p = raw) argv = PInvoke.CommandLineToArgv(p, &n);
        if (argv is null) throw new Win32Exception();
        try
        {
            var result = new string[n];
            for (int i = 0; i < n; i++) result[i] = argv[i].ToString();
            return result;
        }
        finally { PInvoke.LocalFree((HLOCAL)argv); }
    }

    /// <summary>
    /// (exe, the rest untouched) by the argv[0] rule: a quoted exe ends at the next quote, otherwise at the first space or tab.
    /// Relaunch keeps RawArgs as is: ProcessStartInfo(exe) { Arguments = rawArgs }.
    /// </summary>
    public static (string Exe, string RawArgs) SplitExe(string raw)
    {
        var s = raw.TrimStart(' ', '\t');
        bool quoted = s.StartsWith('"');
        int end = quoted ? s.IndexOf('"', 1) : s.IndexOfAny([' ', '\t']);
        if (end < 0) return (quoted ? s[1..] : s, "");
        return (quoted ? s[1..end] : s[..end], s[(quoted ? end + 1 : end)..].TrimStart(' ', '\t'));
    }
}
