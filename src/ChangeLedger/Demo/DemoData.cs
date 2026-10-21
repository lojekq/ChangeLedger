using ChangeLedger.Features;
using ChangeLedger.Shell;

namespace ChangeLedger.Demo;

/// <summary>
/// --demo: 30 days of snapshots with changes on six days, 3 updates, 6 power events (spec §10). Believable, invented versions;
/// C:\Users\demo\…, no real data. Story: on day −6 an update brings a new NVIDIA driver, the same evening the PC crashes (0x9F).
/// </summary>
public static class DemoData
{
    public static Dictionary<string, Snapshot> Snapshots { get; } = new(StringComparer.Ordinal);
    public static List<IndexEntry> Index { get; } = [];
    public static LedgerState State { get; } = new();
    public static List<PowerEntry> Power { get; } = [];
    public static List<DateTime> Boots { get; } = [];
    public static List<WuEntry> Updates { get; } = [];
    public static (long Bytes, int Count) Usage => (37L * 1024 * 1024, 212);

    static string envBefore = "", envAfter = "";

    public sealed record DemoScene(string A, string B, string[] Expand, WuEntry? Update);

    /// <summary>--page changes (hero: before/after the update), changes-apps, changes-path.</summary>
    public static DemoScene Scene(string? name) => name switch
    {
        "apps" => new(Index[0].F, Index[^1].F, ["apps", "autostart"], null),
        "path" => new(envBefore, envAfter, ["env"], null),
        _ => new("", "", ["drivers", "services"], Updates.First(u => u.Title.Contains("KB5070311"))),
    };

    static DateTime At(int day, int hour, int minute = 0) => DateTime.Today.AddDays(day).AddHours(hour).AddMinutes(minute).ToUniversalTime();

