using System.Runtime.InteropServices;
using System.Windows.Input;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Input.KeyboardAndMouse;

namespace TrayApp.Shell;

/// <summary>A combination as stored in settings: "Ctrl+Alt+W", "Ctrl+Win+Alt+Left".</summary>
public readonly record struct Hotkey(ModifierKeys Modifiers, Key Key)
{
    static readonly (ModifierKeys Mod, string Name)[] Names =
        [(ModifierKeys.Control, "Ctrl"), (ModifierKeys.Windows, "Win"), (ModifierKeys.Alt, "Alt"), (ModifierKeys.Shift, "Shift")];

    public override string ToString()
    {
        var mods = Modifiers;
        return string.Join("+", Names.Where(n => mods.HasFlag(n.Mod)).Select(n => n.Name).Append(Key.ToString()));
    }

    public static Hotkey? Parse(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var parts = s.Split('+', StringSplitOptions.TrimEntries);
        var mods = ModifierKeys.None;
        foreach (var p in parts[..^1])
        {
            var n = Names.FirstOrDefault(n => n.Name.Equals(p, StringComparison.OrdinalIgnoreCase));
            if (n.Name is null) return null;
            mods |= n.Mod;
        }
        return Enum.TryParse<Key>(parts[^1], true, out var key) && key != Key.None ? new Hotkey(mods, key) : null;
    }
}

public enum HotkeyStatus { Ok, Busy, Failed }

/// <summary>
/// RegisterHotKey with MOD_NOREPEAT on MessageWindow. No keyboard hooks (00-shared §4.3).
/// Only combinations from the table in 00-shared §9.5. --demo registers nothing and reports Ok,
/// otherwise a demo next to a working instance would show "taken".
/// </summary>
public static class Hotkeys
{
    /// <summary>Set by an app whose scene GIF needs the hotkey (PingWhen --autoplay), with the working instance closed.</summary>
    public static bool RegisterInDemo { get; set; }

    static readonly Dictionary<int, Action> Handlers = [];
    static bool hooked;

    public static HotkeyStatus Register(int id, Hotkey hotkey, Action pressed)
    {
        Unregister(id);
        if (Args.Current.Demo && !RegisterInDemo) return HotkeyStatus.Ok;
        if (!hooked) { MessageWindow.WndProc += OnMessage; hooked = true; }
        // ModifierKeys Alt/Control/Shift/Windows have the same bits as MOD_ALT/MOD_CONTROL/MOD_SHIFT/MOD_WIN
        var mods = (HOT_KEY_MODIFIERS)(uint)hotkey.Modifiers | HOT_KEY_MODIFIERS.MOD_NOREPEAT;
        if (PInvoke.RegisterHotKey(new HWND(MessageWindow.Handle), id, mods, (uint)KeyInterop.VirtualKeyFromKey(hotkey.Key)))
        {
            Handlers[id] = pressed;
            return HotkeyStatus.Ok;
        }
        var error = Marshal.GetLastPInvokeError();
        Log.Warn($"hotkey {hotkey} not registered, error {error}");
        return error == 1409 ? HotkeyStatus.Busy : HotkeyStatus.Failed; // 1409 ERROR_HOTKEY_ALREADY_REGISTERED
    }

    public static void Unregister(int id)
    {
        if (Handlers.Remove(id)) PInvoke.UnregisterHotKey(new HWND(MessageWindow.Handle), id);
    }

    static nint OnMessage(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == MessageWindow.WM_HOTKEY && Handlers.TryGetValue((int)wParam, out var pressed))
        {
            handled = true;
            pressed();
        }
        return 0;
    }
}
