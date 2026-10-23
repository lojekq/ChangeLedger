using System.Diagnostics.Eventing.Reader;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using ChangeLedger.Shell;

namespace ChangeLedger.Features;

/// <summary>A System log event with its named EventData fields (Message is localized and never parsed).</summary>
public sealed record Ev(DateTime Utc, string Provider, int Id, IReadOnlyDictionary<string, string> Data)
{
    public string? this[string name] => Data.GetValueOrDefault(name);
    public bool Is(string provider, int id) => Id == id && Provider == provider;
}

public enum PowerKind { Restart, Shutdown, Bsod, PowerLoss, PowerButton, Unknown, Wake }

public sealed record PowerEntry(DateTime Utc, PowerKind Kind, string? Initiator = null, string? Reason = null, string? Code = null,
    int? BootType = null, string? WakeSource = null, string? WakeTask = null)
{
    public bool Unexpected => Kind is PowerKind.Bsod or PowerKind.PowerLoss or PowerKind.PowerButton;
}

/// <summary>An installed update; StoreCount &gt; 0 is the day's Store apps grouped into one line.</summary>
public sealed record WuEntry(DateTime Utc, string Title, bool Ok, int StoreCount = 0);

/// <summary>Journal categories are read live for [A, B] and never stored in snapshots (spec §5).</summary>
public static partial class EventHistory
{
    public const string KernelGeneral = "Microsoft-Windows-Kernel-General";   // 12 start, 13 stop
    public const string KernelBoot = "Microsoft-Windows-Kernel-Boot";         // 27 boot type
    public const string KernelPower = "Microsoft-Windows-Kernel-Power";       // 41 unexpected
    public const string User32 = "User32";                                     // 1074 planned
    public const string EventLog = "EventLog";                                 // 6008 unexpected
    public const string Wer = "Microsoft-Windows-WER-SystemErrorReporting";    // 1001 bugcheck
    public const string Troubleshooter = "Microsoft-Windows-Power-Troubleshooter"; // 1 wake
    const string StoreService = "855e8a7c-ecb4-4ca3-b045-1dfa50104289";

    static readonly XNamespace Ns = "http://schemas.microsoft.com/win/2004/08/events/event";

    public const string Display = "Display";                                   // 4101 GPU driver reset (TDR)
    public const string AppError = "Application Error";                         // 1000 app crash (Application log)
    public const string Defender = "Microsoft-Windows-Windows Defender";        // 5007 settings changed (Defender/Operational)
    const string DefenderLog = "Microsoft-Windows-Windows Defender/Operational";

    /// <summary>Power events, GPU resets, app crashes and Defender exclusion changes in [from, to]; a log that can't be read is skipped.</summary>
    public static List<Ev> ReadAll(DateTime fromUtc, DateTime toUtc)
    {
        var list = new List<Ev>();
        void Try(string log, string providers)
        {
            try { list.AddRange(Query(log, providers, fromUtc, toUtc)); }
            catch (Exception e) when (e is EventLogException or UnauthorizedAccessException) { Log.Warn($"event log {log} not read: {e.GetType().Name}"); }
        }
        Try("System", $"""
            (Provider[@Name='{KernelGeneral}'] and (EventID=12 or EventID=13)) or (Provider[@Name='{KernelBoot}'] and EventID=27)
             or (Provider[@Name='{KernelPower}'] and EventID=41) or (Provider[@Name='{User32}'] and EventID=1074)
             or (Provider[@Name='{EventLog}'] and EventID=6008) or (Provider[@Name='{Wer}'] and EventID=1001)
             or (Provider[@Name='{Troubleshooter}'] and EventID=1) or (Provider[@Name='{Display}'] and EventID=4101)
            """);
        Try("Application", $"Provider[@Name='{AppError}'] and EventID=1000");
        Try(DefenderLog, "EventID=5007");
        return list;
    }

