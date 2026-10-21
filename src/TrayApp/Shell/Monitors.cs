using System.Drawing;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.HiDpi;

namespace TrayApp.Shell;

/// <summary>One monitor in physical pixels (the app is PerMonitorV2). Device is @"\\.\DISPLAY1".</summary>
public sealed record Display(string Device, Rectangle Bounds, Rectangle Work, int Dpi);

/// <summary>Snapshot() reads the monitors; the rest are pure functions over a list, so tests run on fixtures (Layouts.Owner).</summary>
public static unsafe class Monitors
{
    public static List<Display> Snapshot()
    {
        var list = new List<Display>();
        PInvoke.EnumDisplayMonitors(default, null, (h, _, _, _) =>
        {
            var info = new MONITORINFOEXW { monitorInfo = { cbSize = (uint)sizeof(MONITORINFOEXW) } };
            if (PInvoke.GetMonitorInfo(h, (MONITORINFO*)&info))
            {
                var dpi = OperatingSystem.IsWindowsVersionAtLeast(10) // always true on .NET 10; tells CA1416 that 8.1+ API is fine
                    && PInvoke.GetDpiForMonitor(h, MONITOR_DPI_TYPE.MDT_EFFECTIVE_DPI, out var x, out _).Succeeded ? (int)x : 96;
                list.Add(new(info.szDevice.ToString(), ToRect(info.monitorInfo.rcMonitor), ToRect(info.monitorInfo.rcWork), dpi));
            }
            return true;
        }, 0);
        return list;
    }

    public static Display? At(Point p, IReadOnlyList<Display> monitors) => monitors.FirstOrDefault(m => m.Bounds.Contains(p));

    /// <summary>
    /// r unchanged if at least half of it is on monitors. Otherwise moved into the Work area of the nearest monitor
    /// (by the window's center), keeping its position as far as it fits; shrunk only where it is bigger than that Work area.
    /// </summary>
    public static Rectangle ClampToVisible(Rectangle r, IReadOnlyList<Display> monitors)
    {
        if (monitors.Count == 0 || 2 * monitors.Sum(m => Area(Rectangle.Intersect(r, m.Bounds))) >= Area(r)) return r;
        var center = new Point(r.Left + r.Width / 2, r.Top + r.Height / 2);
        var w = monitors.MinBy(m => Distance2(center, m.Work))!.Work;
        int width = Math.Min(r.Width, w.Width), height = Math.Min(r.Height, w.Height);
        return new Rectangle(Math.Clamp(r.Left, w.Left, w.Right - width), Math.Clamp(r.Top, w.Top, w.Bottom - height), width, height);
    }

    static long Area(Rectangle r) => (long)r.Width * r.Height;

    static long Distance2(Point p, Rectangle r)
    {
        long dx = Math.Max(0, Math.Max(r.Left - p.X, p.X - r.Right)), dy = Math.Max(0, Math.Max(r.Top - p.Y, p.Y - r.Bottom));
        return dx * dx + dy * dy;
    }

    static Rectangle ToRect(RECT r) => Rectangle.FromLTRB(r.left, r.top, r.right, r.bottom);
}
