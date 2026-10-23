using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Wpf.Ui.Controls;
using MessageBox = Wpf.Ui.Controls.MessageBox;
using MessageBoxResult = Wpf.Ui.Controls.MessageBoxResult;
using TextBlock = System.Windows.Controls.TextBlock;

namespace ChangeLedger.Shell;

/// <summary>
/// One exe that installs itself, no admin rights. Started from anywhere but Install.Exe it offers "Install" (or "Update X → Y"),
/// closes the running copy, copies itself to %LOCALAPPDATA%\Programs\&lt;App&gt;\&lt;App&gt;.exe, starts that copy and exits.
/// The installed copy registers itself in Settings → Apps (HKCU Uninstall) on every start. --uninstall removes it all.
/// Off for a dev instance (Debug, CHANGELEDGER_DATA_DIR), under --demo and for non-single-file builds (they need their DLLs next to them).
/// </summary>
public static class Installer
{
    const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\" + AppInfo.Name;

#pragma warning disable IL3000 // empty Location is exactly the single-file test
    static readonly bool SingleFile = string.IsNullOrEmpty(typeof(Installer).Assembly.Location);
#pragma warning restore IL3000

    public static bool Available => !AppInfo.IsDev && SingleFile && !Args.Current.Demo && Args.Current.Screenshot is null && !Args.Current.Autoplay;

    public enum Plan { Run, Install, Update, ShowInstalled }

    /// <summary>What a start from <paramref name="self"/> does. <paramref name="installedVersion"/> is null when nothing is installed.</summary>
    public static Plan Decide(string self, string selfVersion, string? installedVersion, bool skipped)
    {
        if (Install.IsAt(self) || skipped) return Plan.Run;
        if (installedVersion is null) return Plan.Install;
        return Updates.IsNewer(selfVersion, installedVersion) ? Plan.Update : Plan.ShowInstalled;
    }

    /// <summary>Call before the single-instance check, after Settings and Loc. True: this process is done and exits.</summary>
    public static async Task<bool> HandleAsync()
    {
        var a = Args.Current;
        if (a.Uninstall) // a dev instance must never remove the real installation
        {
            if (!AppInfo.IsDev) await UninstallAsync();
            return true;
        }
        if (!Available || Install.IsInstalled) return false;

        var self = AppInfo.ExePath;
        var installed = File.Exists(Install.Exe) ? (FileVersionInfo.GetVersionInfo(Install.Exe).ProductVersion ?? "0.0.0").Split('+')[0] : null;
        var skipped = !a.Install && Settings.Current.NoInstall.Contains(self, StringComparer.OrdinalIgnoreCase);
        var plan = Decide(self, AppInfo.Version, installed, skipped);
        if (plan == Plan.Run) return false;
        if (plan == Plan.ShowInstalled) // same or newer is installed: that one opens (or a running one shows itself)
        {
            Process.Start(Install.Exe);
            return true;
        }

        if (!a.Install)
        {
            Theme.ApplyColors(Settings.Current.Theme);
            var update = plan == Plan.Update;
            var text = update ? string.Format(Loc.T("Install.Update"), AppInfo.DisplayName, installed, AppInfo.Version)
                              : string.Format(Loc.T("Install.Ask"), AppInfo.DisplayName, Install.Dir);
            if (await Ask(text, Loc.T(update ? "Install.UpdateButton" : "Install.Button"), Loc.T("Install.NoInstall")) != MessageBoxResult.Primary)
            {
                Settings.Current.NoInstall.Add(self); // not asked again for this exe; Settings → Install is still there
                Settings.Save();
                return false;
            }
        }
        if (!await CloseRunningAsync()) return true;
        Copy();
        Log.Info(installed is null ? $"installed {AppInfo.Version}" : $"updated {installed} → {AppInfo.Version}");
        var start = new ProcessStartInfo(Install.Exe);
        if (installed is null) start.ArgumentList.Add("--installed");
        Process.Start(start);
        return true;
    }

    /// <summary>
    /// Start of the installed exe: refresh the Apps entry (the version after an update), re-point Run and the shortcut,
    /// and on a fresh install turn on the shortcut and, if <paramref name="autostart"/>, autostart.
    /// </summary>
    public static void InstalledStart(bool autostart)
    {
        try
        {
            foreach (var old in Directory.EnumerateFiles(Install.Dir, $"{AppInfo.Name}.exe.old-*"))
                try { File.Delete(old); } catch { } // still held by a process from before the update: next time
            Register();
            Autostart.FixPath();
            StartMenu.FixPath();
            if (Settings.Current.FirstRunDone && !Args.Current.Installed) return;
            StartMenu.Set(true);
            Autostart.Set(autostart);
            Settings.Current.FirstRunDone = true;
            Settings.Save();
        }
        catch (Exception e) { Log.Error("installed start", e); }
    }

