using System.Diagnostics;
using System.Windows;
using TrayApp.Demo;
using TrayApp.Features;
using TrayApp.Shell;

namespace TrayApp;

public partial class App : Application
{
    /// <summary>Synchronous hook before exit: tray Exit, Windows session end, crash handler (best effort, before the error box).</summary>
    public static event Action? Exiting;

    /// <summary>Asked on a user exit (tray, restart); return false to stay. May ask the user.</summary>
    public static Func<bool>? ConfirmExit;

    /// <summary>"Remove data &amp; autostart": undo what the app registered and return null, or return why not (shown to the user).</summary>
    public static Func<string?>? RemoveData;

    static MainWindow? window;
    static bool quitting;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var a = Args.Current;
        Crash.Install(this);
        if (!SingleInstance.Acquire(() => Dispatcher.BeginInvoke(() => ShowMain(null))))
        {
            Shutdown();
            return;
        }
        Settings.Load();
        Loc.Apply(Settings.Current.Language);
        Log.Info($"start {AppInfo.Version}{(a.Unknown.Count > 0 ? $", {a.Unknown.Count} unknown argument(s)" : "")}");

        if (a.Demo) DemoData.Fill();
        else if (Install.IsInstalled) InstalledStart(); // Debug builds and exes in Downloads write nothing by themselves

        // App features: services, hotkeys, tray items, settings section (template sample below)
        SettingsPage.AppSection = SampleSettings.Create;
        SampleSettings.Apply();

        window = new MainWindow();
        if (a.Width > 0) (window.Width, window.Height) = (a.Width, a.Height);
        Theme.Apply(window, Settings.Current.Theme);
        window.Closing += (_, ce) =>
        {
            if (quitting) return;
            ce.Cancel = true; // the cross hides to the tray
            window.Hide();
            if (Settings.Current.TrayHintShown) return;
            Settings.Current.TrayHintShown = true;
            Settings.Save();
            Tray.Notify(AppInfo.DisplayName, Loc.T("Tray.Hidden"));
        };
        Capture.Attach(window, () => { if (a.Autoplay) Autoplay.Start(window); });

        if (a.Screenshot is null) Tray.Create(ShowMain, () => Quit());
        SessionEnding += (_, _) => RaiseExiting();
        Updates.Found += (version, url) =>
        {
            window.ShowUpdate(version, url);
            if (Settings.Current.NotifiedVersion == version) return;
            Settings.Current.NotifiedVersion = version;
            Settings.Save();
            Tray.Notify(AppInfo.DisplayName, string.Format(Loc.T("Update.Message"), version), "about");
        };
        Updates.Start();

        if (!a.Background) ShowMain(a.Page);
    }

    public static void ShowMain(string? page)
    {
        if (window is null) return;
        window.Show();
        if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
        window.Activate();
        if (page is not null) window.Navigate(page);
    }

    /// <summary>The exe is in %LOCALAPPDATA%\Programs\&lt;App&gt;\: repair paths if it moved, first start turns the Start menu shortcut on.</summary>
    static void InstalledStart()
    {
        try
        {
            Autostart.FixPath();
            StartMenu.FixPath();
            if (Settings.Current.FirstRunDone) return;
            StartMenu.Set(true);
            Settings.Current.FirstRunDone = true;
            Settings.Save();
        }
        catch (Exception e) { Log.Error("installed start", e); }
    }

    public static void RaiseExiting()
    {
        var hook = Exiting;
        Exiting = null; // once, whoever comes first
        foreach (var h in hook?.GetInvocationList() ?? [])
            try { ((Action)h)(); }
            catch (Exception e) { Log.Error("Exiting hook failed", e); } // the other hooks still run
    }

    public static void Quit(bool confirm = true)
    {
        if (confirm && ConfirmExit?.Invoke() == false) return;
        quitting = true;
        RaiseExiting();
        Tray.Dispose();
        Log.Info("exit");
        Current.Shutdown();
    }

    /// <summary>After a language change. The mutex is released first, so the new process does not just wake this one.</summary>
    public static void Restart()
    {
        if (ConfirmExit?.Invoke() == false) return;
        SingleInstance.Release();
        Process.Start(AppInfo.ExePath);
        Quit(confirm: false);
    }
}
