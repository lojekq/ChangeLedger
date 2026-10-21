using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
using ChangeLedger.Demo;
using ChangeLedger.Shell;
using InfoBar = Wpf.Ui.Controls.InfoBar;
using InfoBarSeverity = Wpf.Ui.Controls.InfoBarSeverity;

namespace ChangeLedger.Features;

/// <summary>
/// "Changes": timeline by day on the left, net A→B diff on the right. A range comes from a preset, the timeline
/// (click = previous snapshot → that day's last, Shift+click = range), the A/B pickers, an update or the Reboots page.
/// </summary>
public partial class ChangesPage
{
    static ChangesPage? shown;
    static Action<ChangesPage>? pending;

    /// <summary>--page changes-&lt;scene&gt; under --demo: which range and categories the screenshot shows.</summary>
    public static string? Scene { get; set; }

    /// <summary>The page on screen (autoplay drives it).</summary>
    public static ChangesPage? Shown => shown;

    /// <summary>Tray "What changed in N days": now, or when the page opens.</summary>
    public static void Request(int days) => Post(p => p.Preset(days));

    /// <summary>Reboots page "What changed before this": A at or before <paramref name="aUtc"/>, B at or before <paramref name="bUtc"/>.</summary>
    public static void Request(DateTime aUtc, DateTime bUtc) => Post(p => p.Range(aUtc, bUtc));

    static void Post(Action<ChangesPage> a)
    {
        if (shown is { IsLoaded: true } p) a(p);
        else pending = a;
    }

    IndexEntry? a, b;
    Diff? diff;
    List<KeyVersion> keys = [];
    string? error;
    Action? reapply;              // how the current range was chosen, re-run after the index changes
    List<PowerEntry> power = [];
    List<WuEntry> updates = [];
    List<DateTime> boots = [];
    readonly Dictionary<string, bool> expanded = [];
    readonly ObservableCollection<object> rows = [];
    bool syncing;
    int generation, journalGeneration;

    public ChangesPage()
    {
        InitializeComponent();
        Language = System.Windows.Markup.XmlLanguage.GetLanguage(System.Globalization.CultureInfo.CurrentUICulture.IetfLanguageTag); // DatePicker formats by Language, not by the system culture
        DiffList.ItemsSource = rows;
        Preset1.Click += (_, _) => Preset(1);
        Preset7.Click += (_, _) => Preset(7);
        Preset30.Click += (_, _) => Preset(30);
        UpdateButton.Click += (_, _) => OpenUpdates();
        SearchBox.TextChanged += (_, _) => Render();
        HiddenSwitch.Click += (_, _) => Render();
        CopyButton.Click += (_, _) => CopyForAi();
        EmptySnapshot.Click += (_, _) => TrayMenu.SnapshotNow();
        Timeline.SelectionChanged += (_, _) => TimelinePicked();
        ADate.SelectedDateChanged += (_, _) => DatePicked(ADate, ATime);
        BDate.SelectedDateChanged += (_, _) => DatePicked(BDate, BTime);
        ATime.SelectionChanged += (_, _) => { if (!syncing) Picked(); };
        BTime.SelectionChanged += (_, _) => { if (!syncing) Picked(); };
        Loaded += (_, _) =>
        {
            shown = this;
            Ledger.Refresh();
            IndexChanged();
            Ledger.Changed += IndexChanged;
            Ledger.BusyChanged += BusyChanged;
            BusyChanged();
        };
        Unloaded += (_, _) =>
        {
            if (shown == this) shown = null;
            Ledger.Changed -= IndexChanged;
            Ledger.BusyChanged -= BusyChanged;
        };
    }

    void BusyChanged() => EmptySnapshot.IsEnabled = !Ledger.Busy && !AppInfo.IsElevated;

    void IndexChanged()
    {
        BuildTimeline();
        LoadJournal();
        if (pending is { } p) { pending = null; p(this); }
        else if (reapply is not null) reapply();
        else if (Args.Current.Demo) DemoScene(Scene);
        else Preset(7);
    }

