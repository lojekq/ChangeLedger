using System.Text.RegularExpressions;

namespace ChangeLedger.Features;

public sealed record PropChange(string Name, string? Old, string? New);

/// <summary>PATH element added ('+', position in B) or removed ('-', position in A); 1-based.</summary>
public sealed record PathStep(char Kind, string Element, int Position);

/// <summary>Kind: '+' added, '-' removed, '~' changed. Noise: matched the built-in noise list (shown with "Show hidden").</summary>
public sealed record Change(string Category, char Kind, string Key, string Title, IReadOnlyList<PropChange> Props, bool Noise,
    IReadOnlyList<PathStep>? Path = null, bool PathOrder = false, Item? A = null, Item? B = null);

/// <summary>NoDataIn 'A'/'B' + Error: the category failed in that snapshot, so no diff is built (an error is not "everything removed").</summary>
public sealed record CategoryDiff(string Id, IReadOnlyList<Change> Changes, char? NoDataIn = null, string? Error = null, bool FormatChanged = false);

public sealed record Diff(IReadOnlyList<CategoryDiff> Categories, bool BuildChanged)
{
    public IEnumerable<Change> All => Categories.SelectMany(c => c.Changes);
}

public sealed record KeyVersion(string Label, string? Old, string? New)
{
    public bool Changed => Old != New;
}

/// <summary>Net A→B diff by key; per-property pairs for changed items; PATH element by element.</summary>
public static partial class DiffEngine
{
    /// <summary>
    /// Built-in noise: (category, key regex), filled from what the owner's PC showed in the first two weeks.
    /// lojekq's own entries and the 8 apps of the set are real changes, never noise (spec §5).
    /// </summary>
    public static readonly (string Category, Regex Key)[] Noise =
    [
        ("services", new Regex("^BITS$")), // Windows itself flips Start 2 ↔ 3 (2026-10-21, 20 min apart)
    ];

    public static Diff Compare(Snapshot a, Snapshot b)
    {
        var ids = Collectors.All.Select(d => d.Id).Concat(a.Categories.Keys.Union(b.Categories.Keys).Order(StringComparer.Ordinal)).Distinct();
        var cats = new List<CategoryDiff>();
        foreach (var id in ids)
        {
            var ca = a.Categories.GetValueOrDefault(id);
            var cb = b.Categories.GetValueOrDefault(id);
            if (ca is null && cb is null) continue;
            if (ca is not { Ok: true }) cats.Add(new(id, [], 'A', ca?.Error));
            else if (cb is not { Ok: true }) cats.Add(new(id, [], 'B', cb?.Error));
            else cats.Add(new(id, Items(id, ca.Items, cb.Items), FormatChanged: ca.V != cb.V));
        }
        return new(cats, Feature(a) is { } fa && Feature(b) is { } fb && fa != fb);

        static string? Feature(Snapshot s) => s.Categories.GetValueOrDefault("os") is { Ok: true } ? s.Find("os", "windows")?["Build"]?.Split('.')[0] : null;
    }

    static List<Change> Items(string category, List<Item> a, List<Item> b)
    {
        var da = a.ToDictionary(i => i.K, StringComparer.Ordinal);
        var db = b.ToDictionary(i => i.K, StringComparer.Ordinal);
        var list = new List<Change>();
        foreach (var x in b.Where(x => !da.ContainsKey(x.K))) list.Add(new(category, '+', x.K, x.T, [], IsNoise(category, x.K), B: x));
        foreach (var x in a.Where(x => !db.ContainsKey(x.K))) list.Add(new(category, '-', x.K, x.T, [], IsNoise(category, x.K), A: x));
        foreach (var x in b.Where(x => da.ContainsKey(x.K)))
        {
            var old = da[x.K];
            if (category == "env" && IsPath(x.K))
            {
                var (steps, order) = PathDiff(old["Value"] ?? "", x["Value"] ?? "");
                if (steps.Count > 0 || order) list.Add(new(category, '~', x.K, x.T, [], IsNoise(category, x.K), steps, order, old, x));
                continue;
            }
            var props = old.P.Keys.Union(x.P.Keys).Order(StringComparer.Ordinal)
                .Where(p => old[p] != x[p]).Select(p => new PropChange(p, old[p], x[p])).ToList();
            if (props.Count > 0) list.Add(new(category, '~', x.K, x.T, props, IsNoise(category, x.K), A: old, B: x));
        }
        return [.. list.OrderBy(c => c.Title, StringComparer.CurrentCultureIgnoreCase)];
    }

    public static bool IsPath(string envKey) => envKey.EndsWith(@"\Path", StringComparison.OrdinalIgnoreCase);

    static bool IsNoise(string category, string key) => Noise.Any(n => n.Category == category && n.Key.IsMatch(key));

    /// <summary>Elements compared without case and trailing '\'; empty elements ignored. Order = the common elements' relative order.</summary>
    public static (List<PathStep> Steps, bool OrderChanged) PathDiff(string a, string b)
    {
        static List<string> Split(string s) => [.. s.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)];
        static string Norm(string e) => e.TrimEnd('\\').ToUpperInvariant();
        var ea = Split(a);
        var eb = Split(b);
        var setA = ea.Select(Norm).ToHashSet();
        var setB = eb.Select(Norm).ToHashSet();
        var steps = new List<PathStep>();
        for (var i = 0; i < eb.Count; i++) if (!setA.Contains(Norm(eb[i]))) steps.Add(new('+', eb[i], i + 1));
        for (var i = 0; i < ea.Count; i++) if (!setB.Contains(Norm(ea[i]))) steps.Add(new('-', ea[i], i + 1));
        var commonA = ea.Select(Norm).Where(setB.Contains).Distinct();
        var commonB = eb.Select(Norm).Where(setA.Contains).Distinct();
        return (steps, !commonA.SequenceEqual(commonB));
    }

    /// <summary>For index.jsonl: changes per category, noise excluded.</summary>
    public static Dictionary<string, int> Counts(Snapshot a, Snapshot b) =>
        Compare(a, b).Categories.Select(c => (c.Id, N: c.Changes.Count(x => !x.Noise))).Where(c => c.N > 0).ToDictionary(c => c.Id, c => c.N);

    /// <summary>"Windows 26300.9457 → 26300.9512 · NVIDIA 32.0.16.1088 → 32.0.16.1123 · BIOS F12 → F13".</summary>
    public static List<KeyVersion> KeyVersions(Snapshot a, Snapshot b)
    {
        var list = new List<KeyVersion> { new("Windows", a.Find("os", "windows")?["Build"], b.Find("os", "windows")?["Build"]) };
        var gpus = new List<Item>();
        foreach (var s in new[] { b, a })
            foreach (var i in s.Categories.GetValueOrDefault("drivers")?.Items ?? [])
                if (i["Class"] == "Display" && gpus.All(g => g.K != i.K)) gpus.Add(i);
        foreach (var g in gpus.Take(3))
        {
            var label = (g["DriverDesc"] ?? g.T).Split(' ')[0].Replace("(R)", "");
            list.Add(new(label, a.Find("drivers", g.K)?["DriverVersion"], b.Find("drivers", g.K)?["DriverVersion"]));
        }
        list.Add(new("BIOS", a.Find("os", "bios")?["BIOSVersion"], b.Find("os", "bios")?["BIOSVersion"]));
        return [.. list.Where(k => k.Old is not null || k.New is not null)];
    }
}
