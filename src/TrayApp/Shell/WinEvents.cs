using System.Runtime.ExceptionServices;
using System.Windows.Threading;
using Windows.Win32;
using Windows.Win32.UI.Accessibility;

namespace TrayApp.Shell;

/// <summary>
/// SetWinEventHook out of context, skipping our own process; no low-level hooks (00-shared §4.3).
/// Hook on the UI thread: events arrive through its message loop.
/// </summary>
public static class WinEvents
{
    static readonly List<Hooked> Live = [];

    static WinEvents() => App.Exiting += UnhookAll;

    /// <summary>callback(event, hwnd, idObject, idChild); pid 0 = all processes. Dispose to unhook.</summary>
    public static IDisposable Hook(uint min, uint max, Action<uint, nint, int, int> callback, int pid = 0)
    {
        var hook = new Hooked(callback);
        hook.Handle = PInvoke.SetWinEventHook(min, max, null, hook.Proc, (uint)pid, 0, PInvoke.WINEVENT_OUTOFCONTEXT | PInvoke.WINEVENT_SKIPOWNPROCESS);
        if (hook.Handle.IsInvalid) throw new InvalidOperationException($"SetWinEventHook(0x{min:X}, 0x{max:X}) failed");
        Live.Add(hook);
        return hook;
    }

    /// <summary>Unhooks whatever is still hooked; runs on App.Exiting by itself.</summary>
    public static void UnhookAll()
    {
        foreach (var h in Live.ToArray()) h.Dispose();
    }

    sealed class Hooked(Action<uint, nint, int, int> callback) : IDisposable
    {
        // Kept in a field: once the GC collects a delegate that Windows still calls, the process dies on the next event
        public readonly WINEVENTPROC Proc = (_, ev, hwnd, idObject, idChild, _, _) =>
        {
            try { callback(ev, hwnd, idObject, idChild); }
            catch (Exception e)
            {
                // An exception must not cross the native frame (that kills the process with no crash log): rethrow it on the dispatcher
                var info = ExceptionDispatchInfo.Capture(e);
                Dispatcher.CurrentDispatcher.BeginInvoke(info.Throw);
            }
        };

        public UnhookWinEventSafeHandle Handle = null!;

        public void Dispose()
        {
            Handle.Dispose();
            Live.Remove(this);
        }
    }
}
