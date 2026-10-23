using System.Collections;
using System.Globalization;
using System.IO;
using System.Resources;
using System.Windows.Input;
using ChangeLedger.Shell;

namespace ChangeLedger.Tests;

[TestClass]
public class ShellTests
{
    public TestContext TestContext { get; set; } = null!;

    string NewDir()
    {
        var d = Path.Combine(Path.GetTempPath(), "ChangeLedger.Tests", TestContext.TestName + "-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(d);
        return d;
    }

    [TestMethod]
    public void CorruptSettingsGiveDefaultsAndAreKept()
    {
        var file = Path.Combine(NewDir(), "settings.json");
        File.WriteAllText(file, "{ \"theme\": \"dark\", ");
        var s = Settings.Load(file);
        Assert.AreEqual("system", s.Theme);
        Assert.IsFalse(File.Exists(file));
        Assert.HasCount(1, Directory.GetFiles(Path.GetDirectoryName(file)!, "settings.bad-*.json"));
    }

    [TestMethod]
    public void CorruptSettingsFallBackToBak()
    {
        var file = Path.Combine(NewDir(), "settings.json");
        AtomicFile.WriteAllText(file, "{ \"theme\": \"dark\" }");
        AtomicFile.WriteAllText(file, "{ \"theme\": \"light\" }"); // first version becomes .bak
        File.WriteAllText(file, "garbage");
        Assert.AreEqual("dark", Settings.Load(file).Theme);
    }

    [TestMethod]
    public void AtomicWriteLeavesNoTmp()
    {
        var file = Path.Combine(NewDir(), "state.json");
        AtomicFile.WriteAllText(file, "1");
        AtomicFile.WriteAllText(file, "2");
        Assert.AreEqual("2", File.ReadAllText(file));
        Assert.AreEqual("1", File.ReadAllText(file + ".bak"));
        Assert.IsFalse(File.Exists(file + ".tmp"));
    }

    [TestMethod]
    [DataRow("v0.2.0", "0.1.0", true)]
    [DataRow("v0.1.0", "0.1.0", false)]
    [DataRow("v0.1.0", "0.2.0", false)]
    [DataRow("v0.1.0", "0.1.0-beta.2", true)]
    [DataRow("v0.0.1", "0.0.0-dev", true)]
    [DataRow("v1.10.0", "1.9.0", true)]
    [DataRow("nonsense", "0.1.0", false)]
    public void UpdateVersionCompare(string tag, string current, bool newer) => Assert.AreEqual(newer, Updates.IsNewer(tag, current));

    [TestMethod]
    public void DevInstancesLiveApartFromTheInstalledCopy()
    {
        const string roaming = @"C:\Users\u\AppData\Roaming", local = @"C:\Users\u\AppData\Local";
        Assert.AreEqual(("", $@"{roaming}\ChangeLedger", $@"{local}\ChangeLedger"), AppInfo.Resolve(false, null, roaming, local));          // Release: the real app
        Assert.AreEqual(("-dev", $@"{roaming}\ChangeLedger-dev", $@"{local}\ChangeLedger-dev"), AppInfo.Resolve(true, null, roaming, local)); // Debug
        var a = AppInfo.Resolve(false, @"C:\Demo\data\", roaming, local);
        Assert.AreEqual((@"C:\Demo\data", @"C:\Demo\data\local"), (a.Data, a.Local));                                            // CHANGELEDGER_DATA_DIR
        Assert.AreEqual(a.Suffix, AppInfo.Resolve(true, @"c:\demo\DATA", roaming, local).Suffix);                                // same folder, same instance
        Assert.AreNotEqual(a.Suffix, AppInfo.Resolve(false, @"C:\Demo\data2", roaming, local).Suffix);
        Assert.StartsWith("-", a.Suffix);
        // this test run itself is a dev instance (TestSetup): it can never touch the installed copy
        Assert.IsTrue(AppInfo.IsDev);
        Assert.IsFalse(Install.IsInstalled);
    }

    [TestMethod]
    public void InstalledExeIsComparedAsWindowsDoes()
    {
        Assert.IsTrue(Install.IsAt(Install.Exe.ToUpperInvariant()));
        Assert.IsTrue(Install.IsAt(Path.Combine(Install.Dir, "sub", "..", "ChangeLedger.exe")));
        Assert.IsFalse(Install.IsAt(Path.Combine(Install.Dir + "-dev", "ChangeLedger.exe")));
        Assert.IsFalse(Install.IsAt(Path.Combine(Install.Dir, "ChangeLedger-1.0.1-win-x64.exe")));
    }

    [TestMethod]
    public void InstallerPlans()
    {
        const string downloaded = @"C:\Tools\ChangeLedger.exe";
        Assert.AreEqual(Installer.Plan.Run, Installer.Decide(Install.Exe, "1.0.2", "1.0.2", skipped: false));    // the installed copy itself
        Assert.AreEqual(Installer.Plan.Install, Installer.Decide(downloaded, "1.0.2", null, skipped: false));
        Assert.AreEqual(Installer.Plan.Run, Installer.Decide(downloaded, "1.0.2", null, skipped: true));         // "Run without installing" before
        Assert.AreEqual(Installer.Plan.Update, Installer.Decide(downloaded, "1.0.2", "1.0.1", skipped: false));
        Assert.AreEqual(Installer.Plan.Update, Installer.Decide(downloaded, "1.0.0", "1.0.0-rc.1", skipped: false));
        Assert.AreEqual(Installer.Plan.ShowInstalled, Installer.Decide(downloaded, "1.0.1", "1.0.1", skipped: false));
        Assert.AreEqual(Installer.Plan.ShowInstalled, Installer.Decide(downloaded, "1.0.1", "1.1.0", skipped: false)); // never downgrade
    }

    [TestMethod]
    public void RuAndEnStringsHaveTheSameKeys()
    {
        var rm = new ResourceManager("ChangeLedger.Resources.Strings", typeof(Loc).Assembly);
        static HashSet<string> Keys(ResourceSet? set) => set!.Cast<DictionaryEntry>().Select(e => (string)e.Key).ToHashSet();
        var en = Keys(rm.GetResourceSet(CultureInfo.InvariantCulture, true, false));
        var ru = Keys(rm.GetResourceSet(CultureInfo.GetCultureInfo("ru"), true, false));
        CollectionAssert.AreEquivalent(en.Order().ToList(), ru.Order().ToList());
    }

    [TestMethod]
    public void HotkeyRoundTrip()
    {
        var h = new Hotkey(ModifierKeys.Control | ModifierKeys.Windows | ModifierKeys.Alt, Key.Left);
        Assert.AreEqual("Ctrl+Win+Alt+Left", h.ToString());
        Assert.AreEqual(h, Hotkey.Parse("ctrl + win + alt + left"));
        Assert.IsNull(Hotkey.Parse("Hyper+W"));
        Assert.IsNull(Hotkey.Parse(""));
    }
}
