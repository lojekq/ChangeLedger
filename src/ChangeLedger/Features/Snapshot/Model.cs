using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ChangeLedger.Features;

/// <summary>
/// One read-only picture of the system:
/// {"schema":1,"takenUtc":…,"reason":"scheduled|startup|manual","app":"0.1.0","categories":{"drivers":{"v":1,"status":"ok","items":[{"k":…,"t":…,"p":{…}}]}}}
/// </summary>
public sealed class Snapshot
{
    public int Schema { get; set; } = 1;
    public DateTime TakenUtc { get; set; }
    public string Reason { get; set; } = "manual";
    public string App { get; set; } = "";
    public SortedDictionary<string, Category> Categories { get; set; } = new(StringComparer.Ordinal);

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, // readable paths and Cyrillic in the file; never embedded in HTML
    };

    /// <summary>Items sorted by key, properties by name (ordinal), so the same state always gives the same bytes.</summary>
    public Snapshot Canonical()
    {
        var c = new Snapshot { Schema = Schema, TakenUtc = TakenUtc, Reason = Reason, App = App };
        foreach (var (id, cat) in Categories)
            c.Categories[id] = new Category
            {
                V = cat.V, Status = cat.Status, Error = cat.Error,
                Items = [.. cat.Items.OrderBy(i => i.K, StringComparer.Ordinal).Select(i => new Item { K = i.K, T = i.T, P = new(i.P, StringComparer.Ordinal) })],
            };
        return c;
    }

    /// <summary>SHA-256 of the canonical categories only: time, reason and app version don't make a snapshot "different".</summary>
    public string Hash() => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(Canonical().Categories, Json)));

    public Item? Find(string category, string key) => Categories.GetValueOrDefault(category)?.Items.FirstOrDefault(i => i.K == key);
}

public sealed class Category
{
    public int V { get; set; } = 1;
    public string Status { get; set; } = "ok";   // ok | error
    public string? Error { get; set; }
    public List<Item> Items { get; set; } = [];

    [JsonIgnore] public bool Ok => Status == "ok";
}

/// <summary>K is the stable key inside the category, T the title for people, P the compared properties.</summary>
public sealed class Item
{
    public string K { get; set; } = "";
    public string T { get; set; } = "";
    public SortedDictionary<string, string> P { get; set; } = new(StringComparer.Ordinal);

    public string? this[string prop] => P.GetValueOrDefault(prop);
}