    static List<Ev> Query(string log, string providers, DateTime fromUtc, DateTime toUtc)
    {
        var filter = $"*[System[({providers}) and TimeCreated[@SystemTime>='{fromUtc:yyyy-MM-ddTHH:mm:ss.fffZ}' and @SystemTime<='{toUtc:yyyy-MM-ddTHH:mm:ss.fffZ}']]]";
        var list = new List<Ev>();
        using var r = new EventLogReader(new EventLogQuery(log, PathType.LogName, filter));
        while (r.ReadEvent() is { } e)
            using (e)
            {
                var data = XDocument.Parse(e.ToXml()).Descendants(Ns + "Data")
                    .Where(d => (string?)d.Attribute("Name") is { Length: > 0 })
                    .GroupBy(d => (string)d.Attribute("Name")!).ToDictionary(g => g.Key, g => g.First().Value);
                // Defender 5007 is kept only when it is about exclusions: every definition update logs one too
                if (e.Id == 5007 && !data.Values.Any(v => v.Contains(@"\Exclusions", StringComparison.OrdinalIgnoreCase))) continue;
                list.Add(new(e.TimeCreated?.ToUniversalTime() ?? DateTime.MinValue, e.ProviderName, e.Id, data));
            }
        return list;
    }

    /// <summary>The System log rotates (≈ 20 MB): events before this time are gone.</summary>
    public static DateTime? OldestUtc()
    {
        try
        {
            using var r = new EventLogReader(new EventLogQuery("System", PathType.LogName, "*"));
            using var e = r.ReadEvent();
            return e?.TimeCreated?.ToUniversalTime();
        }
        catch (Exception e) { Log.Warn("system log oldest", e); return null; }
    }

    /// <summary>Installed updates, newest first: Defender definitions (KB2267602, several a day) dropped, Store apps grouped per day.</summary>
    public static List<WuEntry> Updates()
    {
        dynamic session = Activator.CreateInstance(Type.GetTypeFromProgID("Microsoft.Update.Session", throwOnError: true)!)!;
        var searcher = session.CreateUpdateSearcher();
        int total = searcher.GetTotalHistoryCount();
        var list = new List<WuEntry>();
        var store = new List<DateTime>();
        foreach (var h in searcher.QueryHistory(0, Math.Min(total, 500)))
            try
            {
                if ((int)h.Operation != 1) continue; // installs only
                string title = h.Title ?? "";
                if (title.Contains("KB2267602")) continue;
                var utc = DateTime.SpecifyKind((DateTime)h.Date, DateTimeKind.Utc);
                if (string.Equals((string)h.ServiceID, StoreService, StringComparison.OrdinalIgnoreCase)) store.Add(utc);
                else list.Add(new(utc, title, (int)h.ResultCode is 2 or 3));
            }
            catch (Exception e) when (e is System.Runtime.InteropServices.COMException or InvalidCastException) { }
        list.AddRange(store.GroupBy(t => t.ToLocalTime().Date).Select(g => new WuEntry(g.Max(), "", true, g.Count())));
        return [.. list.OrderByDescending(u => u.Utc)];
    }

    public static string? Kb(string title) => KbRegex().Match(title) is { Success: true } m ? m.Value : null;

    [GeneratedRegex(@"KB\d{6,8}")] private static partial Regex KbRegex();
}

