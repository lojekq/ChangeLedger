using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Dwm;

namespace TrayApp.Shell;

/// <summary>
/// --screenshot &lt;png&gt;: 500 ms after ContentRendered the window renders itself at 2× (RenderTargetBitmap) and the app exits.
/// --rect-file &lt;txt&gt;: "x y w h" of the window in physical pixels (DWMWA_EXTENDED_FRAME_BOUNDS, w and h even), for gif.ps1.
/// </summary>
public static class Capture
{
    /// <summary><paramref name="ready"/> runs once the window is on screen and the rect is written (autoplay starts there).</summary>
    public static void Attach(Window w, Action ready)
    {
        var a = Args.Current;
        w.ContentRendered += async (_, _) =>
        {
            await Task.Delay(500);
            if (a.RectFile is not null) WriteRect(w, a.RectFile);
            if (a.Screenshot is not null)
            {
                Save(w, a.Screenshot);
                App.Quit(confirm: false);
                return;
            }
            ready();
        };
    }

    static void Save(Window w, string path)
    {
        var bmp = new RenderTargetBitmap((int)Math.Round(w.ActualWidth * 2), (int)Math.Round(w.ActualHeight * 2), 192, 192, PixelFormats.Pbgra32);
        bmp.Render(w);
        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(bmp));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var f = File.Create(path);
        png.Save(f);
    }

    static unsafe void WriteRect(Window w, string path)
    {
        RECT r;
        PInvoke.DwmGetWindowAttribute(new HWND(new WindowInteropHelper(w).Handle), DWMWINDOWATTRIBUTE.DWMWA_EXTENDED_FRAME_BOUNDS, &r, (uint)sizeof(RECT)).ThrowOnFailure();
        AtomicFile.WriteAllText(path, $"{r.left} {r.top} {(r.right - r.left) & ~1} {(r.bottom - r.top) & ~1}"); // the script never reads half a file
    }
}
