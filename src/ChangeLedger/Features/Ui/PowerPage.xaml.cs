using System.Windows;
using System.Windows.Controls;
using ChangeLedger.Shell;
using InfoBar = Wpf.Ui.Controls.InfoBar;
using InfoBarSeverity = Wpf.Ui.Controls.InfoBarSeverity;

namespace ChangeLedger.Features;

public sealed record PowerRow(PowerEntry Entry, string Time, string Event, string Cause, string Hint)
{
    public bool Unexpected => Entry.Unexpected;
}

/// <summary>"Reboots and wakes": every boot session and wake explained from the System log, plus what may wake the PC now.</summary>
public partial class PowerPage
{
    int generation;

    public PowerPage()
    {
        InitializeComponent();
        RangeBox.SelectedIndex = 1; // 30 days
        RangeBox.SelectionChanged += (_, _) => Load();
        Loaded += (_, _) => Load();
    }

    async void Load()
    {
        var g = ++generation;
        var days = int.Parse((string)((ComboBoxItem)RangeBox.SelectedItem).Tag);
        var to = DateTime.UtcNow;
        var from = to.AddDays(-days);
        var latest = Ledger.Latest();
        var tasks = latest?.Categories.GetValueOrDefault("tasks")?.Items.Where(i => i["WakeToRun"] == "true" && i["Enabled"] == "true").Select(i => i.K).ToList() ?? [];
        var devices = latest?.Categories.GetValueOrDefault("wake")?.Items.Select(i => i.T).ToList() ?? [];
        WakeTasks.ItemsSource = tasks.Count > 0 ? tasks : [Loc.T("Power.None")];
        WakeDevices.ItemsSource = devices.Count > 0 ? devices : [Loc.T("Power.None")];

        List<PowerEntry> entries;
        try { entries = await Task.Run(() => Ledger.Power(from, to)); }
        catch (Exception e)
        {
            Log.Warn("system log", e);
            entries = [];
        }
        if (g != generation) return;
        List.ItemsSource = entries.Select(e => new PowerRow(e, Words.When(e.Utc), Words.PowerEvent(e), Words.PowerCause(e), Words.PowerHint(e, devices))).ToList();
        EmptyText.Text = entries.Count == 0 ? Loc.T("Power.Empty") : "";
        Banners.Children.Clear();
        if (Ledger.LogOldest is { } oldest && oldest > from)
            Banners.Children.Add(new InfoBar
            {
                Message = string.Format(Loc.T("Banner.Log"), Words.WhenFull(oldest)), Severity = InfoBarSeverity.Informational,
                IsOpen = true, IsClosable = false, Margin = new(0, 0, 0, 8),
            });
    }

    /// <summary>"What changed before this": Changes with B = the event, A = 7 days before it.</summary>
    void Before_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not PowerRow r) return;
        ChangesPage.Request(r.Entry.Utc.AddDays(-7), r.Entry.Utc);
        App.ShowMain("changes");
    }
}