    static DateTime Utc(DateTime localDay) => DateTime.SpecifyKind(localDay, DateTimeKind.Local).ToUniversalTime();

    /// <summary>Reboots, wakes and updates for the timeline icons; the System log is read once per index change.</summary>
    async void LoadJournal()
    {
        var idx = Ledger.Index;
        if (idx.Count == 0) return;
        var g = ++journalGeneration;
        var from = Utc(idx[0].T.ToLocalTime().Date);
        var to = DateTime.UtcNow;
        var (p, u, bt) = await Task.Run(() => (Try(() => Ledger.Power(from, to)), Ledger.Updates(), Try(() => Ledger.Boots(from, to))));
        if (g != journalGeneration) return;
        (power, updates, boots) = (p, u, bt);
        BuildTimeline();
        Render();

        static List<T> Try<T>(Func<List<T>> f)
        {
            try { return f(); }
            catch (Exception e) { Log.Warn("system log", e); return []; }
        }
    }

    void BuildTimeline()
    {
        var idx = Ledger.Index;
        var days = new List<DayRow>();
        if (idx.Count > 0)
        {
            var first = idx[0].T.ToLocalTime().Date;
            var today = DateTime.Today;
            if ((today - first).TotalDays > 400) first = today.AddDays(-400);
            for (var d = today; d >= first; d = d.AddDays(-1))
            {
                var (from, to) = (Utc(d), Utc(d.AddDays(1)));
                var counts = new Dictionary<string, int>();
                foreach (var e in idx.Where(e => e.T >= from && e.T < to))
                    foreach (var (k, v) in e.D) counts[k] = counts.GetValueOrDefault(k) + v;
                var p = power.Where(x => x.Utc >= from && x.Utc < to).OrderBy(x => x.Utc).ToList();
                var u = updates.Where(x => x.Utc >= from && x.Utc < to).OrderBy(x => x.Utc).ToList();
                var reboots = p.Where(x => x.Kind != PowerKind.Wake).ToList();
                var tip = string.Join("\n", p.Select(x => $"{x.Utc.ToLocalTime():HH:mm} {Words.PowerEvent(x)}").Concat(u.Select(x => $"{x.Utc.ToLocalTime():HH:mm} {Words.UpdateTitle(x)}")));
                days.Add(new(d, Words.Day(d), Words.Chips(counts), reboots.Count > 0, reboots.Any(x => x.Unexpected), p.Any(x => x.Kind == PowerKind.Wake),
                    u.Any(x => x.StoreCount == 0), tip.Length > 0 ? tip : null, counts.Count == 0 && p.Count == 0 && u.Count == 0));
            }
        }
        syncing = true;
        Timeline.ItemsSource = days;
        syncing = false;
    }

    void Select(IndexEntry? na, IndexEntry? nb, Action again, string? updateLabel = null)
    {
        if (na is not null && nb is not null && na.T > nb.T) (na, nb) = (nb, na);
        (a, b, reapply) = (na, nb, again);
        UpdateText.Text = updateLabel ?? Loc.T("Changes.BeforeAfter");
        expanded.Clear();
        syncing = true;
        Fill(ADate, ATime, a);
        Fill(BDate, BTime, b);
        syncing = false;
        Compute();
    }

    void ClearTimeline()
    {
        syncing = true;
        Timeline.UnselectAll();
        syncing = false;
    }

    internal void Preset(int days)
    {
        var idx = Ledger.Index;
        ClearTimeline();
        Select(Ledger.AtOrBefore(DateTime.UtcNow.AddDays(-days)) ?? idx.FirstOrDefault(), idx.LastOrDefault(), () => Preset(days));
    }

    void Range(DateTime aUtc, DateTime bUtc)
    {
        ClearTimeline();
        Select(Ledger.AtOrBefore(aUtc) ?? Ledger.Index.FirstOrDefault(), Ledger.AtOrBefore(bUtc) ?? Ledger.Index.FirstOrDefault(), () => Range(aUtc, bUtc));
    }

