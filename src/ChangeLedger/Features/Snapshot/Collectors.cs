using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using ChangeLedger.Shell;
using Microsoft.Win32;

namespace ChangeLedger.Features;

/// <summary>
/// One static method per category (spec §5). Every collector runs in its own try/catch: a failure gives Status="error",
/// never an empty list that would read as "everything removed". Bump V when a collector's output format changes.
/// </summary>
public static partial class Collectors
{
    public sealed record Context(byte[] Salt, IReadOnlyList<string> ExtraSecrets);
    public sealed record Def(string Id, int V, Func<Context, List<Item>> Collect);

    /// <summary>Display order too: os first, so a feature update is seen before its thousands of changes.</summary>
    public static readonly Def[] All =
    [
        new("os", 1, _ => Os()),
        new("apps", 1, _ => Apps()),
        new("drivers", 1, _ => Drivers()),
        new("services", 1, Services),
        new("tasks", 1, Tasks),
        new("autostart", 1, Autostart),
        new("firewall", 1, _ => Firewall()),
        new("env", 1, Env),
        new("network", 1, _ => Network()),
        new("appx", 1, _ => Appx()),
        new("wake", 1, _ => Wake()),
    ];

    /// <summary>Cancellation is checked between collectors (app exit, 5 min limit).</summary>
    public static Snapshot Take(string reason, Context ctx, CancellationToken ct)
    {
        var s = new Snapshot { TakenUtc = DateTime.UtcNow, Reason = reason, App = AppInfo.Version };
        foreach (var d in All)
        {
            ct.ThrowIfCancellationRequested();
            try { s.Categories[d.Id] = new Category { V = d.V, Items = Unique(d.Collect(ctx)) }; }
            catch (Exception e)
            {
                Log.Warn($"collector {d.Id} failed: {e.GetType().Name} 0x{e.HResult:X8}"); // the message may hold paths
                s.Categories[d.Id] = new Category { V = d.V, Status = "error", Error = e.Message };
            }
        }
        return s;
    }

