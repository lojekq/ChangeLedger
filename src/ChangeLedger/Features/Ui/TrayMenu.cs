using System.Reflection;
using System.Windows.Controls;
using ChangeLedger.Shell;
using H.NotifyIcon;

namespace ChangeLedger.Features;

/// <summary>Tray items between the shell's Open and Exit, the tooltip "Change Ledger · snapshot 21 Oct 13:00 · changes: 3", "Snapshot now".</summary>
public static class TrayMenu
{
    public static List<Control> Items()
    {
        var items = new List<Control>
        {
            Tray.Item(Loc.T("Tray.Snapshot"), SnapshotNow),
            Tray.Item(Loc.T("Tray.Day"), () => Changes(1)),
            Tray.Item(Loc.T("Tray.Week"), () => Changes(7)),
            Tray.Item(Loc.T("Tray.Why"), () => App.ShowMain("power")),
            Tray.Item(Loc.T("Tray.Settings"), () => App.ShowMain("settings")),
        };
        Ledger.BusyChanged += () => items[0].IsEnabled = !Ledger.Busy;
        return items;
    }

    /// <summary>After Tray.Create: the index is re-read when the menu opens (no timers, spec §5).</summary>
    public static void Attach(List<Control> items)
    {
        if (items[0].Parent is ContextMenu menu) menu.Opened += (_, _) => Ledger.Refresh();
        Ledger.Changed += UpdateTip;
        UpdateTip();
    }

    static void Changes(int days)
    {
        ChangesPage.Request(days);
        App.ShowMain("changes");
    }

    public static void SnapshotNow()
    {
        if (AppInfo.IsElevated)
        {
            Tray.Notify(AppInfo.DisplayName, Loc.T("Elevated.Message"));
            return;
        }
        Ledger.SnapshotNow("manual", r => Tray.Notify(AppInfo.DisplayName, r switch
        {
            RunResult.Written when Ledger.Index.Count == 1 => Loc.T("Snap.First"),
            RunResult.Written => string.Format(Loc.T("Snap.Written"), Ledger.Index.LastOrDefault()?.Total ?? 0),
            RunResult.Unchanged => Loc.T("Snap.Unchanged"),
            RunResult.Busy => Loc.T("Snap.Busy"),
            _ => Loc.T("Snap.Failed"),
        }, "changes"));
    }

    // ponytail: Shell/Tray keeps its TaskbarIcon private and the template is frozen; reflection until it gets a SetToolTip
    static readonly FieldInfo? IconField = typeof(Tray).GetField("icon", BindingFlags.NonPublic | BindingFlags.Static);

    static void UpdateTip()
    {
        if (IconField?.GetValue(null) is not TaskbarIcon icon) return;
        var last = Ledger.State.LastCheckUtc;
        var changes = Ledger.Index.Where(e => e.T > (last ?? DateTime.UtcNow).AddDays(-1)).Sum(e => e.Total);
        icon.ToolTipText = last is null ? AppInfo.Title : string.Format(Loc.T("Tray.Tip"), AppInfo.Title, Words.When(last.Value), changes);
    }
}