    static void Register()
    {
        using var k = Registry.CurrentUser.CreateSubKey(UninstallKey);
        k.SetValue("DisplayName", AppInfo.DisplayName);
        k.SetValue("DisplayVersion", AppInfo.Version);
        k.SetValue("Publisher", "lojekq");
        k.SetValue("InstallLocation", Install.Dir);
        k.SetValue("DisplayIcon", Install.Exe);
        k.SetValue("UninstallString", $"\"{Install.Exe}\" --uninstall");
        k.SetValue("URLInfoAbout", $"https://github.com/{AppInfo.Repo}");
        k.SetValue("EstimatedSize", (int)(new FileInfo(Install.Exe).Length / 1024), RegistryValueKind.DWord);
        k.SetValue("NoModify", 1, RegistryValueKind.DWord);
        k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
        if (k.GetValue("InstallDate") is null) k.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd"));
    }

    /// <summary>
    /// The old exe moves aside first: a running exe (a PingWhen --mcp relay) can be renamed but not overwritten.
    /// If the copy fails, the old exe comes back: never leave the user without a working program.
    /// </summary>
    static void Copy()
    {
        Directory.CreateDirectory(Install.Dir);
        string? old = null;
        if (File.Exists(Install.Exe)) File.Move(Install.Exe, old = $"{Install.Exe}.old-{DateTime.Now:yyyyMMddHHmmss}");
        try { File.Copy(AppInfo.ExePath, Install.Exe); }
        catch
        {
            if (old is not null) File.Move(old, Install.Exe, overwrite: true);
            throw;
        }
        if (old is not null) try { File.Delete(old); } catch { }
    }

    /// <summary>
    /// The running copy (installed or not) holds the mutex: ask it to exit through the single-instance channel and wait.
    /// Versions before that channel do not listen; then the user closes it from the tray. False: the user gave up.
    /// </summary>
    static async Task<bool> CloseRunningAsync()
    {
        SingleInstance.SignalExit();
        while (!await Task.Run(() => SingleInstance.WaitFree(TimeSpan.FromSeconds(10))))
        {
            Theme.ApplyColors(Settings.Current.Theme);
            if (await Ask(string.Format(Loc.T("Install.CloseRunning"), AppInfo.DisplayName), Loc.T("Install.Retry"), Loc.T("Install.Cancel")) != MessageBoxResult.Primary)
                return false;
        }
        return true;
    }

    static async Task UninstallAsync()
    {
        // The program folder cannot be deleted while this exe runs from it: go on from a copy in %TEMP% (it stays there)
        if (Install.IsInstalled)
        {
            var tmp = Path.Combine(Path.GetTempPath(), $"{AppInfo.Name}-uninstall-{Environment.ProcessId}.exe");
            File.Copy(AppInfo.ExePath, tmp, overwrite: true);
            Process.Start(new ProcessStartInfo(tmp) { ArgumentList = { "--uninstall" } });
            return;
        }
        Theme.ApplyColors(Settings.Current.Theme);
        var data = new CheckBox { Content = Loc.T("Uninstall.Data"), Margin = new Thickness(0, 12, 0, 0) };
        var content = new StackPanel { Children = { Text(string.Format(Loc.T("Uninstall.Ask"), AppInfo.DisplayName)), data } };
        if (await Ask(content, Loc.T("Uninstall.Button"), Loc.T("Install.Cancel")) != MessageBoxResult.Primary) return;
        if (!await CloseRunningAsync()) return;
        if (App.RemoveData?.Invoke() is { } reason) // the app unregisters its own things or refuses (Reclaimer: quarantine not empty)
        {
            await Ask(reason, "", "OK");
            return;
        }
        Autostart.Set(false);
        StartMenu.Set(false);
        Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, throwOnMissingSubKey: false);
        if (data.IsChecked == true)
        {
            Log.Off = true;
            foreach (var dir in new[] { AppInfo.DataDir, AppInfo.LocalDir })
                try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); } catch { }
        }
        // A copy that has just exited may hold its exe for a moment more; a PingWhen --mcp relay holds it until its agent ends
        for (var attempt = 1; Directory.Exists(Install.Dir); attempt++)
        {
            try { Directory.Delete(Install.Dir, recursive: true); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                if (attempt == 20)
                {
                    await Ask(string.Format(Loc.T("Uninstall.Leftover"), Install.Dir), "", "OK");
                    return;
                }
                await Task.Delay(500);
            }
        }
        await Ask(string.Format(Loc.T("Uninstall.Done"), AppInfo.DisplayName), "", "OK");
    }

    static TextBlock Text(string s) => new() { Text = s, TextWrapping = TextWrapping.Wrap, MaxWidth = 440 };

    /// <summary>A Fluent dialog before any window exists. An empty <paramref name="primary"/> hides that button.</summary>
    static Task<MessageBoxResult> Ask(object content, string primary, string close) =>
        new MessageBox
        {
            Title = AppInfo.DisplayName,
            Content = content is string s ? Text(s) : content,
            PrimaryButtonText = primary,
            IsPrimaryButtonEnabled = primary != "",
            CloseButtonText = close,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
        }.ShowDialogAsync();
}
