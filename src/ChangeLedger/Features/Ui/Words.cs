using System.ComponentModel;
using System.Globalization;
using System.Text;
using ChangeLedger.Shell;

namespace ChangeLedger.Features;

/// <summary>A day in the timeline: chips with counts, icons for reboots, wakes and Windows updates.</summary>
public sealed record DayRow(DateTime Day, string Label, string Chips, bool Reboot, bool Unexpected, bool Wake, bool Update, string? Tip, bool Empty);

/// <summary>Category header in the diff list; click toggles the rows below.</summary>
public sealed class HeaderRow(string id, string text, string? note, List<ChangeRow> rows, bool expanded) : INotifyPropertyChanged
{
    public string Id { get; } = id;
    public string Text { get; } = text;
    public string? Note { get; } = note;
    public List<ChangeRow> Rows { get; } = rows;
    public bool HasRows => Rows.Count > 0;

    public bool Expanded
    {
        get => expanded;
        set { expanded = value; PropertyChanged?.Invoke(this, new(nameof(Expanded))); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

public sealed record ChangeRow(Change Change, string Kind, string Title, string? Secondary, IReadOnlyList<string> Lines)
{
    public bool HasSecondary => Secondary is not null;
    public string Glyph => Kind switch { "+" => "+", "-" => "−", _ => "~" };
}

/// <summary>Everything the pages and "Copy for AI" say about changes, in the UI language.</summary>
public static class Words
{
    static CultureInfo C => CultureInfo.CurrentUICulture;

    public static string Category(string id) => Loc.T($"Cat.{id}") is var t && t.StartsWith('[') ? id : t;
    public static string Day(DateTime local) => local.ToString("ddd d MMM", C);
    public static string When(DateTime utc) => utc.ToLocalTime().ToString("d MMM HH:mm", C);
    public static string WhenFull(DateTime utc) => utc.ToLocalTime().ToString("d MMM yyyy HH:mm", C);

    public static string Value(string prop, string? v) => (prop, v) switch
    {
        (_, null) => "—",
        ("Start", "0" or "1" or "2" or "3" or "4") => Loc.T($"Start.{v}"),
        ("Enabled" or "WakeToRun" or "SystemComponent", "true" or "1") => Loc.T("Word.Yes"),
        ("Enabled" or "WakeToRun", "false") => Loc.T("Word.No"),
        _ => v,
    };

    public static string Counts(IEnumerable<Change> changes)
    {
        var parts = new List<string>();
        foreach (var (kind, key) in new[] { ('+', "Count.Added"), ('-', "Count.Removed"), ('~', "Count.Changed") })
            if (changes.Count(c => c.Kind == kind) is > 0 and var n) parts.Add(string.Format(Loc.T(key), n));
        return string.Join(", ", parts);
    }

    public static string Header(string id, IReadOnlyCollection<Change> visible) =>
        visible.Count == 0 ? Category(id) : $"{Category(id)} · {Counts(visible)}";

    public static string? NoData(CategoryDiff d) => d.NoDataIn is { } side ? string.Format(Loc.T("Changes.NoData"), side, d.Error ?? "—") : null;

    public static string Chips(IReadOnlyDictionary<string, int> d) =>
        string.Join(" · ", d.OrderBy(kv => Array.FindIndex(Collectors.All, c => c.Id == kv.Key) is var i and >= 0 ? i : 99).Select(kv => $"{Category(kv.Key)} {kv.Value}"));

    public static ChangeRow Row(Change c) => new(c, c.Kind.ToString(), c.Title, Secondary(c), Lines(c));

    static string? Secondary(Change c)
    {
        var i = c.B ?? c.A;
        if (i is null) return null;
        var s = c.Category switch
        {
            "apps" => Join(i["Publisher"], i["DisplayVersion"]),
            "drivers" => Join(i["Class"], i["ProviderName"], i["DriverVersion"]),
            "services" => Join(c.Key, Value("Start", i["Start"])),
            "tasks" => c.Key,
            "autostart" => Join(c.Key[..Math.Max(0, c.Key.LastIndexOf('\\'))], i["Command"]),
            "env" => DiffEngine.IsPath(c.Key) ? Scope(c.Key) : Join(Scope(c.Key), Cut(i["Value"])),
            _ => null,
        };
        return string.IsNullOrEmpty(s) ? null : s;

        static string Scope(string key) => Loc.T(key.StartsWith("user\\") ? "Env.User" : "Env.Machine");
        static string? Cut(string? v) => v is { Length: > 100 } ? v[..100] + "…" : v;
    }

    static List<string> Lines(Change c)
    {
        var lines = c.Props.Select(p => $"{p.Name}: {Value(p.Name, p.Old)} → {Value(p.Name, p.New)}").ToList();
        foreach (var s in c.Path ?? [])
            lines.Add(s.Kind == '+' ? string.Format(Loc.T("Path.Added"), s.Position, s.Element) : string.Format(Loc.T("Path.Removed"), s.Element));
        if (c.PathOrder) lines.Add(Loc.T("Path.Order"));
        return lines;
    }

    static string Join(params string?[] parts) => string.Join(" · ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));

    /// <summary>One line for the clipboard: "~ NVIDIA GeForce RTX 5060 Ti (Display · NVIDIA) — DriverVersion: 1 → 2".</summary>
    public static string Line(ChangeRow r) =>
        $"{r.Kind} {r.Title}{(r.Secondary is null ? "" : $" ({r.Secondary})")}{(r.Lines.Count == 0 ? "" : " — " + string.Join("; ", r.Lines))}";

    /// <summary>"Copy path": where the thing lives on disk or in the scheduler.</summary>
    public static string Path(Change c)
    {
        var i = c.B ?? c.A;
        return c.Category switch
        {
            "apps" => i?["InstallLocation"],
            "drivers" => i?["InfPath"],
            "services" => i?["ImagePath"],
            "autostart" => i?["Command"],
            _ => null,
        } ?? c.Key;
    }

    public static string PowerEvent(PowerEntry e) => e.Kind == PowerKind.Bsod
        ? string.Format(Loc.T("Power.Bsod"), e.Code ?? "?")
        : Loc.T($"Power.{e.Kind}");

    public static string PowerCause(PowerEntry e)
    {
        var parts = new List<string?> { e.Initiator, e.Reason, e.WakeTask ?? e.WakeSource };
        if (e.BootType is 1 or 2) parts.Add(Loc.T($"Boot.{e.BootType}"));
        return Join([.. parts]) is { Length: > 0 } s ? s : "—";
    }

    public static string PowerHint(PowerEntry e, IReadOnlyCollection<string> wakeArmed) => e.Kind switch
    {
        PowerKind.Wake when e.WakeTask is not null => Loc.T("Hint.WakeTask"),
        PowerKind.Wake when e.WakeSource is { } src && wakeArmed.Any(d => src.Contains(d, StringComparison.OrdinalIgnoreCase)) => Loc.T("Hint.WakeDevice"),
        PowerKind.Wake => Loc.T("Hint.WakeUnknown"),
        PowerKind.Bsod => Loc.T("Hint.Bsod"),
        PowerKind.PowerLoss => Loc.T("Hint.PowerLoss"),
        PowerKind.PowerButton => Loc.T("Hint.PowerButton"),
        _ => "",
    };

    /// <summary>
    /// "Copy for AI": header with the range, lists per category, reboots and updates; at most ~300 lines.
    /// Values were masked before they reached the disk, so nothing secret can get here.
    /// </summary>
    public static string Markdown(DateTime aUtc, DateTime bUtc, IReadOnlyList<(string Id, List<ChangeRow> Rows)> cats,
        IEnumerable<string> notes, IEnumerable<PowerEntry> power, IEnumerable<WuEntry> updates)
    {
        const int Max = 300;
        var lines = new List<string>
        {
            $"# {string.Format(Loc.T("Md.Title"), WhenFull(aUtc), WhenFull(bUtc))}",
            $"{AppInfo.DisplayName} {AppInfo.Version} · {Loc.T("Md.Masked")}",
            "",
        };
        lines.AddRange(notes.Select(n => $"> {n}"));
        foreach (var (id, rows) in cats.Where(c => c.Rows.Count > 0))
        {
            lines.Add($"## {Category(id)} — {Counts(rows.Select(r => r.Change))}");
            foreach (var r in rows)
            {
                lines.Add($"- {r.Kind} **{r.Title}**{(r.Secondary is null ? "" : $" ({r.Secondary})")}");
                lines.AddRange(r.Lines.Select(l => $"  - {l}"));
            }
        }
        if (power.ToList() is { Count: > 0 } p)
        {
            lines.Add($"## {Loc.T("Md.Power")}");
            lines.AddRange(p.OrderBy(e => e.Utc).Select(e => $"- {When(e.Utc)} — {PowerEvent(e)} ({PowerCause(e)})"));
        }
        if (updates.ToList() is { Count: > 0 } u)
        {
            lines.Add($"## {Loc.T("Md.Updates")}");
            lines.AddRange(u.OrderBy(x => x.Utc).Select(x => $"- {When(x.Utc)} — {UpdateTitle(x)}"));
        }
        var sb = new StringBuilder();
        foreach (var l in lines.Take(Max)) sb.AppendLine(l);
        if (lines.Count > Max) sb.AppendLine(string.Format(Loc.T("Md.More"), lines.Count - Max));
        return sb.ToString();
    }

    public static string UpdateTitle(WuEntry u) =>
        (u.StoreCount > 0 ? string.Format(Loc.T("Wu.Store"), u.StoreCount) : u.Title) + (u.Ok ? "" : $" ({Loc.T("Wu.Failed")})");
}
