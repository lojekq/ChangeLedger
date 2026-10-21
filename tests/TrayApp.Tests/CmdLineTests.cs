using System.Diagnostics;
using System.IO;
using TrayApp.Shell;

namespace TrayApp.Tests;

[TestClass]
public class CmdLineTests
{
    [TestMethod]
    public void SplitExeKeepsTheTailAsIs()
    {
        Assert.AreEqual((@"C:\Program Files\App\app.exe", @"--port 8081 -m ""a b.gguf"" "),
            CmdLine.SplitExe(@"""C:\Program Files\App\app.exe"" --port 8081 -m ""a b.gguf"" "));
        Assert.AreEqual((@"C:\tools\app.exe", "-x  y"), CmdLine.SplitExe(@"C:\tools\app.exe -x  y"));
        Assert.AreEqual((@"C:\tools\app.exe", ""), CmdLine.SplitExe(@"C:\tools\app.exe"));
        Assert.AreEqual((@"C:\Program Files\app.exe", ""), CmdLine.SplitExe(@"""C:\Program Files\app.exe"""));
    }

    [TestMethod]
    public void ArgsFollowsWindowsQuoting()
    {
        CollectionAssert.AreEqual(new[] { @"C:\Program Files\App\app.exe", "--port", "8081", "a b", @"say ""hi""", @"C:\a dir\" },
            CmdLine.Args(@"""C:\Program Files\App\app.exe"" --port 8081 ""a b"" ""say \""hi\"""" ""C:\a dir\\"""));
        CollectionAssert.AreEqual(new[] { "app.exe" }, CmdLine.Args("app.exe"));
        Assert.IsEmpty(CmdLine.Args("  "));
    }

    [TestMethod]
    public void ArgumentListRoundTripsThroughARealProcess()
    {
        // "cmd /k rem …" ignores the rest and then waits on stdin, so its command line can be read back
        string[] args = ["/d", "/k", "rem", @"C:\Program Files\x", @"a""b", "", "x y", @"C:\a dir\", @"\\srv\share", "tab\there"];
        using var p = Process.Start(new ProcessStartInfo("cmd.exe", args) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true })!;
        try
        {
            var raw = ProcessInfo.CommandLine(p.Id);
            Assert.IsNotNull(raw);
            Assert.AreEqual("cmd.exe", Path.GetFileName(CmdLine.SplitExe(raw).Exe), ignoreCase: true);
            CollectionAssert.AreEqual(args, CmdLine.Args(raw)[1..]);
        }
        finally { p.Kill(); }
    }
}
