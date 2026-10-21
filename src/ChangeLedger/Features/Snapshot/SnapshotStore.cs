using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using ChangeLedger.Shell;

namespace ChangeLedger.Features;

/// <summary>A line of index.jsonl: {"t","f","r","h","d":{"drivers":1,…},"err":["tasks"]}; d counts changes against the previous written snapshot.</summary>
public sealed record IndexEntry(DateTime T, string F, string R, string H, Dictionary<string, int> D, string[]? Err = null)
{
    [System.Text.Json.Serialization.JsonIgnore] public int Total => D.Values.Sum();
}

public sealed class LedgerState
{
    public DateTime? LastCheckUtc { get; set; }
    public string? LastHash { get; set; }
    public string? Salt { get; set; }   // random 16 bytes for the masking hashes
}

/// <summary>
/// %LOCALAPPDATA%\ChangeLedger: snapshots\&lt;yyyyMMddTHHmmssZ&gt;-&lt;reason&gt;.json.gz, index.jsonl, state.json.
/// Writers (snapshot, rebuild, retention) run under the snapshot mutex (<see cref="Runner"/>); readers tolerate a half-written last line.
/// </summary>
public static partial class SnapshotStore
{
    static string Root => AppInfo.LocalDir;
    public static string SnapDir => Path.Combine(Root, "snapshots");
    public static string IndexPath => Path.Combine(Root, "index.jsonl");
    static string StatePath => Path.Combine(Root, "state.json");

    public static LedgerState LoadState() => AtomicFile.Read(StatePath, s => JsonSerializer.Deserialize<LedgerState>(s, Snapshot.Json)) ?? new();
    public static void SaveState(LedgerState s) => AtomicFile.WriteAllText(StatePath, JsonSerializer.Serialize(s, Snapshot.Json));

    public static byte[] Salt(LedgerState s) => Convert.FromBase64String(s.Salt ??= Convert.ToBase64String(RandomNumberGenerator.GetBytes(16)));

    /// <summary>.tmp, then rename: a power cut leaves either the old set of files or the new one. Same second → "-2".</summary>
    public static string Write(Snapshot s)
    {
        Directory.CreateDirectory(SnapDir);
        var stem = $"{s.TakenUtc:yyyyMMdd'T'HHmmss'Z'}-{s.Reason}";
        var name = stem + ".json.gz";
        for (var n = 2; File.Exists(Path.Combine(SnapDir, name)); n++) name = $"{stem}-{n}.json.gz";
        var path = Path.Combine(SnapDir, name);
        using (var f = new FileStream(path + ".tmp", FileMode.Create, FileAccess.Write, FileShare.None))
        {
            using (var gz = new GZipStream(f, CompressionLevel.SmallestSize, leaveOpen: true))
                JsonSerializer.Serialize(gz, s.Canonical(), Snapshot.Json);
            f.Flush(flushToDisk: true);
        }
        File.Move(path + ".tmp", path, overwrite: true);
        return name;
    }

    public static Snapshot Read(string file)
    {
        using var f = File.OpenRead(Path.Combine(SnapDir, file));
        using var gz = new GZipStream(f, CompressionMode.Decompress);
        return JsonSerializer.Deserialize<Snapshot>(gz, Snapshot.Json) ?? throw new InvalidDataException(file);
    }

    /// <summary>Broken lines (power cut mid-write) are skipped; no index at all → rebuilt from the files.</summary>
    public static List<IndexEntry> ReadIndex()
    {
        if (!File.Exists(IndexPath)) return Directory.Exists(SnapDir) && SnapshotFiles().Any() ? Runner.Locked(Rebuild) ?? [] : [];
        var list = new List<IndexEntry>();
        using var r = new StreamReader(new FileStream(IndexPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete));
        while (r.ReadLine() is { } line)
            try { if (JsonSerializer.Deserialize<IndexEntry>(line, Snapshot.Json) is { } e && File.Exists(Path.Combine(SnapDir, e.F))) list.Add(e); }
            catch (JsonException) { }
        return [.. list.OrderBy(e => e.T)];
    }

    public static void Append(IndexEntry e)
    {
        Directory.CreateDirectory(Root);
        using var f = new FileStream(IndexPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read);
        var newline = f.Length > 0 && Last(f) != '\n'; // a torn last line must not swallow this one
        f.Seek(0, SeekOrigin.End);
        var bytes = System.Text.Encoding.UTF8.GetBytes((newline ? "\n" : "") + JsonSerializer.Serialize(e, Snapshot.Json) + "\n");
        f.Write(bytes);
        f.Flush(flushToDisk: true);

        static int Last(FileStream f) { f.Seek(-1, SeekOrigin.End); return f.ReadByte(); }
    }

