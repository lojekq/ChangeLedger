namespace TrayApp.Shell;

/// <summary>
/// Mutex Local\lojekq.&lt;App&gt;. A second start signals Local\lojekq.&lt;App&gt;.show and exits;
/// the first instance shows its window. --demo takes no mutex, so it runs next to a working instance.
/// </summary>
public static class SingleInstance
{
    static Mutex? mutex;

    /// <summary>False: another instance runs and was asked to show itself. <paramref name="show"/> runs on a pool thread.</summary>
    public static bool Acquire(Action show)
    {
        if (Args.Current.Demo) return true;
        mutex = new Mutex(true, $@"Local\lojekq.{AppInfo.Name}", out var first);
        var ev = new EventWaitHandle(false, EventResetMode.AutoReset, $@"Local\lojekq.{AppInfo.Name}.show");
        if (!first) { ev.Set(); return false; }
        ThreadPool.RegisterWaitForSingleObject(ev, (_, _) => show(), null, Timeout.Infinite, executeOnlyOnce: false);
        return true;
    }

    /// <summary>Before a restart, so the new process does not just wake this one.</summary>
    public static void Release()
    {
        mutex?.ReleaseMutex();
        mutex?.Dispose();
        mutex = null;
    }
}
