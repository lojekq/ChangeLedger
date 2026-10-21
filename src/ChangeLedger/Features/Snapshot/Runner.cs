using System.Diagnostics;
using ChangeLedger.Shell;

namespace ChangeLedger.Features;

public enum RunResult { Written, Unchanged, Busy, Elevated, Failed }

/// <summary>
/// Takes a snapshot under the mutex Local\lojekq.ChangeLedger.Snapshot: the scheduler's --snapshot process and the app's
/// "Snapshot now" never run two at once. A file is written only when the hash differs from the last one.
/// </summary>
public static class Runner
{
    const string MutexName = @"Local\lojekq.ChangeLedger.Snapshot";

    /// <summary>Verb <c>--snapshot [--reason scheduled|manual]</c>: no window, Idle priority, 5 min limit. Exit 0 = ok, 1 = error, 2 = busy.</summary>
    public static int Verb(string[] args)
    {
        var reason = args is ["--reason", "scheduled" or "manual" or "startup"] ? args[1] : "manual";
        Log.Info($"snapshot {reason}: start");
        try { Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.Idle; } catch { }
        Settings.Load();
        var result = RunResult.Failed;
        var worker = new Thread(() => result = Run(reason, CancellationToken.None)) { IsBackground = true }; // a hung COM call can't keep the process alive
        worker.Start();
        if (!worker.Join(TimeSpan.FromMinutes(5))) { Log.Error("snapshot did not finish in 5 min"); return 1; }
        Log.Info($"snapshot {reason}: {result}");
        return result switch { RunResult.Written or RunResult.Unchanged => 0, RunResult.Busy => 2, _ => 1 };
    }

    public static RunResult Run(string reason, CancellationToken ct)
    {
        // Elevated, the scheduler shows tasks a normal user never sees: snapshots would stop being comparable
        if (AppInfo.IsElevated) { Log.Warn("running elevated, no snapshot"); return RunResult.Elevated; }
        return Locked(() =>
        {
            try
            {
                SnapshotStore.CleanTemp();
                var state = SnapshotStore.LoadState();
                var snap = Collectors.Take(reason, new(SnapshotStore.Salt(state), Settings.Current.ExtraSecretNames), ct);
                var hash = snap.Hash();
                state.LastCheckUtc = snap.TakenUtc;
                var index = SnapshotStore.ReadIndex(); // entries whose file is gone are dropped
                if (hash == state.LastHash && index.Count > 0 && index[^1].H == hash) // files deleted by hand → write again
                {
                    SnapshotStore.SaveState(state);
                    return RunResult.Unchanged;
                }
                Snapshot? prev = null;
                if (index.Count > 0)
                    try { prev = SnapshotStore.Read(index[^1].F); }
                    catch (Exception e) { Log.Warn("previous snapshot unreadable", e); }
                var file = SnapshotStore.Write(snap);
                SnapshotStore.Append(SnapshotStore.Entry(snap, file, prev));
                state.LastHash = hash;
                SnapshotStore.SaveState(state);
                SnapshotStore.Retention(Settings.Current.RetentionDays, Settings.Current.MaxStorageMb);
                return RunResult.Written;
            }
            catch (OperationCanceledException) { return RunResult.Failed; }
            catch (Exception e) { Log.Error("snapshot failed", e); return RunResult.Failed; }
        }, RunResult.Busy);
    }

    /// <summary>Runs <paramref name="work"/> holding the snapshot mutex; <paramref name="busy"/> when another process holds it.</summary>
    public static T Locked<T>(Func<T> work, T busy)
    {
        using var m = new Mutex(false, MutexName);
        bool owned;
        try { owned = m.WaitOne(0); }
        catch (AbandonedMutexException) { owned = true; } // the previous owner died mid-snapshot; CleanTemp tidies up
        if (!owned) return busy;
        try { return work(); }
        finally { m.ReleaseMutex(); }
    }

    public static T? Locked<T>(Func<T> work) where T : class => Locked(work, null);
}
