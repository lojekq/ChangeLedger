using System.Diagnostics;
using TrayApp.Shell;

namespace TrayApp.Tests;

[TestClass]
public class ProcessInfoTests
{
    static readonly DateTime T0 = new(2026, 10, 21, 0, 0, 0, DateTimeKind.Utc);
    static ProcessNode N(int pid, int parent, int minute) => new(pid, parent, $"p{pid}.exe", T0.AddMinutes(minute));

    [TestMethod]
    public void ParentYoungerThanChildDoesNotCount()
    {
        var tree = new ProcessTree([
            N(10, 1, 0),   // explorer, its parent is gone
            N(20, 10, 1),  // terminal
            N(30, 20, 2),  // shell
            N(40, 30, 3),  // agent
            N(50, 60, 4),  // its parent died, and pid 60 went to a newer process
            N(60, 10, 5),
            new(70, 10, "p70.exe", null), // start time unknown
        ]);
        CollectionAssert.AreEqual(new[] { 30, 20, 10 }, tree.Ancestors(40).Select(p => p.Pid).ToArray());
        Assert.IsEmpty(tree.Ancestors(50));
        CollectionAssert.AreEquivalent(new[] { 20, 30, 40, 60 }, tree.Descendants(10).Select(p => p.Pid).ToArray());
        Assert.IsEmpty(tree.Descendants(60));
        Assert.IsNull(tree.Parent(70));
    }

    [TestMethod]
    public void ReadsItsOwnProcess()
    {
        int pid = Environment.ProcessId;
        using var self = Process.GetCurrentProcess();
        Assert.AreEqual(Environment.ProcessPath, ProcessInfo.ExePath(pid), ignoreCase: true);
        CollectionAssert.AreEqual(Environment.GetCommandLineArgs()[1..], CmdLine.Args(ProcessInfo.CommandLine(pid)!)[1..]); // .NET's argv[0] is the dll
        Assert.AreEqual(self.StartTime.ToUniversalTime(), ProcessInfo.StartTime(pid));
        Assert.IsNotNull(ProcessInfo.IsElevated(pid));
        Assert.IsNotEmpty(ProcessInfo.Tree().Ancestors(pid)); // dotnet test started us
        Assert.IsNull(ProcessInfo.ExePath(0x7FFFFFFC));       // Windows ignores the low 2 bits of a pid, so no odd numbers here
    }
}
