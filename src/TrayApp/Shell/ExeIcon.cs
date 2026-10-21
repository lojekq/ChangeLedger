using System.Collections.Concurrent;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Windows.Win32;
using Windows.Win32.UI.WindowsAndMessaging;

namespace TrayApp.Shell;

/// <summary>
/// Small icon of an exe, frozen (so any thread can hand it to the UI) and cached by path.
/// null when the exe has no icon or can't be read: draw a glyph instead.
/// ExtractIconEx, not SHGetFileInfo: CsWin32 generates SHFILEINFOW only for a fixed CPU, and the project is AnyCPU.
/// </summary>
public static class ExeIcon
{
    static readonly ConcurrentDictionary<string, ImageSource?> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static ImageSource? Get(string exePath) => Cache.GetOrAdd(exePath, Load);

    static ImageSource? Load(string path)
    {
        Span<HICON> small = stackalloc HICON[1];
        if (PInvoke.ExtractIconEx(path, 0, default, small) is 0 or uint.MaxValue || small[0] == default) return null;
        try
        {
            var image = Imaging.CreateBitmapSourceFromHIcon(small[0], Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            image.Freeze();
            return image;
        }
        catch { return null; } // an icon is cosmetic
        finally { PInvoke.DestroyIcon(small[0]); } // HICONs leak otherwise
    }
}
