using System.Diagnostics;
using System.IO;
using System.Windows;
using ChangeLedger.Demo;
using ChangeLedger.Shell;

namespace ChangeLedger.Features;

/// <summary>Change Ledger's part of Settings (SettingsPage.AppSection): daily snapshot, the scheduler task, retention, secrets, data.</summary>
public partial class SettingsSection
{
    static bool Demo => Args.Current.Demo;

    public SettingsSection()
    {
        InitializeComponent();
        var s = Settings.Current;
        for (var m = 0; m < 24 * 60; m += 30) TimeBox.Items.Add($"{m / 60:00}:{m % 60:00}");
        if (!TimeBox.Items.Contains(s.SnapshotTime)) TimeBox.Items.Add(s.SnapshotTime);
        TimeBox.SelectedItem = s.SnapshotTime;
        DailySwitch.IsChecked = s.DailySnapshot;
        DaysBox.Value = s.RetentionDays;
        MbBox.Value = s.MaxStorageMb;
        SecretsBox.Text = string.Join(", ", s.ExtraSecretNames);
        IgnoreBox.Text = string.Join(Environment.NewLine, s.IgnoreRules);
        NotifySwitch.IsChecked = s.NotifyUnexpected;
        Refresh();

        IgnoreBox.LostFocus += (_, _) =>
        {
            s.IgnoreRules = [.. (IgnoreBox.Text ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
            Settings.Save();
            DiffEngine.SetUserRules(s.IgnoreRules);
        };
        NotifySwitch.Click += (_, _) => { s.NotifyUnexpected = NotifySwitch.IsChecked == true; Settings.Save(); };

        TimeBox.SelectionChanged += (_, _) =>
        {
            s.SnapshotTime = (string)TimeBox.SelectedItem;
            Settings.Save();
            if (!Demo) Try(TaskRegistration.Reschedule);
            Refresh();
        };
        DailySwitch.Click += (_, _) =>
        {
            s.DailySnapshot = DailySwitch.IsChecked == true;
            Settings.Save();
            if (!Demo)
            {
                if (!s.DailySnapshot) Try(TaskRegistration.Remove);
                else if (Install.IsInstalled) Try(TaskRegistration.Repair); // a Debug build or a copy in Downloads only by the button
            }
            Refresh();
        };
        RepairButton.Click += (_, _) =>
        {
            if (Demo) return;
            s.DailySnapshot = true;
            DailySwitch.IsChecked = true;
            Settings.Save();
            Try(TaskRegistration.Repair);
            Refresh();
        };
        DaysBox.ValueChanged += (_, _) => { s.RetentionDays = (int)Math.Clamp(DaysBox.Value ?? 365, 7, 3650); Settings.Save(); };
        MbBox.ValueChanged += (_, _) => { s.MaxStorageMb = (int)Math.Clamp(MbBox.Value ?? 500, 50, 100000); Settings.Save(); };
        SecretsBox.LostFocus += (_, _) =>
        {
            s.ExtraSecretNames = [.. (SecretsBox.Text ?? "").Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
            Settings.Save();
        };
        SnapshotButton.Click += (_, _) => TrayMenu.SnapshotNow();
        FolderButton.Click += (_, _) =>
        {
            if (Demo) return;
            Directory.CreateDirectory(AppInfo.LocalDir);
            Process.Start("explorer.exe", $"\"{AppInfo.LocalDir}\"");
        };
        RebuildButton.Click += async (_, _) =>
        {
            if (Demo) return;
            RebuildButton.IsEnabled = false;
            var done = await Task.Run(() => Runner.Locked(SnapshotStore.Rebuild));
            RebuildButton.IsEnabled = true;
            if (done is null) MessageBox.Show(Loc.T("Snap.Busy"), AppInfo.DisplayName);
            Ledger.Refresh(force: true);
        };
        DeleteButton.Click += (_, _) => DeleteAll();

        Ledger.Changed += Refresh;
        Ledger.BusyChanged += Refresh;
        if (Args.Current.Screenshot is not null) Loaded += (_, _) => BringIntoView(); // the README shot shows this section, not the shell's
        Unloaded += (_, _) => { Ledger.Changed -= Refresh; Ledger.BusyChanged -= Refresh; };
    }

    void Refresh()
    {
        var s = Settings.Current;
        var (bytes, count) = Demo ? DemoData.Usage : SnapshotStore.Usage();
        UsageText.Text = string.Format(Loc.T("Set.Usage"), Math.Max(1, bytes / 1024 / 1024), count, Demo ? @"C:\Users\demo\AppData\Local\ChangeLedger" : AppInfo.LocalDir);
        var info = Demo ? new TaskRegistration.Info(TaskState.Ok) : TaskRegistration.Status();
        var text = info.State switch
        {
            TaskState.Ok => string.Format(Loc.T("Task.Ok"), s.SnapshotTime),
            TaskState.Missing => Loc.T("Task.Missing"),
            TaskState.StalePath => Loc.T("Task.Stale"),
            TaskState.Off => Loc.T("Task.Off"),
            _ => Loc.T("Task.Error"),
        };
        if (info.LastRun is { } last) text += " · " + string.Format(Loc.T("Task.LastRun"), Words.When(last.ToUniversalTime()), $"0x{info.LastResult:X}");
        if (!Demo && !Install.IsInstalled) text += "\n" + string.Format(Loc.T("Task.NotInstalled"), Install.Dir);
        TaskStatus.Text = text;
        SnapshotButton.IsEnabled = !Ledger.Busy && !AppInfo.IsElevated;
    }

    static void Try(Action a)
    {
        try { a(); }
        catch (Exception e)
        {
            Log.Error("scheduler task", e);
            MessageBox.Show(string.Format(Loc.T("Task.Failed"), e.Message), AppInfo.DisplayName, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>Confirmation with size and count, then the task and %LOCALAPPDATA%\ChangeLedger go; the daily snapshot is turned off so nothing comes back.</summary>
    void DeleteAll()
    {
        if (Demo) return;
        var (bytes, count) = SnapshotStore.Usage();
        if (MessageBox.Show(string.Format(Loc.T("Set.Delete.Confirm"), count, Math.Max(1, bytes / 1024 / 1024), AppInfo.LocalDir), AppInfo.DisplayName,
                MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK) return;
        Try(TaskRegistration.Remove);
        Settings.Current.DailySnapshot = false;
        DailySwitch.IsChecked = false;
        Settings.Save();
        var deleted = Runner.Locked(() =>
        {
            try { if (Directory.Exists(AppInfo.LocalDir)) Directory.Delete(AppInfo.LocalDir, recursive: true); return "ok"; }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Log.Warn("data folder not deleted", e); return e.Message; }
        });
        if (deleted != "ok") MessageBox.Show(deleted ?? Loc.T("Snap.Busy"), AppInfo.DisplayName);
        Log.Info("task and data deleted");
        Ledger.Refresh(force: true);
    }
}
