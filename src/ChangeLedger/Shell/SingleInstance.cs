namespace ChangeLedger.Shell;

/// <summary>
/// Mutex Local\lojekq.&lt;App&gt;. A second start signals Local\lojekq.&lt;App&gt;.show and exits;
/// the first instance shows its window. Local\lojekq.&lt;App&gt;.exit asks it to exit (Installer: update, uninstall).
/// --demo takes no mutex, so it runs next to a working instance.
/// </summary>
public static class SingleInstance
{
    static readonly string Name = $@"Local\lojekq.{AppInfo.InstanceName}"; // a dev instance (-dev, -<hash>) never wakes the installed one
    static Mutex? mutex;

    /// <summary>False: another instance runs and was asked to show itself. The callbacks run on a pool thread.</summary>
    public static bool Acquire(Action show, Action exit)
    {
        if (Args.Current.Demo) return true;
        mutex = new Mutex(true, Name, out var first);
        var ev = new EventWaitHandle(false, EventResetMode.AutoReset, Name + ".show");
        if (!first) { ev.Set(); return false; }
        ThreadPool.RegisterWaitForSingleObject(ev, (_, _) => show(), null, Timeout.Infinite, executeOnlyOnce: false);
        // Auto-reset: the signal is used up here, so the copy started next does not exit on it as well
        var quit = new EventWaitHandle(false, EventResetMode.AutoReset, Name + ".exit");
        ThreadPool.RegisterWaitForSingleObject(quit, (_, _) => exit(), null, Timeout.Infinite, executeOnlyOnce: true);
        return true;
    }

    /// <summary>Before a restart, so the new process does not just wake this one.</summary>
    public static void Release()
    {
        mutex?.ReleaseMutex();
        mutex?.Dispose();
        mutex = null;
    }

    /// <summary>Asks the running instance to exit as if from the tray. Versions before this channel do not listen.</summary>
    public static void SignalExit()
    {
        if (EventWaitHandle.TryOpenExisting(Name + ".exit", out var e))
            using (e) e.Set();
    }

    /// <summary>True when no instance holds the mutex, or it let go within <paramref name="timeout"/>.</summary>
    public static bool WaitFree(TimeSpan timeout)
    {
        using var m = new Mutex(false, Name);
        try { if (!m.WaitOne(timeout)) return false; }
        catch (AbandonedMutexException) { } // the owner ended without releasing it: it is ours now
        m.ReleaseMutex();
        return true;
    }
}
