using TrayApp.Shell;

namespace TrayApp.Tests;

[TestClass]
public class ArgsTests
{
    [TestMethod]
    public void ParsesShotsCommandLine()
    {
        var a = Args.Parse(["--demo", "--lang", "ru", "--theme", "dark", "--page", "settings", "--size", "1100x700", "--screenshot", @"C:\r\docs\x.png"]);
        Assert.IsTrue(a.Demo);
        Assert.AreEqual("ru", a.Lang);
        Assert.AreEqual("dark", a.Theme);
        Assert.AreEqual("settings", a.Page);
        Assert.AreEqual((1100, 700), (a.Width, a.Height));
        Assert.AreEqual(@"C:\r\docs\x.png", a.Screenshot);
        Assert.IsEmpty(a.Unknown);
    }

    [TestMethod]
    public void BadValuesAndUnknownFlagsAreCollected()
    {
        var a = Args.Parse(["--lang", "de", "--size", "big", "--wat", "--background", "--rect-file"]);
        Assert.IsNull(a.Lang);
        Assert.AreEqual(0, a.Width);
        Assert.IsTrue(a.Background);
        CollectionAssert.AreEqual(new[] { "--lang", "--size", "--wat", "--rect-file" }, a.Unknown);
    }
}