    public static void Fill()
    {
        var s = Base();
        s.TakenUtc = At(-29, 12);
        Add(s, "scheduled");

        s = Next(s, At(-24, 12), x => App(x, "7-Zip", "7-Zip 25.01 (x64)", "25.01", "Igor Pavlov", @"C:\Program Files\7-Zip\"));
        s = Next(s, At(-18, 12), x =>
        {
            App(x, "Ollama", "Ollama version 0.13.1", "0.13.1", "Ollama", @"C:\Users\demo\AppData\Local\Programs\Ollama\", hive: "HKCU");
            App(x, "Docker Desktop", "Docker Desktop", "4.49.0", "Docker Inc.", @"C:\Program Files\Docker\Docker");
            x.Find("autostart", @"HKCU\Run\Discord")!.P["Enabled"] = "false";
        });
        envBefore = Index[^1].F;
        s = Next(s, At(-13, 12), x =>
        {
            Env(x, @"user\Path", @"C:\Python313\;C:\Users\demo\AppData\Local\Microsoft\WindowsApps;C:\Users\demo\.dotnet\tools;C:\Users\demo\AppData\Local\Programs\Ollama;C:\Users\demo\AppData\Local\Programs\Microsoft VS Code\bin");
            Env(x, @"machine\CUDA_PATH", @"C:\Program Files\NVIDIA GPU Computing Toolkit\CUDA\v13.0");
            Env(x, @"user\HF_TOKEN", "•••• sha256:3fa1c07e");
        });
        envAfter = Index[^1].F;
        s = Next(s, At(-6, 3, 28), x =>
        {
            var w = x.Find("os", "windows")!;
            w.P["Build"] = "26300.9512";
            var gpu = x.Find("drivers", @"{4d36e968-e325-11ce-bfc1-08002be10318}\0000")!;
            gpu.P["DriverVersion"] = "32.0.16.1123";
            gpu.P["DriverDate"] = "10-9-2026";
            gpu.P["InfPath"] = "oem61.inf";
            Item(x, "services", "NvBroadcast.ContainerLocalSystem", "NVIDIA Broadcast Container",
                ("ImagePath", @"""C:\Program Files\NVIDIA Corporation\NvContainer\nvcontainer.exe"" -s NvBroadcast.ContainerLocalSystem"),
                ("ObjectName", "LocalSystem"), ("Start", "2"), ("Type", "16"));
        }, "startup");
        s = Next(s, At(-3, 12), x => App(x, "PowerToys", "PowerToys (Preview) x64", "0.95.1", "Microsoft Corporation", @"C:\Users\demo\AppData\Local\PowerToys\", hive: "HKCU"));
        Next(s, At(-1, 12), x =>
        {
            Item(x, "autostart", @"HKCU\Run\PowerToys", "PowerToys", ("Command", @"C:\Users\demo\AppData\Local\PowerToys\PowerToys.exe"), ("Enabled", "true"));
            x.Find("services", "SysMain")!.P["Start"] = "3";
        });
        State.LastCheckUtc = DateTime.Today.AddHours(Math.Min(12, DateTime.Now.Hour)).ToUniversalTime();

        Updates.AddRange(
        [
            new(At(-6, 3, 10), Loc.T("Demo.Wu1"), true),
            new(At(-15, 4, 2), Loc.T("Demo.Wu2"), true),
            new(At(-22, 3, 40), Loc.T("Demo.Wu3"), true),
            new(At(-8, 14, 5), "", true, StoreCount: 4),
        ]);
        Updates.Sort((x, y) => y.Utc.CompareTo(x.Utc));

        Power.AddRange(
        [
            new(At(-6, 3, 25), PowerKind.Restart, @"winlogon.exe, NT AUTHORITY\SYSTEM", Loc.T("Demo.Planned")),
            new(At(-6, 21, 41), PowerKind.Bsod, Code: "0x0000009F"),
            new(At(-20, 14, 7), PowerKind.PowerLoss),
            new(At(-2, 8, 12), PowerKind.Wake, WakeSource: Loc.T("Demo.Mouse")),
            new(At(-9, 4, 0), PowerKind.Wake, WakeSource: Loc.T("Demo.WakeTimer"), WakeTask: @"\Microsoft\Windows\UpdateOrchestrator\Schedule Wake To Work"),
            new(At(-11, 23, 30), PowerKind.Shutdown),
        ]);
        Power.Sort((x, y) => y.Utc.CompareTo(x.Utc));
        Boots.AddRange([At(-20, 14, 7), At(-11, 9, 2), At(-6, 3, 26), At(-6, 21, 41)]);
    }

    static Snapshot Next(Snapshot prev, DateTime utc, Action<Snapshot> change, string reason = "scheduled")
    {
        var s = prev.Canonical();
        s.TakenUtc = utc;
        s.Reason = reason;
        change(s);
        Add(s, reason);
        return s;
    }

    static void Add(Snapshot s, string reason)
    {
        s.Reason = reason;
        var file = $"{s.TakenUtc:yyyyMMdd'T'HHmmss'Z'}-{reason}.json.gz";
        Snapshots[file] = s;
        Index.Add(SnapshotStore.Entry(s, file, Index.Count > 0 ? Snapshots[Index[^1].F] : null));
    }

    static void Item(Snapshot s, string cat, string key, string title, params (string Name, string Value)[] props)
    {
        if (!s.Categories.TryGetValue(cat, out var c)) s.Categories[cat] = c = new Category();
        c.Items.RemoveAll(i => i.K == key);
        var item = new Item { K = key, T = title };
        foreach (var (n, v) in props) item.P[n] = v;
        c.Items.Add(item);
    }

    static void App(Snapshot s, string key, string name, string version, string publisher, string location, string hive = "HKLM") =>
        Item(s, "apps", $@"{hive}\{key}", name, ("DisplayName", name), ("DisplayVersion", version), ("Publisher", publisher), ("InstallLocation", location));

    static void Env(Snapshot s, string key, string value) => Item(s, "env", key, key[(key.IndexOf('\\') + 1)..], ("Value", value));

    static Snapshot Base()
    {
        var s = new Snapshot { App = AppInfo.Version };
        Item(s, "os", "windows", "Windows", ("Build", "26300.9457"), ("DisplayVersion", "26H2"));
        Item(s, "os", "bios", "BIOS", ("BIOSReleaseDate", "07/10/2025"), ("BIOSVersion", "1.A0"));

        App(s, "Mozilla Firefox 144.0 (x64 en-US)", "Mozilla Firefox (x64 en-US)", "144.0", "Mozilla", @"C:\Program Files\Mozilla Firefox");
        App(s, "Steam", "Steam", "2.10.91.91", "Valve Corporation", @"C:\Program Files (x86)\Steam", hive: "HKLM32");
        App(s, "Discord", "Discord", "1.0.9212", "Discord Inc.", @"C:\Users\demo\AppData\Local\Discord", hive: "HKCU");
        App(s, "Ollama", "Ollama version 0.12.6", "0.12.6", "Ollama", @"C:\Users\demo\AppData\Local\Programs\Ollama\", hive: "HKCU");
        App(s, "{771FD6B0-FA20-440A-A002-3B3BAC16DC50}_is1", "Microsoft Visual Studio Code (User)", "1.105.1", "Microsoft Corporation", @"C:\Users\demo\AppData\Local\Programs\Microsoft VS Code\", hive: "HKCU");
        App(s, "Git_is1", "Git", "2.51.0", "The Git Development Community", @"C:\Program Files\Git\");
        App(s, "{B5E1E9E3-6B7C-4C1E-9D5E-3A1F0E2D8C41}", "Python 3.13.7 (64-bit)", "3.13.7150.0", "Python Software Foundation", "");

        Item(s, "drivers", @"{4d36e968-e325-11ce-bfc1-08002be10318}\0000", "NVIDIA GeForce RTX 5060 Ti", ("Class", "Display"), ("DriverDate", "7-22-2026"),
            ("DriverDesc", "NVIDIA GeForce RTX 5060 Ti"), ("DriverVersion", "32.0.16.1088"), ("InfPath", "oem55.inf"),
            ("MatchingDeviceId", @"pci\ven_10de&dev_2d04"), ("ProviderName", "NVIDIA"));
        Item(s, "drivers", @"{4d36e96c-e325-11ce-bfc1-08002be10318}\0002", "Realtek High Definition Audio", ("Class", "MEDIA"), ("DriverDate", "5-14-2026"),
            ("DriverDesc", "Realtek High Definition Audio"), ("DriverVersion", "6.0.9814.1"), ("InfPath", "oem12.inf"), ("ProviderName", "Realtek Semiconductor Corp."));
        Item(s, "drivers", @"{4d36e972-e325-11ce-bfc1-08002be10318}\0001", "Intel(R) Ethernet Controller I226-V", ("Class", "Net"), ("DriverDate", "3-2-2026"),
            ("DriverDesc", "Intel(R) Ethernet Controller I226-V"), ("DriverVersion", "2.1.4.3"), ("InfPath", "oem7.inf"), ("ProviderName", "Intel"));

        Item(s, "services", "SysMain", "SysMain", ("ImagePath", @"%systemroot%\system32\svchost.exe -k LocalSystemNetworkRestricted -p"), ("ObjectName", "LocalSystem"), ("Start", "2"), ("Type", "32"));
        Item(s, "services", "Spooler", "Print Spooler", ("ImagePath", @"%SystemRoot%\System32\spoolsv.exe"), ("ObjectName", "LocalSystem"), ("Start", "2"), ("Type", "16"));
        Item(s, "services", "wuauserv", "Windows Update", ("ImagePath", @"%systemroot%\system32\svchost.exe -k netsvcs -p"), ("ObjectName", "LocalSystem"), ("Start", "3"), ("Type", "32"));

        Item(s, "tasks", @"\Microsoft\Windows\UpdateOrchestrator\Schedule Wake To Work", "Schedule Wake To Work", ("Enabled", "true"), ("Exec", "COM {A6BA00FE-40E8-477C-B713-C64A14F23D3B}"),
            ("Triggers", "Time"), ("UserId", "S-1-5-18"), ("WakeToRun", "true"));
        Item(s, "tasks", @"\Ollama\Update", "Update", ("Enabled", "true"), ("Exec", @"C:\Users\demo\AppData\Local\Programs\Ollama\ollama.exe update --quiet"),
            ("Triggers", "Calendar"), ("UserId", "demo"));

        Item(s, "autostart", @"HKCU\Run\Discord", "Discord", ("Command", @"""C:\Users\demo\AppData\Local\Discord\Update.exe"" --processStart Discord.exe"), ("Enabled", "true"));
        Item(s, "autostart", @"HKCU\Run\Steam", "Steam", ("Command", @"""C:\Program Files (x86)\Steam\steam.exe"" -silent"), ("Enabled", "false"));
        Item(s, "autostart", @"Startup\Ollama.lnk", "Ollama", ("Command", @"C:\Users\demo\AppData\Local\Programs\Ollama\ollama app.exe"), ("Enabled", "true"));

        Env(s, @"user\Path", @"C:\Users\demo\AppData\Local\Microsoft\WindowsApps;C:\Users\demo\AppData\Local\Programs\Microsoft VS Code\bin;C:\Users\demo\.dotnet\tools;C:\Users\demo\AppData\Local\Programs\Ollama");
        Env(s, @"machine\Path", @"C:\WINDOWS\system32;C:\WINDOWS;C:\WINDOWS\System32\Wbem;C:\Program Files\Git\cmd;C:\Program Files\dotnet\");
        Env(s, @"machine\CUDA_PATH", @"C:\Program Files\NVIDIA GPU Computing Toolkit\CUDA\v12.6");
        Env(s, @"user\HF_TOKEN", "•••• sha256:9c2e41d0");
        Env(s, @"user\OLLAMA_MODELS", @"D:\models");

        Item(s, "wake", Loc.T("Demo.Mouse"), Loc.T("Demo.Mouse"));
        Item(s, "wake", "Intel(R) Ethernet Controller I226-V", "Intel(R) Ethernet Controller I226-V");
        return s;
    }
}
