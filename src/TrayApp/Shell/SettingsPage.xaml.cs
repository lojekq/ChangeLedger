using System.Windows;
using System.Windows.Controls;

namespace TrayApp.Shell;

/// <summary>Shell settings (language, theme, autostart, Start menu, updates) plus the app's own section below.</summary>
public partial class SettingsPage
{
    /// <summary>The app's own settings, e.g. <c>SettingsPage.AppSection = () =&gt; new Features.MySettings();</c> in App startup.</summary>
    public static Func<FrameworkElement>? AppSection;

    public SettingsPage()
    {
        InitializeComponent();
        var s = Settings.Current;
        Select(LanguageBox, s.Language);
        Select(ThemeBox, s.Theme);
        AutostartSwitch.IsChecked = !Args.Current.Demo && Autostart.IsEnabled; // --demo: same picture on every PC
        AutostartNote.Text = Autostart.IsDisabledByUser ? Loc.T("Settings.Autostart.DisabledByUser") : "";
        StartMenuSwitch.IsChecked = !Args.Current.Demo && StartMenu.Exists;
        UpdatesSwitch.IsChecked = s.CheckUpdates;
        AppSectionHost.Content = AppSection?.Invoke();

        var initialLanguage = s.Language;
        LanguageBox.SelectionChanged += (_, _) =>
        {
            s.Language = Selected(LanguageBox);
            Settings.Save();
            RestartButton.Visibility = s.Language == initialLanguage ? Visibility.Collapsed : Visibility.Visible;
        };
        RestartButton.Click += (_, _) => App.Restart();
        ThemeBox.SelectionChanged += (_, _) =>
        {
            s.Theme = Selected(ThemeBox);
            Settings.Save();
            if (Window.GetWindow(this) is Wpf.Ui.Controls.FluentWindow w) Theme.Apply(w, s.Theme);
        };
        AutostartSwitch.Click += (_, _) => Autostart.Set(AutostartSwitch.IsChecked == true);
        StartMenuSwitch.Click += (_, _) => StartMenu.Set(StartMenuSwitch.IsChecked == true);
        UpdatesSwitch.Click += (_, _) => { s.CheckUpdates = UpdatesSwitch.IsChecked == true; Settings.Save(); };
    }

    static void Select(ComboBox box, string tag) =>
        box.SelectedItem = box.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == tag) ?? box.Items[0];

    static string Selected(ComboBox box) => (string)((ComboBoxItem)box.SelectedItem).Tag;
}
