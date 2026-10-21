using ChangeLedger.Features;

namespace ChangeLedger.Tests;

/// <summary>Spec §9 tests 1–4 and 6: pure data, no registry, no logs.</summary>
[TestClass]
public class LedgerTests
{
    static Item I(string k, params (string, string)[] p)
    {
        var i = new Item { K = k, T = k };
        foreach (var (n, v) in p) i.P[n] = v;
        return i;
    }

    static Snapshot S(params (string Id, Category C)[] cats)
    {
        var s = new Snapshot { TakenUtc = DateTime.UtcNow };
        foreach (var (id, c) in cats) s.Categories[id] = c;
        return s;
    }

    static Category C(params Item[] items) => new() { Items = [.. items] };

    [TestMethod]
    public void DiffFindsAddedRemovedAndChangedProperties()
    {
        var a = S(("apps", C(I("x", ("DisplayVersion", "1.0"), ("Publisher", "A")), I("gone"))));
        var b = S(("apps", C(I("x", ("DisplayVersion", "1.1"), ("Publisher", "A")), I("new"))));
        var d = DiffEngine.Compare(a, b).Categories.Single(c => c.Id == "apps").Changes;

        Assert.AreEqual(3, d.Count);
        Assert.AreEqual('+', d.Single(c => c.Key == "new").Kind);
        Assert.AreEqual('-', d.Single(c => c.Key == "gone").Kind);
        var x = d.Single(c => c.Key == "x");
        Assert.AreEqual('~', x.Kind);
        Assert.AreEqual(new PropChange("DisplayVersion", "1.0", "1.1"), x.Props.Single());
        Assert.AreEqual(3, DiffEngine.Counts(a, b)["apps"]);
    }

    [TestMethod]
    public void ErrorCategoryIsNoDataNotEverythingRemoved()
    {
        var a = S(("tasks", C(I("\\a"), I("\\b"))));
        var b = S(("tasks", new Category { Status = "error", Error = "Access denied" }));
        var cat = DiffEngine.Compare(a, b).Categories.Single(c => c.Id == "tasks");

        Assert.AreEqual('B', cat.NoDataIn);
        Assert.AreEqual("Access denied", cat.Error);
        Assert.AreEqual(0, cat.Changes.Count);
        Assert.IsFalse(DiffEngine.Counts(a, b).ContainsKey("tasks"));
    }

    [TestMethod]
    public void ItemOrderDoesNotChangeTheHash()
    {
        var a = S(("apps", C(I("b", ("V", "1"), ("A", "2")), I("a"))));
        var b = S(("apps", C(I("a"), I("b", ("A", "2"), ("V", "1")))));
        Assert.AreEqual(a.Hash(), b.Hash());
        b.TakenUtc = b.TakenUtc.AddDays(1);
        b.Reason = "scheduled";
        Assert.AreEqual(a.Hash(), b.Hash(), "time and reason are not state");
        b.Categories["apps"].Items[0].P["X"] = "1";
        Assert.AreNotEqual(a.Hash(), b.Hash());
    }

    [TestMethod]
    public void FeatureUpdateAndFormatChangeAreFlagged()
    {
        var a = S(("os", C(I("windows", ("Build", "26100.9457")))), ("tasks", new Category { V = 1 }));
        var b = S(("os", C(I("windows", ("Build", "26300.1000")))), ("tasks", new Category { V = 2 }));
        var d = DiffEngine.Compare(a, b);
        Assert.IsTrue(d.BuildChanged);
        Assert.IsTrue(d.Categories.Single(c => c.Id == "tasks").FormatChanged);
        Assert.AreEqual("os", d.Categories[0].Id);

        var monthly = S(("os", C(I("windows", ("Build", "26100.9500")))));
        Assert.IsFalse(DiffEngine.Compare(a, monthly).BuildChanged, "a monthly update keeps CurrentBuild");
    }

    [TestMethod]
    public void PathDiffAddInTheMiddleAndRemove()
    {
        var (steps, order) = DiffEngine.PathDiff(@"C:\A;C:\B;C:\C", @"C:\A;C:\X;C:\B");
        CollectionAssert.AreEquivalent(new[] { new PathStep('+', @"C:\X", 2), new PathStep('-', @"C:\C", 3) }, steps);
        Assert.IsFalse(order);
    }

    [TestMethod]
    public void PathDiffOrderChange()
    {
        var (steps, order) = DiffEngine.PathDiff(@"C:\A;C:\B;C:\C", @"C:\C;C:\A;C:\B");
        Assert.AreEqual(0, steps.Count);
        Assert.IsTrue(order);
    }

