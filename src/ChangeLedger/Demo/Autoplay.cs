using System.Windows.Threading;
using ChangeLedger.Features;

namespace ChangeLedger.Demo;

/// <summary>
/// --autoplay for gif.ps1 (spec §10): "what changed in 7 days" → the day with the red reboot → Drivers opens →
/// Reboots → "What changed before this" on the BSOD row.
/// </summary>
public static class Autoplay
{
    public static void Start(MainWindow w)
    {
        // Seconds after the window is on screen; gif.ps1 starts recording when --rect-file appears, ffmpeg needs ~1 s
        (double At, Action Do)[] steps =
        [
            (1.2, () => ChangesPage.Shown?.Preset(7)),
            (2.4, () => ChangesPage.Shown?.ClickDay(DateTime.Today.AddDays(-6))),
            (3.6, () => ChangesPage.Shown?.ExpandOnly("drivers")),
            (5.0, () => w.Navigate("power")),
            (6.4, () =>
            {
                var bsod = DemoData.Power.First(p => p.Kind == PowerKind.Bsod).Utc;
                ChangesPage.Request(bsod.AddDays(-7), bsod);
                w.Navigate("changes");
            }),
        ];
        var started = DateTime.UtcNow;
        var next = 0;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        timer.Tick += (_, _) =>
        {
            while (next < steps.Length && (DateTime.UtcNow - started).TotalSeconds >= steps[next].At) steps[next++].Do();
            if (next == steps.Length) timer.Stop();
        };
        timer.Start();
    }
}