    /// <summary>The diff matches items by key, so a repeated key gets "#2", "#3".</summary>
    static List<Item> Unique(List<Item> items)
    {
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var i in items)
            if (seen.TryGetValue(i.K, out var n)) { seen[i.K] = ++n; i.K = $"{i.K}#{n}"; }
            else seen[i.K] = 1;
        return items;
    }

    static List<Item> Os()
    {
        using var nt = Open(RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
        using var bios = Open(RegistryHive.LocalMachine, @"HARDWARE\DESCRIPTION\System\BIOS");
        return
        [
            Make("windows", "Windows", ("DisplayVersion", Str(nt, "DisplayVersion")), ("Build", $"{Str(nt, "CurrentBuild")}.{Str(nt, "UBR")}")),
            Make("bios", "BIOS", ("BIOSVersion", Str(bios, "BIOSVersion")), ("BIOSReleaseDate", Str(bios, "BIOSReleaseDate"))),
        ];
    }

    /// <summary>Entries without DisplayName are updates and components that "Installed apps" hides too.</summary>
    static List<Item> Apps() =>
    [
        .. UninstallEntries.List().Where(e => !string.IsNullOrWhiteSpace(e.DisplayName)).Select(e => Make($@"{e.Hive}\{e.SubKey}", e.DisplayName!,
            ("DisplayName", e.DisplayName), ("DisplayVersion", e.DisplayVersion), ("Publisher", e.Publisher),
            ("InstallLocation", e.InstallLocation), ("SystemComponent", e.SystemComponent ? "1" : null))),
    ];

    /// <summary>Class keys stay when a USB device is unplugged, so this is quieter than WMI.</summary>
    static List<Item> Drivers()
    {
        var list = new List<Item>();
        using var classes = Open(RegistryHive.LocalMachine, @"SYSTEM\CurrentControlSet\Control\Class") ?? throw new IOException("Class key is missing");
        foreach (var guid in classes.GetSubKeyNames())
            Safe(() =>
            {
                using var g = classes.OpenSubKey(guid);
                if (g is null) return;
                var cls = Str(g, "Class");
                foreach (var sub in g.GetSubKeyNames().Where(n => n.Length == 4 && n.All(char.IsAsciiDigit)))
                    Safe(() =>
                    {
                        using var k = g.OpenSubKey(sub);
                        var desc = Str(k, "DriverDesc");
                        var version = Str(k, "DriverVersion");
                        if (k is null || desc is null && version is null) return;
                        list.Add(Make($@"{guid}\{sub}", desc ?? sub, ("Class", cls), ("DriverDesc", desc), ("DriverVersion", version),
                            ("DriverDate", Str(k, "DriverDate")), ("ProviderName", Str(k, "ProviderName")), ("InfPath", Str(k, "InfPath")),
                            ("MatchingDeviceId", Str(k, "MatchingDeviceId"))));
                    });
            });
        return list;
    }

    static List<Item> Services(Context ctx)
    {
        var list = new List<Item>();
        using var root = Open(RegistryHive.LocalMachine, @"SYSTEM\CurrentControlSet\Services") ?? throw new IOException("Services key is missing");
        foreach (var name in DropUserServiceInstances(root.GetSubKeyNames()))
            Safe(() =>
            {
                using var k = root.OpenSubKey(name);
                if (k?.GetValue("Type") is not int type) return; // event log sources and other non-service keys
                var display = Str(k, "DisplayName");
                // Title is localized; P keeps the raw value, so a language switch is not a change
                var title = display is null ? name : display.StartsWith('@') ? Native.IndirectString(display) ?? name : display;
                list.Add(Make(name, title, ("Type", type.ToString()), ("Start", Str(k, "Start")), ("DelayedAutostart", Str(k, "DelayedAutostart")),
                    ("ImagePath", Masking.CommandLine(Str(k, "ImagePath"), ctx.Salt)), ("ObjectName", Str(k, "ObjectName")), ("DisplayName", display)));
            });
        return list;
    }

    /// <summary>Per-user service instances "AarSvc_139057" get a new suffix at every sign-in: dropped when the template "AarSvc" exists.</summary>
    public static IEnumerable<string> DropUserServiceInstances(IReadOnlyCollection<string> names)
    {
        var all = names.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return names.Where(n => UserServiceInstance().Match(n) is not { Success: true } m || !all.Contains(m.Groups[1].Value));
    }

    static List<Item> Tasks(Context ctx)
    {
        var list = new List<Item>();
        dynamic svc = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service", throwOnError: true)!)!;
        svc.Connect();
        void Walk(dynamic folder)
        {
            Safe(() =>
            {
                foreach (var t in folder.GetTasks(1)) // 1 = TASK_ENUM_HIDDEN
                    Safe(() => list.Add(TaskItem((string)t.Path, (string)t.Name, (bool)t.Enabled, (string)t.Xml, ctx.Salt)));
            });
            Safe(() => { foreach (var f in folder.GetFolders(0)) Walk(f); });
        }
        Walk(svc.GetFolder(@"\"));
        return list;
    }

    static readonly XNamespace TaskNs = "http://schemas.microsoft.com/windows/2004/02/mit/task";

    /// <summary>Everything from the task XML; LastRunTime, NextRunTime and State change all the time and are not taken.</summary>
    internal static Item TaskItem(string path, string name, bool enabled, string xml, byte[] salt)
    {
        var x = XDocument.Parse(xml).Root!;
        string? V(params string[] p) => p.Aggregate((XElement?)x, (e, n) => e?.Element(TaskNs + n))?.Value;
        var actions = x.Element(TaskNs + "Actions")?.Elements().Select(a => a.Name.LocalName switch
        {
            "Exec" => $"{a.Element(TaskNs + "Command")?.Value} {a.Element(TaskNs + "Arguments")?.Value}".Trim(),
            "ComHandler" => $"COM {a.Element(TaskNs + "ClassId")?.Value}",
            var other => other,
        });
        var triggers = x.Element(TaskNs + "Triggers")?.Elements().Select(t => t.Name.LocalName.Replace("Trigger", "")).Distinct();
        return Make(path, name, ("Enabled", enabled ? "true" : "false"), ("Author", V("RegistrationInfo", "Author")),
            ("Exec", Masking.CommandLine(actions is null ? null : string.Join(" | ", actions), salt)),
            ("Triggers", triggers is null ? null : string.Join(", ", triggers)),
            ("RunLevel", V("Principals", "Principal", "RunLevel")),
            ("UserId", V("Principals", "Principal", "UserId") ?? V("Principals", "Principal", "GroupId")),
            ("WakeToRun", V("Settings", "WakeToRun") == "true" ? "true" : null));
    }

    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string RunOnceKey = @"Software\Microsoft\Windows\CurrentVersion\RunOnce";
    const string Wow = @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\";
    const string Approved = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\";

    static List<Item> Autostart(Context ctx)
    {
        var list = new List<Item>();
        void Run(string place, RegistryHive hive, string path, string? approved)
        {
            using var k = Open(hive, path);
            using var a = approved is null ? null : Open(hive, Approved + approved);
            foreach (var name in k?.GetValueNames() ?? [])
                list.Add(Make($@"{place}\{name}", name, ("Command", Masking.CommandLine(Str(k, name), ctx.Salt)),
                    ("Enabled", approved is null ? null : ApprovedState(a?.GetValue(name)))));
        }
        Run(@"HKCU\Run", RegistryHive.CurrentUser, RunKey, "Run");
        Run(@"HKCU\RunOnce", RegistryHive.CurrentUser, RunOnceKey, null);
        Run(@"HKLM\Run", RegistryHive.LocalMachine, RunKey, "Run");
        Run(@"HKLM\RunOnce", RegistryHive.LocalMachine, RunOnceKey, null);
        Run(@"HKLM32\Run", RegistryHive.LocalMachine, Wow + "Run", "Run32");
        Run(@"HKLM32\RunOnce", RegistryHive.LocalMachine, Wow + "RunOnce", null);

        dynamic? shell = null;
        void Folder(string place, Environment.SpecialFolder folder, RegistryHive hive)
        {
            var dir = Environment.GetFolderPath(folder);
            if (!Directory.Exists(dir)) return;
            using var a = Open(hive, Approved + "StartupFolder");
            foreach (var file in Directory.GetFiles(dir).Where(f => !Path.GetFileName(f).Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)))
            {
                var command = file;
                if (file.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
                    Safe(() =>
                    {
                        shell ??= Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell", throwOnError: true)!)!;
                        var lnk = shell.CreateShortcut(file);
                        command = $"{lnk.TargetPath} {lnk.Arguments}".Trim();
                    });
                var name = Path.GetFileName(file);
                list.Add(Make($@"{place}\{name}", Path.GetFileNameWithoutExtension(file), ("Command", Masking.CommandLine(command, ctx.Salt)),
                    ("Enabled", ApprovedState(a?.GetValue(name)))));
            }
        }
        Folder("Startup", Environment.SpecialFolder.Startup, RegistryHive.CurrentUser);
        Folder("CommonStartup", Environment.SpecialFolder.CommonStartup, RegistryHive.LocalMachine);
        return list;
    }

    /// <summary>
    /// StartupApproved (undocumented): no value = enabled, even first byte = enabled (02, 06), odd = disabled by the user (03, 07).
    /// Anything else is kept as raw hex instead of guessing.
    /// </summary>
    public static string ApprovedState(object? value) => value switch
    {
        null => "true",
        byte[] { Length: > 0 } b => b[0] % 2 == 0 ? "true" : "false",
        byte[] b => "raw:" + Convert.ToHexString(b),
        var other => "raw:" + other,
    };

    /// <summary>Raw values (%VARS% not expanded); secret names are masked, PATH is split by the diff.</summary>
    static List<Item> Env(Context ctx)
    {
        var list = new List<Item>();
        void Read(string scope, RegistryHive hive, string path)
        {
            using var k = Open(hive, path) ?? throw new IOException($"{scope} environment key is missing");
            foreach (var name in k.GetValueNames().Where(n => n.Length > 0))
            {
                var value = Str(k, name) ?? "";
                list.Add(Make($@"{scope}\{name}", name, ("Value", Masking.IsSecretName(name, ctx.ExtraSecrets) ? Masking.Hash(value, ctx.Salt) : value)));
            }
        }
        Read("user", RegistryHive.CurrentUser, "Environment");
        Read("machine", RegistryHive.LocalMachine, @"SYSTEM\CurrentControlSet\Control\Session Manager\Environment");
        return list;
    }

    /// <summary>Local rules and GPO rules from the registry: the value name is the rule's stable ID.</summary>
    static List<Item> Firewall()
    {
        var list = new List<Item>();
        void Read(string scope, string path)
        {
            using var k = Open(RegistryHive.LocalMachine, path);
            foreach (var id in k?.GetValueNames() ?? [])
            {
                var p = FirewallRule(Str(k, id) ?? "");
                var name = p.GetValueOrDefault("Name") ?? id;
                var item = Make($@"{scope}\{id}", name.StartsWith('@') ? Native.IndirectString(name) ?? id : name);
                foreach (var (n, v) in p) item.P[n] = v;
                list.Add(item);
            }
        }
        Read("local", @"SYSTEM\CurrentControlSet\Services\SharedAccess\Parameters\FirewallPolicy\FirewallRules");
        Read("gpo", @"SOFTWARE\Policies\Microsoft\WindowsFirewall\FirewallRules");
        return list;
    }

    /// <summary>
    /// "v2.33|Action=Allow|Active=TRUE|Dir=Out|RA4=LocalSubnet|RA4=10.0.0.0/8|Name=@FirewallAPI.dll,-32765|Desc=…|" → fields;
    /// repeated keys are joined, the long descriptive Desc and EmbedCtxt are left out (they don't change what the rule does).
    /// </summary>
    public static SortedDictionary<string, string> FirewallRule(string data)
    {
        var p = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var part in data.Split('|', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = part.IndexOf('=');
            if (eq <= 0) continue; // the "v2.33" version marker
            var (n, v) = (part[..eq], part[(eq + 1)..]);
            if (n is "Desc" or "EmbedCtxt") continue;
            p[n] = p.TryGetValue(n, out var old) ? $"{old}, {v}" : v;
        }
        return p;
    }

    /// <summary>hosts entries, the user's proxy and the DNS servers of each adapter.</summary>
    static List<Item> Network()
    {
        var list = new List<Item>();
        var hosts = Path.Combine(Environment.SystemDirectory, @"drivers\etc\hosts");
        if (File.Exists(hosts))
            foreach (var line in File.ReadAllLines(hosts).Select(l => l.Split('#')[0].Trim()).Where(l => l.Length > 0))
            {
                var parts = line.Split((char[])[' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
                foreach (var host in parts.Skip(1)) list.Add(Make($@"hosts\{host}", host, ("Address", parts[0])));
            }
        using (var k = Open(RegistryHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Internet Settings"))
            list.Add(Make("proxy", "Proxy", ("ProxyEnable", Str(k, "ProxyEnable")), ("ProxyServer", Str(k, "ProxyServer")),
                ("ProxyOverride", Str(k, "ProxyOverride")), ("AutoConfigURL", Str(k, "AutoConfigURL"))));
        using var ifaces = Open(RegistryHive.LocalMachine, @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces");
        foreach (var guid in ifaces?.GetSubKeyNames() ?? [])
            Safe(() =>
            {
                using var k = ifaces!.OpenSubKey(guid);
                var dns = Str(k, "NameServer");
                var dhcp = Str(k, "DhcpNameServer");
                if (string.IsNullOrEmpty(dns) && string.IsNullOrEmpty(dhcp)) return; // adapters that never had DNS
                using var c = Open(RegistryHive.LocalMachine, $@"SYSTEM\CurrentControlSet\Control\Network\{{4D36E972-E325-11CE-BFC1-08002BE10318}}\{guid}\Connection");
                list.Add(Make($@"dns\{guid}", Str(c, "Name") ?? guid, ("NameServer", dns), ("DhcpNameServer", dhcp)));
            });
        return list;
    }

    /// <summary>Store packages of the current user; the key leaves the version out, so an update reads as a version change.</summary>
    static List<Item> Appx()
    {
        var list = new List<Item>();
        using var root = Open(RegistryHive.CurrentUser, @"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\Repository\Packages");
        foreach (var full in root?.GetSubKeyNames() ?? [])
            Safe(() =>
            {
                if (AppxKey(full) is not var (key, version)) return;
                using var k = root!.OpenSubKey(full);
                var display = Str(k, "DisplayName");
                var title = display is null ? key.Split('_')[0] : display.StartsWith('@') ? Native.IndirectString(display) ?? key.Split('_')[0] : display;
                list.Add(Make(key, title, ("Version", version)));
            });
        return list;
    }

    /// <summary>"Microsoft.WindowsCalculator_11.2405.2.0_x64__8wekyb3d8bbwe" → ("Microsoft.WindowsCalculator_x64__8wekyb3d8bbwe", "11.2405.2.0").</summary>
    public static (string Key, string Version)? AppxKey(string fullName)
    {
        var p = fullName.Split('_');
        return p.Length == 5 ? ($"{p[0]}_{p[2]}_{p[3]}_{p[4]}", p[1]) : null;
    }

    /// <summary>
    /// Devices that may wake the PC. /waketimers needs admin, so task timers come from tasks with WakeToRun.
    /// </summary>
    // ponytail: with no armed device powercfg prints a localized "NONE" line that becomes one item; fine, it is stable
    static List<Item> Wake()
    {
        var (code, output) = ConsoleTool.Run(Path.Combine(Environment.SystemDirectory, "powercfg.exe"), ["/devicequery", "wake_armed"], TimeSpan.FromSeconds(30));
        if (code != 0) throw new IOException($"powercfg exit code {code}");
        return [.. output.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Select(line => Make(line, line))];
    }

    static Item Make(string key, string title, params (string Name, string? Value)[] props)
    {
        var item = new Item { K = key, T = title };
        foreach (var (n, v) in props)
            if (!string.IsNullOrEmpty(v)) item.P[n] = v;
        return item;
    }

    static RegistryKey? Open(RegistryHive hive, string path)
    {
        using var b = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
        return b.OpenSubKey(path);
    }

    /// <summary>Raw registry value as text: numbers in decimal, binary as hex, multi-strings joined.</summary>
    static string? Str(RegistryKey? k, string name) => k?.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames) switch
    {
        null => null,
        string s => s.TrimEnd('\0'),
        byte[] b => Convert.ToHexString(b),
        string[] a => string.Join("; ", a),
        var v => v.ToString(),
    };

    /// <summary>One unreadable key or task folder doesn't hide the rest.</summary>
    static void Safe(Action a)
    {
        try { a(); }
        catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException or IOException or COMException or System.Xml.XmlException) { }
    }

    [GeneratedRegex(@"^(.+)_[0-9a-fA-F]{4,}$")] private static partial Regex UserServiceInstance();
}