    void TimelinePicked()
    {
        if (syncing) return;
        var sel = Timeline.SelectedItems.Cast<DayRow>().ToList();
        if (sel.Count > 0) PickDays(sel.Min(d => d.Day), sel.Max(d => d.Day));
    }

    /// <summary>Previous snapshot before <paramref name="first"/> → the last snapshot of <paramref name="last"/>.</summary>
    void PickDays(DateTime first, DateTime last)
    {
        var idx = Ledger.Index;
        Select(idx.LastOrDefault(e => e.T < Utc(first)) ?? idx.FirstOrDefault(e => e.T >= Utc(first)),
            idx.LastOrDefault(e => e.T < Utc(last.AddDays(1))), () => PickDays(first, last));
    }

    /// <summary>Autoplay: the same as a click on that day in the timeline.</summary>
    internal void ClickDay(DateTime day)
    {
        if ((Timeline.ItemsSource as List<DayRow>)?.FirstOrDefault(d => d.Day == day) is not { } row) return;
        Timeline.SelectedItem = row;
        Timeline.ScrollIntoView(row);
    }

    void Fill(DatePicker date, ComboBox time, IndexEntry? e)
    {
        date.SelectedDate = e?.T.ToLocalTime().Date;
        FillTimes(time, date.SelectedDate, e);
    }

    static void FillTimes(ComboBox time, DateTime? day, IndexEntry? select)
    {
        time.Items.Clear();
        if (day is null) return;
        foreach (var e in Ledger.Index.Where(e => e.T.ToLocalTime().Date == day))
            time.Items.Add(new ComboBoxItem { Content = $"{e.T.ToLocalTime():HH:mm} · {Loc.T($"Reason.{e.R}")}", Tag = e });
        var items = time.Items.Cast<ComboBoxItem>().ToList();
        time.SelectedItem = items.FirstOrDefault(i => Equals(i.Tag, select)) ?? items.LastOrDefault();
    }

    /// <summary>A day without snapshots means the last snapshot before it.</summary>
    void DatePicked(DatePicker date, ComboBox time)
    {
        if (syncing || date.SelectedDate is not { } day) return;
        var e = Ledger.Index.LastOrDefault(x => x.T < Utc(day.AddDays(1))) ?? Ledger.Index.FirstOrDefault();
        syncing = true;
        date.SelectedDate = e?.T.ToLocalTime().Date;
        FillTimes(time, date.SelectedDate, e);
        syncing = false;
        Picked();
    }

    void Picked()
    {
        var fa = ((ATime.SelectedItem as ComboBoxItem)?.Tag as IndexEntry)?.F;
        var fb = ((BTime.SelectedItem as ComboBoxItem)?.Tag as IndexEntry)?.F;
        ClearTimeline();
        SelectFiles(fa, fb);
    }

    void SelectFiles(string? fa, string? fb) =>
        Select(Ledger.Index.FirstOrDefault(e => e.F == fa), Ledger.Index.FirstOrDefault(e => e.F == fb), () => SelectFiles(fa, fb));

    /// <summary>A = the last snapshot before the update, B = the first one after the next boot.</summary>
    (IndexEntry? Before, IndexEntry? After) AroundUpdate(WuEntry u)
    {
        var boot = boots.FirstOrDefault(t => t > u.Utc);
        return (Ledger.Index.LastOrDefault(e => e.T < u.Utc), Ledger.After(boot == default ? u.Utc : boot));
    }

    void PickUpdate(WuEntry u)
    {
        var (before, after) = AroundUpdate(u);
        ClearTimeline();
        Select(before, after, () => PickUpdate(u), string.Format(Loc.T("Changes.BeforeAfterKb"), EventHistory.Kb(u.Title) ?? Words.When(u.Utc)));
    }

