using System.IO;
using System.Windows;
using System.Windows.Threading;
using ChangeLedger.Demo;
using ChangeLedger.Shell;

namespace ChangeLedger.Features;

/// <summary>
/// What the pages read: snapshots, journal events, updates; plus "Snapshot now" and the start-up snapshot.
/// Under --demo everything comes from DemoData and nothing touches the disk, the scheduler or the logs.
/// No timers: the index is re-read when the window is activated and when the tray menu opens (spec §5).
/// </summary>
public static class Ledger
{
    static bool Demo => Args.Current.Demo;

    /// <summary>Raised on the UI thread after the index changed or a snapshot finished.</summary>
    public static event Action? Changed;
    public static event Action? BusyChanged;

    public static List<IndexEntry> Index { get; private set; } = [];
    public static LedgerState State { get; private set; } = new();
    static (long, DateTime) stamp;

    public static void Refresh(bool force = false)
    {
        if (Demo)
        {
            if (Index.Count > 0 && !force) return;
            (Index, State) = (DemoData.Index, DemoData.State);
            Changed?.Invoke();
            return;
        }
        try
        {
            var fi = new FileInfo(SnapshotStore.IndexPath);
            var now = fi.Exists ? (fi.Length, fi.LastWriteTimeUtc) : (0L, default(DateTime));
            State = SnapshotStore.LoadState();
            if (!force && now == stamp) return;
            stamp = now;
            Index = SnapshotStore.ReadIndex();
            updates = null;
        }
        catch (Exception e) { Log.Warn("index refresh", e); }
        Changed?.Invoke();
    }

    static readonly Dictionary<string, Snapshot> Cache = new(StringComparer.Ordinal);

    /// <summary>Thread-safe; keeps the last few snapshots (≈ 0.5 MB each in memory).</summary>
    public static Snapshot Load(string file)
    {
        if (Demo) return DemoData.Snapshots[file];
        lock (Cache) if (Cache.TryGetValue(file, out var s)) return s;
        var snap = SnapshotStore.Read(file);
        lock (Cache)
        {
            if (Cache.Count >= 6) Cache.Clear();
            Cache[file] = snap;
        }
        return snap;
    }

    public static Snapshot? Latest() => Index.Count > 0 ? Load(Index[^1].F) : null;

    /// <summary>Reboots and wakes in [from, to]; boot sessions that started before <paramref name="fromUtc"/> are read too, to explain the first one.</summary>
    public static List<PowerEntry> Power(DateTime fromUtc, DateTime toUtc) =>
        [.. (Demo ? DemoData.Power : RebootClassifier.Classify(EventHistory.Read(fromUtc.AddDays(-2), toUtc))).Where(e => e.Utc >= fromUtc && e.Utc <= toUtc)];

    public static List<DateTime> Boots(DateTime fromUtc, DateTime toUtc) =>
        Demo ? [.. DemoData.Boots.Where(b => b >= fromUtc && b <= toUtc)]
             : [.. EventHistory.Read(fromUtc, toUtc).Where(e => e.Is(EventHistory.KernelGeneral, 12)).Select(e => e.Utc).Order()];

    static List<WuEntry>? updates;

    /// <summary>~0.4 s of COM: call from Task.Run. Cached until the index changes.</summary>
    public static List<WuEntry> Updates()
    {
        if (Demo) return DemoData.Updates;
        try { return updates ??= EventHistory.Updates(); }
        catch (Exception e) { Log.Warn("update history", e); return []; }
    }

    static DateTime? logOldest;
    public static DateTime? LogOldest => Demo ? null : logOldest ??= EventHistory.OldestUtc();

    static CancellationTokenSource? running;
    public static bool Busy => running is not null;

    /// <summary>On a Lowest-priority background thread under the snapshot mutex; <paramref name="done"/> runs on the UI thread.</summary>
    public static void SnapshotNow(string reason, Action<RunResult>? done = null)
    {
        if (Demo || running is not null) return;
        running = new CancellationTokenSource();
        var token = running.Token;
        var ui = Application.Current.Dispatcher;
        new Thread(() =>
        {
            var r = Runner.Run(reason, token);
            ui.BeginInvoke(() =>
            {
                running = null;
                BusyChanged?.Invoke();
                Refresh(force: true);
                done?.Invoke(r);
            });
        }) { IsBackground = true, Priority = ThreadPriority.Lowest, Name = "snapshot" }.Start();
        BusyChanged?.Invoke();
    }

    /// <summary>App exit: the snapshot stops between collectors.</summary>
    public static void Cancel() => running?.Cancel();

    /// <summary>2 min after start, if the last check is older than 1 h: a snapshot "after the update" follows every reboot.</summary>
    public static void StartupSnapshot()
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(2) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            if (AppInfo.IsElevated) return;
            var last = SnapshotStore.LoadState().LastCheckUtc;
            if (last is null || DateTime.UtcNow - last > TimeSpan.FromHours(1)) SnapshotNow("startup");
        };
        timer.Start();
    }

    /// <summary>Latest index entry at or before <paramref name="utc"/>.</summary>
    public static IndexEntry? AtOrBefore(DateTime utc) => Index.LastOrDefault(e => e.T <= utc);
    public static IndexEntry? After(DateTime utc) => Index.FirstOrDefault(e => e.T > utc);
}
