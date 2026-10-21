using System.Windows;
using Wpf.Ui.Controls;

namespace TrayApp.Shell
{
    // The app's own settings fields: same partial class, same namespace as Shell/Settings.cs
    public sealed partial class Settings
    {
        public string? ShowHotkey { get; set; }   // "Ctrl+Alt+W"; none by default
    }
}

namespace TrayApp.Features
{
    using TrayApp.Shell;

    /// <summary>Sample of the app's own settings section (SettingsPage.AppSection) with a hotkey. Replace in an app.</summary>
    public static class SampleSettings
    {
        const int HotkeyId = 1;

        /// <summary>Registers the saved combination; at start and after a change. Only combinations from 00-shared §9.5.</summary>
        public static HotkeyStatus Apply() =>
            Hotkey.Parse(Settings.Current.ShowHotkey) is { } h ? Hotkeys.Register(HotkeyId, h, () => App.ShowMain(null)) : HotkeyStatus.Ok;

        /// <summary>A plain CardControl, not a subclass: WPF-UI's implicit style only applies to the exact type.</summary>
        public static FrameworkElement Create()
        {
            var box = new HotkeyBox { Hotkey = Settings.Current.ShowHotkey, Status = Apply() };
            box.Changed += (_, _) =>
            {
                Settings.Current.ShowHotkey = box.Hotkey;
                Settings.Save();
                if (box.Hotkey is null) Hotkeys.Unregister(HotkeyId);
                box.Status = Apply();
            };
            return new CardControl
            {
                Header = Loc.T("Sample.Hotkey"), Icon = new SymbolIcon(SymbolRegular.Keyboard24),
                Content = box, Margin = new Thickness(0, 0, 0, 4),
            };
        }
    }
}