    static void WriteIndex(IEnumerable<IndexEntry> entries) =>
        AtomicFile.WriteAllText(IndexPath, string.Concat(entries.Select(e => JsonSerializer.Serialize(e, Snapshot.Json) + "\n")));

    /// <summary>Reads every snapshot again: hashes, change counts against the previous one. Call under the mutex.</summary>
    public static List<IndexEntry> Rebuild()
    {
        var list = new List<IndexEntry>();
        Snapshot? prev = null;
        foreach (var file in SnapshotFiles().Select(Path.GetFileName).Order(StringComparer.Ordinal))
        {
            Snapshot s;
            try { s = Read(file!); }
            catch (Exception e) when (e is IOException or InvalidDataException or JsonException) { Log.Warn("unreadable snapshot skipped", e); continue; }
            list.Add(Entry(s, file!, prev));
            prev = s;
        }
        WriteIndex(list);
        Log.Info($"index rebuilt: {list.Count} snapshots");
        return list;
    }

    public static IndexEntry Entry(Snapshot s, string file, Snapshot? prev) =>
        new(s.TakenUtc, file, s.Reason, s.Hash(), prev is null ? [] : DiffEngine.Counts(prev, s),
            s.Categories.Where(c => !c.Value.Ok).Select(c => c.Key).ToArray() is { Length: > 0 } err ? err : null);

    public static (long Bytes, int Count) Usage()
    {
        if (!Directory.Exists(SnapDir)) return (0, 0);
        var files = SnapshotFiles().Select(f => new FileInfo(f)).ToList();
        return (files.Sum(f => f.Length), files.Count);
    }

    /// <summary>Deletes by age and size, then drops the deleted files from the index. Call under the mutex.</summary>
    public static void Retention(int days, int maxMb)
    {
        if (!Directory.Exists(SnapDir)) return;
        var doomed = PickForDeletion(SnapshotFiles().Select(f => (Path.GetFileName(f), new FileInfo(f).Length)), DateTime.UtcNow, days, maxMb * 1024L * 1024).ToHashSet();
        if (doomed.Count == 0) return;
        foreach (var name in doomed)
            try { File.Delete(Path.Combine(SnapDir, name)); }
            catch (IOException e) { Log.Warn("retention: file not deleted", e); }
        WriteIndex(ReadIndex());
        Log.Info($"retention: {doomed.Count} snapshot(s) deleted");
    }

    /// <summary>
    /// Only our own file names count (foreign files in the folder are never touched), the two newest are never deleted.
    /// Older than <paramref name="days"/> goes first, then the oldest until the total fits <paramref name="maxBytes"/>.
    /// </summary>
    public static IEnumerable<string> PickForDeletion(IEnumerable<(string Name, long Size)> files, DateTime nowUtc, int days, long maxBytes)
    {
        var snaps = files.Where(f => FileName().IsMatch(f.Name)).OrderByDescending(f => f.Name, StringComparer.Ordinal).ToList();
        var total = snaps.Sum(f => f.Size);
        var doomed = new List<string>();
        foreach (var f in snaps.Skip(2).Reverse()) // oldest first
        {
            if (TakenUtc(f.Name) >= nowUtc.AddDays(-days) && total <= maxBytes) break;
            doomed.Add(f.Name);
            total -= f.Size;
        }
        return doomed;
    }

    public static DateTime TakenUtc(string file) =>
        DateTime.ParseExact(file[..16], "yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);

    /// <summary>Leftovers of a write cut short (power, kill). Call under the mutex.</summary>
    public static void CleanTemp()
    {
        if (!Directory.Exists(SnapDir)) return;
        foreach (var f in Directory.GetFiles(SnapDir, "*.json.gz.tmp"))
            try { File.Delete(f); } catch (IOException) { }
    }

    static IEnumerable<string> SnapshotFiles() => Directory.EnumerateFiles(SnapDir, "*.json.gz").Where(f => FileName().IsMatch(Path.GetFileName(f)));

    /// <summary>Retention deletes nothing else (spec §8).</summary>
    [GeneratedRegex(@"^\d{8}T\d{6}Z-[a-z]+(-\d+)?\.json\.gz$")] private static partial Regex FileName();
}
