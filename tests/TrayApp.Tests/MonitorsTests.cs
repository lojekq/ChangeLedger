using System.Drawing;
using TrayApp.Shell;

namespace TrayApp.Tests;

[TestClass]
public class MonitorsTests
{
    static readonly Display[] Display2Unplugged = [Layouts.Display1];

    [TestMethod]
    public void WindowOnUnpluggedDisplay2MovesToDisplay1()
    {
        var onDisplay2 = Rectangle.FromLTRB(3600, 100, 4400, 900);
        Assert.AreEqual(onDisplay2, Monitors.ClampToVisible(onDisplay2, Layouts.Owner));
        Assert.AreEqual(Rectangle.FromLTRB(2640, 100, 3440, 900), Monitors.ClampToVisible(onDisplay2, Display2Unplugged));

        // full-height portrait window: only the height shrinks to DISPLAY1's work area
        var tall = Rectangle.FromLTRB(3440, 0, 4880, 2560);
        Assert.AreEqual(Rectangle.FromLTRB(2000, 0, 3440, 1408), Monitors.ClampToVisible(tall, Display2Unplugged));
    }

    [TestMethod]
    public void HalfVisibleStaysLessMoves()
    {
        var acrossTheSeam = Rectangle.FromLTRB(3000, 100, 3880, 600);
        Assert.AreEqual(acrossTheSeam, Monitors.ClampToVisible(acrossTheSeam, Layouts.Owner));

        var sixtyPercent = Rectangle.FromLTRB(0, 840, 1000, 1840);
        Assert.AreEqual(sixtyPercent, Monitors.ClampToVisible(sixtyPercent, Layouts.Owner));

        var fortyPercent = Rectangle.FromLTRB(0, 1040, 1000, 2040);
        Assert.AreEqual(Rectangle.FromLTRB(0, 408, 1000, 1408), Monitors.ClampToVisible(fortyPercent, Layouts.Owner));

        // under DISPLAY1, left of DISPLAY2's lower half: no monitor there, DISPLAY1 is nearer
        var inTheGap = Rectangle.FromLTRB(2000, 1500, 3000, 2000);
        Assert.AreEqual(Rectangle.FromLTRB(2000, 908, 3000, 1408), Monitors.ClampToVisible(inTheGap, Layouts.Owner));
    }

    [TestMethod]
    public void AtFindsTheMonitorUnderAPoint()
    {
        Assert.AreEqual(Layouts.Display1, Monitors.At(new Point(3439, 1439), Layouts.Owner));
        Assert.AreEqual(Layouts.Display2, Monitors.At(new Point(3440, 2000), Layouts.Owner));
        Assert.IsNull(Monitors.At(new Point(2000, 2000), Layouts.Owner));
    }
}
