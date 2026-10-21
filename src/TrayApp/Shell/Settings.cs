using System.IO;
using System.Text.Json;

namespace TrayApp.Shell;

/// <summary>
/// %APPDATA%\&lt;App&gt;\settings.json. Apps add their own fields in Features/ with
/// <c>public sealed partial class Settings { public int Grace { get; set; } = 60; }</c>.
/// Under --demo settings live in memory only.
/// </summary>
public sealed partial class Settings
{
    public int Schema { get; set; } = 1;
    public string Language { get; set; } = "system";   // system | en | ru
    public string Theme { get; set; } = "system";      // system | light | dark
    public bool CheckUpdates { get; set; } = true;
    public string? NotifiedVersion { get; set; }       // update balloon is shown once per version
    public bool TrayHintShown { get; set; }            // "still running in the tray" balloon, first close only
    public bool FirstRunDone { get; set; }             // first start from Install.Dir done (Start menu shortcut on)

    public static Settings Current { get; private set; } = new();
    public static string FilePath => Path.Combine(AppInfo.DataDir, "settings.json");

    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static void Load() => Current = Args.Current.Demo ? new() : Load(FilePath);

    /// <summary>Corrupt JSON → settings.bad-&lt;time&gt;.json, then .bak, then defaults.</summary>
    internal static Settings Load(string path) => AtomicFile.Read(path, s => JsonSerializer.Deserialize<Settings>(s, Json)) ?? new();

    public static void Save()
    {
        if (Args.Current.Demo) return;
        try { Save(FilePath); }
        catch (Exception e) { Log.Error("settings not saved", e); }
    }

    internal static void Save(string path) => AtomicFile.WriteAllText(path, JsonSerializer.Serialize(Current, Json));
}
