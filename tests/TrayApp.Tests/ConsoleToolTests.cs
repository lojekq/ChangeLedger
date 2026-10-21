using System.Diagnostics;
using System.IO;
using TrayApp.Shell;

namespace TrayApp.Tests;

[TestClass]
public class ConsoleToolTests
{
    [TestMethod]
    public void ReadsOutputBiggerThanThePipeBuffer()
    {
        var file = Path.Combine(Path.GetTempPath(), $"consoletool-{Guid.NewGuid():N}.txt");
        var text = string.Concat(Enumerable.Repeat(new string('x', 62) + "\r\n", 16384)); // 1 MiB
        File.WriteAllText(file, text);
        try
        {
            // the same MiB to stderr: unread, it would fill its pipe and hang the child
            var (code, stdout) = ConsoleTool.Run("cmd.exe", ["/d", "/c", "type", file, "&&", "type", file, "1>&2"], TimeSpan.FromSeconds(30));
            Assert.AreEqual(0, code);
            Assert.AreEqual(text, stdout);
        }
        finally { File.Delete(file); }
    }

    [TestMethod]
    public async Task TimeoutKillsTheWholeTree()
    {
        var marker = $"127.{Random.Shared.Next(1, 255)}.{Random.Shared.Next(1, 255)}.{Random.Shared.Next(1, 255)}";
        var run = Task.Run(() => ConsoleTool.Run("cmd.exe", ["/d", "/c", "ping", "-n", "30", marker], TimeSpan.FromSeconds(3)));

        Process? ping = null;
        for (var sw = Stopwatch.StartNew(); ping is null && sw.Elapsed < TimeSpan.FromSeconds(3); await Task.Delay(100))
            ping = Process.GetProcessesByName("PING").FirstOrDefault(p => ProcessInfo.CommandLine(p.Id)?.Contains(marker) == true);
        Assert.IsNotNull(ping, "the grandchild never started");

        using (ping)
        {
            await Assert.ThrowsExactlyAsync<TimeoutException>(() => run);
            Assert.IsTrue(ping.WaitForExit(5000), "ping outlived the timeout");
        }
    }
}
