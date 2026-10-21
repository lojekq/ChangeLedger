using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace TrayApp.Shell;

/// <summary>
/// Records a combination for the settings page: focus, press the keys; Backspace or Delete clears, Esc leaves.
/// The page registers it with Hotkeys.Register and puts the result into Status ("taken by another program").
/// </summary>
public sealed class HotkeyBox : UserControl
{
    public static readonly DependencyProperty HotkeyProperty = DependencyProperty.Register(nameof(Hotkey), typeof(string), typeof(HotkeyBox),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, (d, _) => ((HotkeyBox)d).Refresh()));

    public static readonly DependencyProperty StatusProperty = DependencyProperty.Register(nameof(Status), typeof(HotkeyStatus), typeof(HotkeyBox),
        new PropertyMetadata(HotkeyStatus.Ok, (d, _) => ((HotkeyBox)d).Refresh()));

    /// <summary>"Ctrl+Alt+W" or null.</summary>
    public string? Hotkey { get => (string?)GetValue(HotkeyProperty); set => SetValue(HotkeyProperty, value); }
    public HotkeyStatus Status { get => (HotkeyStatus)GetValue(StatusProperty); set => SetValue(StatusProperty, value); }

    /// <summary>The user recorded or cleared a combination.</summary>
    public event EventHandler? Changed;

    readonly Wpf.Ui.Controls.TextBox box = new() { IsReadOnly = true, IsReadOnlyCaretVisible = false, MinWidth = 220, ClearButtonEnabled = false };
    readonly TextBlock status = new() { Margin = new Thickness(0, 4, 0, 0), Visibility = Visibility.Collapsed };

    public HotkeyBox()
    {
        box.PlaceholderText = Loc.T("Hotkey.Placeholder");
        status.SetResourceReference(TextBlock.ForegroundProperty, "SystemFillColorCriticalBrush");
        Content = new StackPanel { Children = { box, status } };
        box.PreviewKeyDown += OnKey;
    }

    void OnKey(object sender, KeyEventArgs e)
    {
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        switch (key)
        {
            case Key.Escape: Keyboard.ClearFocus(); return;
            case Key.Back or Key.Delete: Set(null); return;
            case Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin: return;
        }
        if (Keyboard.Modifiers == ModifierKeys.None && key is not (>= Key.F1 and <= Key.F24)) return;
        Set(new Hotkey(Keyboard.Modifiers, key).ToString());
    }

    void Set(string? value)
    {
        if (value == Hotkey) return;
        Hotkey = value;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    void Refresh()
    {
        box.Text = Hotkey ?? "";
        status.Text = Status switch { HotkeyStatus.Busy => Loc.T("Hotkey.Busy"), HotkeyStatus.Failed => Loc.T("Hotkey.Failed"), _ => "" };
        status.Visibility = Status == HotkeyStatus.Ok ? Visibility.Collapsed : Visibility.Visible;
    }
}
