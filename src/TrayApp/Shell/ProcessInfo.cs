using Microsoft.Win32.SafeHandles;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Security;
using Windows.Win32.System.Diagnostics.ToolHelp;
using Windows.Win32.System.Threading;
using Wdk = Windows.Wdk;

namespace TrayApp.Shell;

/// <summary>
/// Facts about other processes. Opens them only with PROCESS_QUERY_LIMITED_INFORMATION (00-shared §4.3), no WMI.
/// Every getter returns null when the process is gone or access is denied (protected process, another user).
/// A pid alone can be reused: pair it with <see cref="StartTime"/> when you keep it.
/// </summary>
public static unsafe class ProcessInfo
{
    public static string? ExePath(int pid) => With(pid, h =>
    {
        var buf = new char[32768];
        uint n = (uint)buf.Length;
        return PInvoke.QueryFullProcessImageName(h, PROCESS_NAME_FORMAT.PROCESS_NAME_WIN32, buf, ref n) ? new string(buf, 0, (int)n) : null;
    });

    /// <summary>UTC.</summary>
    public static DateTime? StartTime(int pid) => With(pid, h =>
        PInvoke.GetProcessTimes(h, out var created, out _, out _, out _)
            ? DateTime.FromFileTimeUtc(((long)created.dwHighDateTime << 32) | (uint)created.dwLowDateTime)
            : (DateTime?)null);

    /// <summary>TokenElevation. null also when the token can't be opened, which for another process often means it is elevated.</summary>
    public static bool? IsElevated(int pid) => With(pid, h =>
    {
        if (!PInvoke.OpenProcessToken(h, TOKEN_ACCESS_MASK.TOKEN_QUERY, out var token)) return (bool?)null;
        using (token)
        {
            TOKEN_ELEVATION e;
            return PInvoke.GetTokenInformation(token, TOKEN_INFORMATION_CLASS.TokenElevation, &e, (uint)sizeof(TOKEN_ELEVATION), out _)
                ? e.TokenIsElevated != 0
                : null;
        }
    });

    /// <summary>The full command line, exe first, as the process got it (NtQueryInformationProcess, class 60). Split it with <see cref="CmdLine"/>.</summary>
    public static string? CommandLine(int pid) => With(pid, h =>
    {
        try
        {
            var handle = (HANDLE)h.DangerousGetHandle();
            uint len = 0;
            Wdk.PInvoke.NtQueryInformationProcess(handle, Wdk.System.Threading.PROCESSINFOCLASS.ProcessCommandLineInformation, null, 0, ref len);
            if (len == 0) return null;
            var buf = new byte[len];
            fixed (byte* p = buf)
            {
                if (Wdk.PInvoke.NtQueryInformationProcess(handle, Wdk.System.Threading.PROCESSINFOCLASS.ProcessCommandLineInformation, p, len, ref len).Value < 0) return null;
                var s = (UNICODE_STRING*)p;
                return new string(s->Buffer.Value, 0, s->Length / 2);
            }
        }
        catch { return null; }
    });

    /// <summary>Toolhelp snapshot of all processes with their start times.</summary>
    public static ProcessTree Tree()
    {
        var list = new List<ProcessNode>();
        using var snap = PInvoke.CreateToolhelp32Snapshot_SafeHandle(CREATE_TOOLHELP_SNAPSHOT_FLAGS.TH32CS_SNAPPROCESS, 0);
        var e = new PROCESSENTRY32W { dwSize = (uint)sizeof(PROCESSENTRY32W) };
        for (var ok = PInvoke.Process32FirstW(snap, ref e); ok; ok = PInvoke.Process32NextW(snap, ref e))
            list.Add(new((int)e.th32ProcessID, (int)e.th32ParentProcessID, e.szExeFile.ToString(), StartTime((int)e.th32ProcessID)));
        return new ProcessTree(list);
    }

    static T? With<T>(int pid, Func<SafeFileHandle, T?> query)
    {
        using var h = PInvoke.OpenProcess_SafeHandle(PROCESS_ACCESS_RIGHTS.PROCESS_QUERY_LIMITED_INFORMATION, false, (uint)pid);
        return h.IsInvalid ? default : query(h);
    }
}

/// <summary>Exe is the file name only ("chrome.exe"); the full path is <see cref="ProcessInfo.ExePath"/>. Started is UTC, null if unknown.</summary>
public sealed record ProcessNode(int Pid, int ParentPid, string Exe, DateTime? Started);

/// <summary>
/// Parent links count only when the parent started no later than the child: a dead parent's pid may belong to a newer process.
/// An unknown start time breaks the link too.
/// </summary>
public sealed class ProcessTree
{
    readonly Dictionary<int, ProcessNode> byPid = [];
    readonly ILookup<int, ProcessNode> byParent;

    public ProcessTree(IEnumerable<ProcessNode> all)
    {
        foreach (var p in all) byPid[p.Pid] = p;
        byParent = byPid.Values.ToLookup(p => p.ParentPid);
    }

    public IReadOnlyCollection<ProcessNode> All => byPid.Values;
    public ProcessNode? this[int pid] => byPid.GetValueOrDefault(pid);

    public ProcessNode? Parent(int pid) =>
        byPid.TryGetValue(pid, out var c) && byPid.TryGetValue(c.ParentPid, out var p) && IsParent(p, c) ? p : null;

    /// <summary>Parent, grandparent, … up to the first link that doesn't count.</summary>
    public IEnumerable<ProcessNode> Ancestors(int pid)
    {
        var seen = new HashSet<int> { pid };
        for (var p = Parent(pid); p is not null && seen.Add(p.Pid); p = Parent(p.Pid))
            yield return p;
    }

    /// <summary>Children, grandchildren, … breadth first, without pid itself.</summary>
    public IEnumerable<ProcessNode> Descendants(int pid)
    {
        if (!byPid.TryGetValue(pid, out var root)) yield break;
        var seen = new HashSet<int> { pid };
        var queue = new Queue<ProcessNode>([root]);
        while (queue.TryDequeue(out var p))
            foreach (var c in byParent[p.Pid])
                if (IsParent(p, c) && seen.Add(c.Pid)) { yield return c; queue.Enqueue(c); }
    }

    static bool IsParent(ProcessNode p, ProcessNode c) => p.Pid != c.Pid && p.Started <= c.Started;
}