    void OpenUpdates()
    {
        var menu = new ContextMenu { PlacementTarget = UpdateButton, Placement = PlacementMode.Bottom };
        var list = updates.Where(u => u.StoreCount == 0 && u.Ok).Take(25).ToList();
        if (list.Count == 0) menu.Items.Add(new MenuItem { Header = Loc.T("Changes.NoUpdates"), IsEnabled = false });
        foreach (var u in list)
        {
            var (before, after) = AroundUpdate(u);
            var title = u.Title.Length > 90 ? u.Title[..90] + "…" : u.Title;
            var item = new MenuItem { Header = $"{Words.When(u.Utc)} · {title}", IsEnabled = before is not null && after is not null };
            if (!item.IsEnabled)
            {
                item.ToolTip = Loc.T(before is null ? "Changes.NoBefore" : "Changes.NoAfter");
                ToolTipService.SetShowOnDisabled(item, true);
            }
            item.Click += (_, _) => PickUpdate(u);
            menu.Items.Add(item);
        }
        menu.IsOpen = true;
    }

    async void Compute()
    {
        var g = ++generation;
        (diff, error) = (null, null);
        if (a is not null && b is not null && a.F != b.F)
        {
            var (fa, fb) = (a.F, b.F);
            try
            {
                var (d, k) = await Task.Run(() =>
                {
                    var (sa, sb) = (Ledger.Load(fa), Ledger.Load(fb));
                    return (DiffEngine.Compare(sa, sb), DiffEngine.KeyVersions(sa, sb));
                });
                if (g != generation) return;
                (diff, keys) = (d, k);
            }
            catch (Exception e)
            {
                if (g != generation) return;
                Log.Warn("diff failed", e);
                error = e.Message;
            }
        }
        Render();
    }

    bool Visible(Change c, string q) =>
        (HiddenSwitch.IsChecked == true || !c.Noise) &&
        (q.Length == 0 || c.Title.Contains(q, StringComparison.CurrentCultureIgnoreCase) || c.Key.Contains(q, StringComparison.CurrentCultureIgnoreCase)
         || c.Props.Any(p => (p.Old ?? "").Contains(q, StringComparison.CurrentCultureIgnoreCase) || (p.New ?? "").Contains(q, StringComparison.CurrentCultureIgnoreCase))
         || (c.Path ?? []).Any(s => s.Element.Contains(q, StringComparison.CurrentCultureIgnoreCase)));

    /// <summary>Visible categories with their rows, for the list and for "Copy for AI".</summary>
    List<(CategoryDiff Cat, List<ChangeRow> Rows)> VisibleCategories()
    {
        var q = SearchBox.Text?.Trim() ?? "";
        return diff is null ? [] : [.. diff.Categories.Select(c => (c, c.Changes.Where(x => Visible(x, q)).Select(Words.Row).ToList()))
            .Where(x => x.Item2.Count > 0 || x.c.NoDataIn is not null)];
    }

    List<string> Notes()
    {
        var notes = new List<string>();
        if (diff is null || a is null || b is null) return notes;
        if (diff.BuildChanged) notes.Add(Loc.T("Banner.Build"));
        if (diff.Categories.Where(c => c.FormatChanged).Select(c => Words.Category(c.Id)).ToList() is { Count: > 0 } f)
            notes.Add(string.Format(Loc.T("Banner.Format"), string.Join(", ", f)));
        if (diff.Categories.Where(c => c.NoDataIn is not null).Select(c => Words.Category(c.Id)).ToList() is { Count: > 0 } n)
            notes.Add(string.Format(Loc.T("Banner.NoData"), string.Join(", ", n)));
        if (Ledger.LogOldest is { } oldest && a.T < oldest) notes.Add(string.Format(Loc.T("Banner.Log"), Words.WhenFull(oldest)));
        return notes;
    }