    [TestMethod]
    public void PathDiffIgnoresCaseTrailingSlashAndEmptyElements()
    {
        var (steps, order) = DiffEngine.PathDiff(@"C:\Tools\;c:\bin;;", @"c:\tools;C:\BIN\");
        Assert.AreEqual(0, steps.Count);
        Assert.IsFalse(order);

        var a = S(("env", C(I(@"user\Path", ("Value", @"C:\Tools\;C:\bin")))));
        var b = S(("env", C(I(@"user\Path", ("Value", @"c:\tools;C:\bin")))));
        Assert.AreEqual(0, DiffEngine.Compare(a, b).All.Count(), "only spelling changed");
    }

    [TestMethod]
    public void PathChangeComesAsStepsNotAsAValuePair()
    {
        var a = S(("env", C(I(@"user\PATH", ("Value", @"C:\A")))));
        var b = S(("env", C(I(@"user\PATH", ("Value", @"C:\Python313\;C:\A")))));
        var c = DiffEngine.Compare(a, b).All.Single();
        Assert.AreEqual(0, c.Props.Count);
        Assert.AreEqual(new PathStep('+', @"C:\Python313\", 1), c.Path!.Single());
    }

    [TestMethod]
    public void UserServiceInstanceDroppedOnlyWhenTheTemplateExists()
    {
        var kept = Collectors.DropUserServiceInstances(["AarSvc", "AarSvc_139057", "Lonely_1a2b3c", "Audiosrv"]).ToList();
        CollectionAssert.AreEqual(new[] { "AarSvc", "Lonely_1a2b3c", "Audiosrv" }, kept);
    }

    [TestMethod]
    public void StartupApprovedBytes()
    {
        Assert.AreEqual("true", Collectors.ApprovedState(new byte[] { 0x02, 0, 0, 0 }));
        Assert.AreEqual("true", Collectors.ApprovedState(new byte[] { 0x06, 0, 0, 0 }));
        Assert.AreEqual("false", Collectors.ApprovedState(new byte[] { 0x03, 0, 0, 0 }));
        Assert.AreEqual("false", Collectors.ApprovedState(new byte[] { 0x07, 0, 0, 0 }));
        Assert.AreEqual("true", Collectors.ApprovedState(null), "no value = enabled");
        Assert.AreEqual("raw:", Collectors.ApprovedState(Array.Empty<byte>()));
        Assert.AreEqual("raw:junk", Collectors.ApprovedState("junk"));
    }

    static readonly byte[] Salt = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16];

    [TestMethod]
    public void SecretNamesAreMaskedPathIsNot()
    {
        Assert.IsTrue(Masking.IsSecretName("HF_TOKEN", []));
        Assert.IsTrue(Masking.IsSecretName("MY_API_KEY", []));
        Assert.IsTrue(Masking.IsSecretName("DB_PASSWORD", []));
        Assert.IsFalse(Masking.IsSecretName("PATH", []));
        Assert.IsFalse(Masking.IsSecretName("PSModulePath", []));
        Assert.IsFalse(Masking.IsSecretName("OLLAMA_MODELS", []));
        Assert.IsTrue(Masking.IsSecretName("ollama_models", ["OLLAMA_MODELS"]), "extraSecretNames, any case");
    }

    [TestMethod]
    public void HashHidesTheValueAndIsStable()
    {
        var h = Masking.Hash("hf_abc123", Salt);
        StringAssert.StartsWith(h, "•••• sha256:");
        Assert.AreEqual(Masking.Hash("hf_abc123", Salt), h);
        Assert.AreNotEqual(Masking.Hash("hf_abc124", Salt), h);
        Assert.IsFalse(h.Contains("abc"));
        Assert.AreNotEqual(Masking.Hash("hf_abc123", [9, 9, 9]), h, "salted");
    }

    [TestMethod]
    public void CommandLineArgumentsAreMasked()
    {
        var m = Masking.CommandLine(@"C:\bot.exe --token=abc123 -ApiKey:xyz --verbose", Salt)!;
        Assert.IsFalse(m.Contains("abc123"));
        Assert.IsFalse(m.Contains("xyz"));
        StringAssert.Contains(m, "--token=•••• sha256:");
        StringAssert.Contains(m, "--verbose");
        Assert.AreEqual(@"C:\Program Files\KeePass\KeePass.exe", Masking.CommandLine(@"C:\Program Files\KeePass\KeePass.exe", Salt));
    }

    static readonly DateTime Now = new(2026, 10, 21, 12, 0, 0, DateTimeKind.Utc);
    static string F(int daysAgo, string reason = "scheduled") => $"{Now.AddDays(-daysAgo):yyyyMMdd'T'HHmmss'Z'}-{reason}.json.gz";

    [TestMethod]
    public void RetentionByDays()
    {
        var files = new[] { (F(0), 10L), (F(1), 10L), (F(100), 10L), (F(400), 10L), (F(500), 10L) };
        CollectionAssert.AreEquivalent(new[] { F(400), F(500) }, SnapshotStore.PickForDeletion(files, Now, 365, 1_000_000).ToList());
    }

    [TestMethod]
    public void RetentionBySizeDeletesTheOldestFirst()
    {
        var files = new[] { (F(0), 100L), (F(1), 100L), (F(2), 100L), (F(3), 100L) };
        CollectionAssert.AreEqual(new[] { F(3), F(2) }, SnapshotStore.PickForDeletion(files, Now, 365, 250).ToList());
    }

    [TestMethod]
    public void RetentionKeepsTheTwoNewestAndForeignFiles()
    {
        var files = new[] { (F(900), 100L), (F(800, "manual") .Replace(".json", "-2.json"), 100L), (F(700), 100L), ("notes.txt", 100L), ("20261021-backup.json.gz", 100L) };
        var doomed = SnapshotStore.PickForDeletion(files, Now, 1, 0).ToList();
        CollectionAssert.AreEqual(new[] { F(900) }, doomed);
    }
}
