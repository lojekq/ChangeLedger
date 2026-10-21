using System.IO;
using TrayApp.Shell;

namespace TrayApp.Tests;

[TestClass]
public class PathsTests
{
    [TestMethod]
    public void IsUnderComparesWholeFolderNames()
    {
        Assert.IsTrue(Paths.IsUnder(@"C:\dev", @"C:\dev\x\a.exe"));
        Assert.IsTrue(Paths.IsUnder(@"C:\dev\", @"C:\dev"));          // the root itself counts
        Assert.IsFalse(Paths.IsUnder(@"C:\dev", @"C:\dev2\a.exe"));
        Assert.IsTrue(Paths.IsUnder(@"c:\DEV", @"C:\dev\a.exe"));
        Assert.IsFalse(Paths.IsUnder(@"C:\dev", @"C:\dev\..\dev2\a.exe"));
        Assert.IsTrue(Paths.IsUnder(@"C:\dev", @"C:\other\..\dev\a.exe"));
        Assert.IsTrue(Paths.IsUnder(@"C:\", @"C:\dev\a.exe"));
        Assert.IsTrue(Paths.IsUnder(@"C:\Games\dota 2 beta", @"\\?\C:\Games\dota 2 beta\game\bin\dota2.exe"));
        Assert.IsFalse(Paths.IsUnder(@"C:\Games\dota 2 beta", @"C:\Games\dota 2 beta2\dota2.exe"));
        Assert.IsTrue(Paths.IsUnder(@"\\srv\share", @"\\?\UNC\srv\share\x.exe"));
    }

    [TestMethod]
    public void DownloadsIsAFullPath() => Assert.IsTrue(Path.IsPathFullyQualified(Paths.Downloads));
}
