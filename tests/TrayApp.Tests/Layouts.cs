using System.Drawing;
using TrayApp.Shell;

namespace TrayApp.Tests;

/// <summary>Monitor layouts for geometry tests, physical px. Owner = the owner's PC (plan 04-edge-tuck §1); apps add their own rows here.</summary>
static class Layouts
{
    public static readonly Display Display1 = new(@"\\.\DISPLAY1", Rectangle.FromLTRB(0, 0, 3440, 1440), Rectangle.FromLTRB(0, 0, 3440, 1408), 96);
    public static readonly Display Display2 = new(@"\\.\DISPLAY2", Rectangle.FromLTRB(3440, 0, 4880, 2560), Rectangle.FromLTRB(3440, 0, 4880, 2560), 96); // portrait, no taskbar

    public static readonly Display[] Owner = [Display1, Display2];
}
