using System.Diagnostics;
using System.IO;
using System.Text;
using Windows.Win32;

namespace TrayApp.Shell;

/// <summary>
/// The only way to run a console tool (git, wsl, powercfg, user commands): no console window ever flashes (00-shared §2.4).
/// </summary>
public static class ConsoleTool
{
    static ConsoleTool() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance); // OEM code pages like 866

    /// <summary>
    /// Runs exe with no window and returns its exit code and stdout; stderr is drained and dropped, stdin is closed at once.
    /// encoding: null = the system OEM code page that classic tools (powercfg, schtasks) write in;
    /// wsl.exe needs Encoding.Unicode, git and most newer tools Encoding.UTF8.
    /// Throws TimeoutException after killing the whole process tree, Win32Exception when exe can't be started.
    /// </summary>
    public static (int ExitCode, string Stdout) Run(string exe, IEnumerable<string> args, TimeSpan timeout, Encoding? encoding = null)
    {
        encoding ??= Encoding.GetEncoding((int)PInvoke.GetOEMCP());
        var deadline = Stopwatch.StartNew();
        using var p = Process.Start(new ProcessStartInfo(exe, args)
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = encoding,
            StandardErrorEncoding = encoding,
        })!;
        p.StandardInput.Close(); // a tool that asks something gets EOF instead of hanging until the timeout
        // Read both pipes asynchronously: a full pipe buffer blocks the child, and a blocked child never exits
        var stdout = p.StandardOutput.ReadToEndAsync();
        var stderr = p.StandardError.ReadToEndAsync();
        if (!p.WaitForExit(timeout) || !Task.WaitAll([stdout, stderr], Max(timeout - deadline.Elapsed, TimeSpan.Zero)))
        {
            try { p.Kill(entireProcessTree: true); } catch { } // a grandchild holding the pipes after the child exited stays
            throw new TimeoutException($"{Path.GetFileName(exe)} did not finish in {timeout.TotalSeconds:0} s");
        }
        return (p.ExitCode, stdout.Result);
    }

    static TimeSpan Max(TimeSpan a, TimeSpan b) => a > b ? a : b;
}
