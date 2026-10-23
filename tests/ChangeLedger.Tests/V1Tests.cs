using ChangeLedger.Features;
using static ChangeLedger.Features.EventHistory;

namespace ChangeLedger.Tests;

/// <summary>v1.0: firewall rules, Store package keys, the user's "Hide such" rules, Defender exclusions and crashes from the logs.</summary>
[TestClass]
public class V1Tests
{
    [TestMethod]
    public void FirewallRuleFields()
    {
        var p = Collectors.FirewallRule("v2.33|Action=Allow|Active=TRUE|Dir=Out|RA4=LocalSubnet|RA4=10.0.0.0/8|Name=@FirewallAPI.dll,-32765|Desc=@FirewallAPI.dll,-32768|EmbedCtxt=x|");
        Assert.AreEqual("Allow", p["Action"]);
        Assert.AreEqual("LocalSubnet, 10.0.0.0/8", p["RA4"], "repeated keys are joined");
        Assert.AreEqual("@FirewallAPI.dll,-32765", p["Name"]);
        Assert.IsFalse(p.ContainsKey("Desc"));
        Assert.IsFalse(p.ContainsKey("EmbedCtxt"));
        Assert.IsFalse(p.Keys.Any(k => k.StartsWith('v')), "the version marker is not a field");
    }

    [TestMethod]
    public void AppxKeyLeavesTheVersionOut()
    {
        Assert.AreEqual(("Microsoft.WindowsCalculator_x64__8wekyb3d8bbwe", "11.2405.2.0"), Collectors.AppxKey("Microsoft.WindowsCalculator_11.2405.2.0_x64__8wekyb3d8bbwe"));
        Assert.IsNull(Collectors.AppxKey("NotAPackage"));
    }

    [TestMethod]
    public void HideSuchRuleHidesExactlyThatKey()
    {
        static Snapshot S(params string[] keys)
        {
            var s = new Snapshot();
            s.Categories["firewall"] = new Category { Items = [.. keys.Select(k => new Item { K = k, T = k })] };
            return s;
        }
        var a = S();
        var b = S(@"local\{1}", @"local\{1}x");
        try
        {
            var first = DiffEngine.Compare(a, b).All.Single(c => c.Key == @"local\{1}");
            DiffEngine.SetUserRules([DiffEngine.RuleFor(first), "bad line", "apps|(unclosed"]);
            var d = DiffEngine.Compare(a, b).All.ToList();
            Assert.IsTrue(d.Single(c => c.Key == @"local\{1}").Noise);
            Assert.IsFalse(d.Single(c => c.Key == @"local\{1}x").Noise, "the rule is anchored");
            Assert.AreEqual(1, DiffEngine.Counts(a, b)["firewall"]);
        }
        finally { DiffEngine.SetUserRules([]); }
    }

    static readonly DateTime T0 = new(2026, 10, 15, 12, 0, 0, DateTimeKind.Utc);
    static Ev E(int minute, string provider, int id, params (string, string)[] data) => new(T0.AddMinutes(minute), provider, id, data.ToDictionary(d => d.Item1, d => d.Item2));

    [TestMethod]
    public void DefenderExclusionsAndCrashes()
    {
        var j = Journal.Of(
        [
            E(0, Defender, 5007, ("Old Value", ""), ("New Value", @"HKLM\SOFTWARE\Microsoft\Windows Defender\Exclusions\Paths\C:\Games = 0x0")),
            E(1, Defender, 5007, ("Old Value", @"HKLM\SOFTWARE\Microsoft\Windows Defender\Exclusions\Processes\x.exe = 0x0"), ("New Value", "")),
            E(2, AppError, 1000, ("AppName", "dota2.exe"), ("AppVersion", "7.39"), ("ModuleName", "nvwgf2umx.dll"), ("ExceptionCode", "c0000005")),
            E(3, Display, 4101),
            E(4, KernelGeneral, 12),
        ]);
        CollectionAssert.AreEqual(new[] { new DefenderChange(T0, '+', @"Paths\C:\Games"), new DefenderChange(T0.AddMinutes(1), '-', @"Processes\x.exe") }, j.Defender);
        Assert.AreEqual(new AppCrash(T0.AddMinutes(2), false, "dota2.exe 7.39", "nvwgf2umx.dll · 0xc0000005"), j.Crashes[0]);
        Assert.IsTrue(j.Crashes[1].Gpu);
        Assert.AreEqual(T0.AddMinutes(4), j.Boots.Single());

        var later = j.Between(T0.AddMinutes(2), T0.AddMinutes(10));
        Assert.AreEqual(0, later.Defender.Count);
        Assert.AreEqual(2, later.Crashes.Count);
        Assert.IsNull(Journal.DefenderOf(E(0, Defender, 5007, ("Old Value", ""), ("New Value", @"HKLM\SOFTWARE\Microsoft\Windows Defender\Signature Updates\x = 1"))));
    }

    [TestMethod]
    public void DevTaskPassesItsDataFolderOn()
    {
        // tests are a CHANGELEDGER_DATA_DIR instance (TestSetup): the scheduler must start the exe with that folder, not the real one
        var xml = TaskRegistration.Xml(@"C:\x\ChangeLedger.exe", "12:00", "S-1-5-21-1");
        StringAssert.Contains(xml, $"<Arguments>--snapshot --reason scheduled --data-dir &quot;{Shell.AppInfo.DataDir}&quot;</Arguments>");
        Assert.AreNotEqual("", Shell.AppInfo.Suffix);
    }
}