    void Render()
    {
        rows.Clear();
        Banners.Children.Clear();
        KeyText.Inlines.Clear();
        var idx = Ledger.Index;
        var cats = VisibleCategories();
        string? empty =
            idx.Count == 0 ? Loc.T("Changes.NoSnapshots")
            : idx.Count == 1 ? Loc.T("Changes.FirstSnapshot")
            : a is null || b is null ? Loc.T("Changes.PickRange")
            : error is not null ? string.Format(Loc.T("Changes.Error"), error)
            : a.F == b.F || diff is not null && cats.Count == 0 ? string.Format(Loc.T("Changes.None"), Words.When(a.T), Words.When(b.T))
            : null;
        EmptyText.Text = empty ?? "";
        EmptyPanel.Visibility = empty is null ? Visibility.Collapsed : Visibility.Visible;
        EmptySnapshot.Visibility = idx.Count <= 1 && !Args.Current.Demo ? Visibility.Visible : Visibility.Collapsed;
        if (diff is null) return;

        foreach (var n in Notes())
            Banners.Children.Add(new InfoBar { Message = n, Severity = InfoBarSeverity.Warning, IsOpen = true, IsClosable = false, Margin = new(0, 0, 0, 6) });

        var accent = (Brush)FindResource("AccentTextFillColorPrimaryBrush");
        foreach (var k in keys)
        {
            if (KeyText.Inlines.Count > 0) KeyText.Inlines.Add(new Run("  ·  "));
            KeyText.Inlines.Add(new Run(k.Label + " "));
            if (!k.Changed) { KeyText.Inlines.Add(new Run(k.New ?? "—")); continue; }
            KeyText.Inlines.Add(new Run(k.Old ?? "—"));
            KeyText.Inlines.Add(new Run(" → "));
            KeyText.Inlines.Add(new Run(k.New ?? "—") { FontWeight = FontWeights.SemiBold, Foreground = accent });
        }

        var total = cats.Sum(c => c.Rows.Count);
        foreach (var (cat, list) in cats)
        {
            var open = expanded.TryGetValue(cat.Id, out var x) ? x : list.Count <= 50 && total <= 300; // a feature update starts collapsed
            var h = new HeaderRow(cat.Id, Words.Header(cat.Id, [.. list.Select(r => r.Change)]), Words.NoData(cat), list, open);
            rows.Add(h);
            if (open) foreach (var r in list) rows.Add(r);
        }
    }

    void Header_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not HeaderRow h || !h.HasRows) return;
        Toggle(h, !h.Expanded);
    }

    void Toggle(HeaderRow h, bool open)
    {
        if (h.Expanded == open) return;
        h.Expanded = expanded[h.Id] = open;
        var i = rows.IndexOf(h);
        if (open) for (var k = 0; k < h.Rows.Count; k++) rows.Insert(i + 1 + k, h.Rows[k]);
        else for (var k = 0; k < h.Rows.Count; k++) rows.RemoveAt(i + 1);
    }

    /// <summary>Autoplay: open one category and close the rest.</summary>
    internal void ExpandOnly(params string[] ids)
    {
        foreach (var h in rows.OfType<HeaderRow>().ToList()) Toggle(h, ids.Contains(h.Id));
    }

    static void ToClipboard(string text)
    {
        try { Clipboard.SetText(text); }
        catch (System.Runtime.InteropServices.COMException e) { Log.Warn("clipboard busy", e); } // another app holds it
    }

    void Copy_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is ChangeRow r) ToClipboard(Words.Line(r));
    }

    void CopyPath_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is ChangeRow r) ToClipboard(Words.Path(r.Change));
    }

    void CopyForAi()
    {
        if (a is null || b is null) return;
        var (from, to) = (a.T, b.T);
        ToClipboard(Words.Markdown(from, to, [.. VisibleCategories().Select(c => (c.Cat.Id, c.Rows))], Notes(),
            power.Where(p => p.Utc > from && p.Utc <= to), updates.Where(u => u.Utc > from && u.Utc <= to)));
        CopyText.Text = Loc.T("Changes.Copied");
        var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
        t.Tick += (_, _) => { t.Stop(); CopyText.Text = Loc.T("Changes.CopyAi"); };
        t.Start();
    }

    /// <summary>--demo: the screenshot scenes of spec §10.</summary>
    void DemoScene(string? scene)
    {
        var s = DemoData.Scene(scene);
        IndexEntry? E(string f) => Ledger.Index.FirstOrDefault(e => e.F == f);
        if (s.Update is { } u) PickUpdate(u);
        else Select(E(s.A), E(s.B), () => DemoScene(scene));
        foreach (var c in Collectors.All) expanded[c.Id] = s.Expand.Contains(c.Id);
    }
}