/// <summary>
/// Explains every boot session (event 12 starts one) and every wake. Unexpected evidence wins over a planned 1074:
/// a restart that hung and was power-cycled is a power loss, not a planned restart.
/// </summary>
public static partial class RebootClassifier
{
    public static List<PowerEntry> Classify(IEnumerable<Ev> events)
    {
        var evs = events.OrderBy(e => e.Utc).ToList();
        var boots = evs.Select((e, i) => (e, i)).Where(x => x.e.Is(EventHistory.KernelGeneral, 12)).Select(x => x.i).ToList();
        var list = new List<PowerEntry>();
        for (var n = 0; n < boots.Count; n++)
        {
            var boot = evs[boots[n]];
            var before = evs[(n > 0 ? boots[n - 1] + 1 : 0)..boots[n]];
            var after = evs[(boots[n] + 1)..(n + 1 < boots.Count ? boots[n + 1] : evs.Count)];
            int? bootType = evs.FirstOrDefault(e => e.Is(EventHistory.KernelBoot, 27) && Math.Abs((e.Utc - boot.Utc).TotalMinutes) < 2)?["BootType"] is { } bt && int.TryParse(bt, out var t) ? t : null;

            var wer = after.FirstOrDefault(e => e.Is(EventHistory.Wer, 1001));
            var k41 = after.FirstOrDefault(e => e.Is(EventHistory.KernelPower, 41));
            var planned = before.LastOrDefault(e => e.Is(EventHistory.User32, 1074));
            var stop = before.LastOrDefault(e => e.Is(EventHistory.KernelGeneral, 13));
            var bugcheck = long.TryParse(k41?["BugcheckCode"], out var code) ? code : 0;

            list.Add(
                wer is not null ? new(boot.Utc, PowerKind.Bsod, Code: wer["param1"]?.Split(' ')[0])
                : bugcheck != 0 ? new(boot.Utc, PowerKind.Bsod, Code: $"0x{bugcheck:X8}")
                : k41 is not null && k41["PowerButtonTimestamp"] is { } pb && pb != "0" ? new(boot.Utc, PowerKind.PowerButton)
                : k41 is not null || after.Any(e => e.Is(EventHistory.EventLog, 6008)) ? new(boot.Utc, PowerKind.PowerLoss)
                : planned is not null ? new(planned.Utc, RestartType().IsMatch(planned["param5"] ?? "") ? PowerKind.Restart : PowerKind.Shutdown,
                    Initiator: Initiator(planned), Reason: string.Join(" · ", new[] { planned["param3"], planned["param6"] }.Where(s => !string.IsNullOrWhiteSpace(s))))
                : stop is not null ? new(stop.Utc, PowerKind.Shutdown)
                : new(boot.Utc, PowerKind.Unknown));
            list[^1] = list[^1] with { BootType = bootType };
        }
        foreach (var w in evs.Where(e => e.Is(EventHistory.Troubleshooter, 1)))
        {
            var text = w["WakeSourceText"];
            list.Add(new(DateTime.TryParse(w["WakeTime"], null, System.Globalization.DateTimeStyles.AdjustToUniversal, out var at) ? at : w.Utc,
                PowerKind.Wake, WakeSource: string.IsNullOrWhiteSpace(text) ? null : text,
                WakeTask: text is null ? null : TaskInText().Match(text) is { Success: true } m ? m.Groups[1].Value : null));
        }
        return [.. list.OrderByDescending(e => e.Utc)];
    }

    /// <summary>"C:\WINDOWS\system32\winlogon.exe (DESKTOP-1)" + "NT AUTHORITY\SYSTEM" → "winlogon.exe, NT AUTHORITY\SYSTEM".</summary>
    static string Initiator(Ev e)
    {
        var process = Regex.Replace(e["param1"] ?? "", @"\s*\([^)]*\)$", "");
        return string.Join(", ", new[] { System.IO.Path.GetFileName(process), e["param7"] }.Where(s => !string.IsNullOrWhiteSpace(s)));
    }

    // ponytail: param5 is localized when written; EN and RU wordings cover the owner's PC and README readers, others read as "shutdown"
    [GeneratedRegex("(?i)restart|перезапус|перезагру")] private static partial Regex RestartType();

    /// <summary>"…задание "NT TASK\Microsoft\Windows\UpdateOrchestrator\Schedule Wake To Work", …" → the task path.</summary>
    [GeneratedRegex("\"(?:NT TASK)?(\\\\[^\"]+)\"")] private static partial Regex TaskInText();
}

/// <summary>A Defender exclusion added ('+'), removed ('-') or changed ('~'); Path is "Paths\C:\Tools", "Processes\x.exe"…</summary>
public sealed record DefenderChange(DateTime Utc, char Kind, string Path);

/// <summary>An app crash (Application Error 1000) or a GPU driver reset (Display 4101, Gpu = true).</summary>
public sealed record AppCrash(DateTime Utc, bool Gpu, string? App = null, string? Detail = null);

