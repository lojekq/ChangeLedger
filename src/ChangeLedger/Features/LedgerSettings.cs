namespace ChangeLedger.Shell;

// Change Ledger's own settings fields (spec §5 "Данные и форматы")
public sealed partial class Settings
{
    public bool DailySnapshot { get; set; } = true;
    public string SnapshotTime { get; set; } = "12:00";   // local, HH:mm
    public int RetentionDays { get; set; } = 365;
    public int MaxStorageMb { get; set; } = 500;
    public List<string> ExtraSecretNames { get; set; } = [];
}
