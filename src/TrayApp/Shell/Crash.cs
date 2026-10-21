using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using Microsoft.Win32;

namespace TrayApp.Shell;

/// <summary>
/// Unhandled exception → %APPDATA%\&lt;App&gt;\crash\crash-yyyyMMdd-HHmmss.txt, App.Exiting (best effort),
/// a "crashed, log is here" box and exit. Never keep running in an unknown state; nothing is sent anywhere.
/// </summary>
public static class Crash
{
    static int crashed;

    public static void Install(Application app)
    {
        app.DispatcherUnhandledException += (_, e) => { e.Handled = true; Report(e.Exception); };
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Report(e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) => Report(e.Exception);
    }

    public static void Report(Exception? ex)
    {
        if (Interlocked.Exchange(ref crashed, 1) == 1) return;
        var dir = Path.Combine(AppInfo.DataDir, "crash");
        var file = Path.Combine(dir, $"crash-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
        try
        {
            Directory.CreateDirectory(dir);
            var ubr = Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion", "UBR", null);
            File.WriteAllText(file,
                $"{AppInfo.Name} {AppInfo.Version}\r\n{RuntimeInformation.OSDescription}.{ubr} {RuntimeInformation.OSArchitecture}\r\n" +
                $".NET {Environment.Version}\r\n{DateTime.Now:yyyy-MM-dd HH:mm:ss}\r\n\r\n{ex}\r\n");
        }
        catch { file = dir; }
        Log.Error("crash", ex);
        try { App.RaiseExiting(); } catch { }

        // Scripted runs (shots, gif) must not hang on a dialog
        if (Args.Current.Screenshot is null && !Args.Current.Autoplay &&
            MessageBox.Show(string.Format(Loc.T("Crash.Message"), AppInfo.DisplayName, file), AppInfo.DisplayName,
                MessageBoxButton.YesNo, MessageBoxImage.Error) == MessageBoxResult.Yes)
            Process.Start("explorer.exe", $"/select,\"{file}\"");
        Environment.Exit(1);
    }
}