/// <summary>What the logs say about [A, B]: restarts and wakes, Defender exclusion changes, crashes, boot times.</summary>
public sealed record Journal(List<PowerEntry> Power, List<DefenderChange> Defender, List<AppCrash> Crashes, List<DateTime> Boots)
{
    public static Journal Of(IEnumerable<Ev> events)
    {
        var evs = events.ToList();
        return new(RebootClassifier.Classify(evs),
            [.. evs.Where(e => e.Is(EventHistory.Defender, 5007)).Select(DefenderOf).OfType<DefenderChange>().OrderBy(d => d.Utc)],
            [.. evs.Where(e => e.Is(EventHistory.AppError, 1000) || e.Is(EventHistory.Display, 4101)).Select(CrashOf).OrderBy(c => c.Utc)],
            [.. evs.Where(e => e.Is(EventHistory.KernelGeneral, 12)).Select(e => e.Utc).Order()]);
    }

    public Journal Between(DateTime fromUtc, DateTime toUtc)
    {
        bool In(DateTime t) => t >= fromUtc && t <= toUtc;
        return new([.. Power.Where(p => In(p.Utc))], [.. Defender.Where(d => In(d.Utc))], [.. Crashes.Where(c => In(c.Utc))], [.. Boots.Where(In)]);
    }

    /// <summary>"Old Value"/"New Value" look like "HKLM\SOFTWARE\Microsoft\Windows Defender\Exclusions\Paths\C:\Tools = 0x0".</summary>
    public static DefenderChange? DefenderOf(Ev e)
    {
        var (old, @new) = (e["Old Value"], e["New Value"]);
        var value = string.IsNullOrWhiteSpace(@new) ? old : @new;
        var i = value?.IndexOf(@"\Exclusions\", StringComparison.OrdinalIgnoreCase) ?? -1;
        if (i < 0) return null;
        var path = value![(i + @"\Exclusions\".Length)..];
        var eq = path.LastIndexOf(" = ", StringComparison.Ordinal);
        return new(e.Utc, string.IsNullOrWhiteSpace(old) ? '+' : string.IsNullOrWhiteSpace(@new) ? '-' : '~', eq > 0 ? path[..eq] : path);
    }

    static AppCrash CrashOf(Ev e) => e.Id == 4101
        ? new(e.Utc, true)
        : new(e.Utc, false, $"{e["AppName"]} {e["AppVersion"]}".Trim(),
            string.Join(" · ", new[] { e["ModuleName"], e["ExceptionCode"] is { Length: > 0 } c ? $"0x{c}" : null }.Where(s => !string.IsNullOrEmpty(s))));
}

/// <summary>
/// %LOCALAPPDATA%\ChangeLedger\events.jsonl: the journal events are copied at every snapshot, so the history outlives
/// the rotation of the Windows logs (System ≈ 20 MB, about 6 months). Written under the snapshot mutex.
/// </summary>
public static class EventArchive
{
    static string FilePath => System.IO.Path.Combine(SnapshotStore.Root, "events.jsonl");

    public static void Append(LedgerState state, int retentionDays)
    {
        var now = DateTime.UtcNow;
        var from = state.LastEventUtc ?? now.AddDays(-retentionDays);
        var fresh = EventHistory.ReadAll(from, now).Where(e => e.Utc > from).OrderBy(e => e.Utc).ToList();
        var keep = now.AddDays(-retentionDays);
        var old = Lines().ToList();
        var kept = old.Where(e => e.Utc >= keep).ToList();
        if (fresh.Count == 0 && kept.Count == old.Count) return;
        AtomicFile.WriteAllText(FilePath, string.Concat(kept.Concat(fresh).Select(e => System.Text.Json.JsonSerializer.Serialize(e, Snapshot.Json) + "\n")));
        if (fresh.Count > 0) state.LastEventUtc = fresh[^1].Utc;
    }

    public static IEnumerable<Ev> Read(DateTime fromUtc, DateTime toUtc) => Lines().Where(e => e.Utc >= fromUtc && e.Utc <= toUtc);

    static IEnumerable<Ev> Lines()
    {
        if (!System.IO.File.Exists(FilePath)) yield break;
        foreach (var line in System.IO.File.ReadLines(FilePath))
        {
            Ev? e = null;
            try { e = System.Text.Json.JsonSerializer.Deserialize<Ev>(line, Snapshot.Json); }
            catch (System.Text.Json.JsonException) { } // a torn line
            if (e is not null) yield return e;
        }
    }
}
