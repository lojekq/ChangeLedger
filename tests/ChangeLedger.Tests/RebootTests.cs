using ChangeLedger.Features;
using static ChangeLedger.Features.EventHistory;

namespace ChangeLedger.Tests;

/// <summary>Spec §9 test 5: RebootClassifier on event sequences.</summary>
[TestClass]
public class RebootTests
{
    static readonly DateTime T0 = new(2026, 10, 12, 20, 0, 0, DateTimeKind.Utc);

    static Ev E(int minute, string provider, int id, params (string, string)[] data) =>
        new(T0.AddMinutes(minute), provider, id, data.ToDictionary(d => d.Item1, d => d.Item2));

    static PowerEntry Last(params Ev[] evs) => RebootClassifier.Classify(evs).First(e => e.Kind != PowerKind.Wake);

    [TestMethod]
    public void Planned1074IsAPlannedRestart()
    {
        var e = Last(E(0, KernelGeneral, 12),
            E(50, User32, 1074, ("param1", @"C:\WINDOWS\system32\winlogon.exe (MINTI)"), ("param3", "Operating System: Upgrade (Planned)"),
                ("param5", "restart"), ("param6", ""), ("param7", @"NT AUTHORITY\SYSTEM")),
            E(51, KernelGeneral, 13),
            E(52, KernelBoot, 27, ("BootType", "0")),
            E(52, KernelGeneral, 12));
        Assert.AreEqual(PowerKind.Restart, e.Kind);
        Assert.AreEqual(@"winlogon.exe, NT AUTHORITY\SYSTEM", e.Initiator);
        Assert.AreEqual("Operating System: Upgrade (Planned)", e.Reason);
        Assert.AreEqual(T0.AddMinutes(50), e.Utc);
        Assert.AreEqual(0, e.BootType);
        Assert.IsFalse(e.Unexpected);
    }

    [TestMethod]
    public void RussianRestartWordAndShutdown()
    {
        Assert.AreEqual(PowerKind.Restart, Last(E(0, User32, 1074, ("param5", "Перезапустить")), E(1, KernelGeneral, 12)).Kind);
        Assert.AreEqual(PowerKind.Shutdown, Last(E(0, User32, 1074, ("param5", "Выключение питания")), E(1, KernelGeneral, 12)).Kind);
    }

    [TestMethod]
    public void BugcheckIsBsod()
    {
        var e = Last(E(0, KernelGeneral, 12), E(60, KernelGeneral, 12), E(61, KernelPower, 41, ("BugcheckCode", "159"), ("PowerButtonTimestamp", "0")));
        Assert.AreEqual(PowerKind.Bsod, e.Kind);
        Assert.AreEqual("0x0000009F", e.Code);
        Assert.IsTrue(e.Unexpected);

        var wer = Last(E(0, KernelGeneral, 12), E(1, Wer, 1001, ("param1", "0x0000009f (0x3, 0xffff)")));
        Assert.AreEqual(PowerKind.Bsod, wer.Kind);
        Assert.AreEqual("0x0000009f", wer.Code);
    }

    [TestMethod]
    public void PowerButtonHold()
    {
        var e = Last(E(0, KernelGeneral, 12), E(1, KernelPower, 41, ("BugcheckCode", "0"), ("PowerButtonTimestamp", "134055123456789")));
        Assert.AreEqual(PowerKind.PowerButton, e.Kind);
    }

    [TestMethod]
    public void Plain41Or6008IsPowerLossEvenAfterA1074()
    {
        Assert.AreEqual(PowerKind.PowerLoss, Last(E(0, KernelGeneral, 12), E(1, KernelPower, 41, ("BugcheckCode", "0"), ("PowerButtonTimestamp", "0"))).Kind);
        Assert.AreEqual(PowerKind.PowerLoss, Last(E(0, KernelGeneral, 12), E(1, EventLog, 6008)).Kind);
        var hung = Last(E(0, User32, 1074, ("param5", "restart")), E(5, KernelGeneral, 12), E(6, KernelPower, 41, ("BugcheckCode", "0")));
        Assert.AreEqual(PowerKind.PowerLoss, hung.Kind, "a restart that hung and was power-cycled");
    }

    [TestMethod]
    public void StopThenStartIsShutdown()
    {
        var e = Last(E(0, KernelGeneral, 12), E(30, KernelGeneral, 13), E(90, KernelGeneral, 12));
        Assert.AreEqual(PowerKind.Shutdown, e.Kind);
        Assert.AreEqual(T0.AddMinutes(30), e.Utc);
    }

    [TestMethod]
    public void NothingKnownIsUnknown()
    {
        Assert.AreEqual(PowerKind.Unknown, Last(E(0, KernelGeneral, 12)).Kind);
        Assert.AreEqual(0, RebootClassifier.Classify([]).Count);
    }

    [TestMethod]
    public void WakeByTaskTimerKeepsTheTaskPath()
    {
        var w = RebootClassifier.Classify([E(0, Troubleshooter, 1, ("WakeTime", "2026-09-23T14:15:09.7162793Z"),
            ("WakeSourceText", "Будет выполнено назначенное задание \"NT TASK\\Microsoft\\Windows\\UpdateOrchestrator\\Schedule Wake To Work\", запросившее вывод компьютера из спящего режима."))]).Single();
        Assert.AreEqual(PowerKind.Wake, w.Kind);
        Assert.AreEqual(@"\Microsoft\Windows\UpdateOrchestrator\Schedule Wake To Work", w.WakeTask);
        Assert.AreEqual(new DateTime(2026, 9, 23, 14, 15, 9, DateTimeKind.Utc), w.Utc.AddTicks(-(w.Utc.Ticks % TimeSpan.TicksPerSecond)));
        Assert.IsNull(RebootClassifier.Classify([E(0, Troubleshooter, 1, ("WakeSourceText", ""))]).Single().WakeSource);
    }
}
