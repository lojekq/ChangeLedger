namespace TrayApp.Shell;

/// <summary>UI flags (00-shared §2.3). Verbs are handled earlier, in Program.Main.</summary>
public sealed class Args
{
    public static Args Current { get; set; } = new();

    public bool Background { get; private set; }
    public bool Demo { get; private set; }
    public bool Autoplay { get; private set; }
    public string? Lang { get; private set; }        // en | ru
    public string? Theme { get; private set; }       // light | dark
    public string? Page { get; private set; }
    public int Width { get; private set; }           // --size 1100x700, 0 = window default
    public int Height { get; private set; }
    public string? Screenshot { get; private set; }  // png path
    public string? RectFile { get; private set; }    // txt path
    public List<string> Unknown { get; } = [];

    public static Args Parse(IReadOnlyList<string> a)
    {
        var r = new Args();
        for (var i = 0; i < a.Count; i++)
        {
            var flag = a[i];
            string? Value() => i + 1 < a.Count ? a[++i] : null;
            string? Bad() { r.Unknown.Add(flag); return null; }
            switch (flag)
            {
                case "--background": r.Background = true; break;
                case "--demo": r.Demo = true; break;
                case "--autoplay": r.Autoplay = true; break;
                case "--lang": r.Lang = Value() is "en" or "ru" ? a[i] : Bad(); break;
                case "--theme": r.Theme = Value() is "light" or "dark" ? a[i] : Bad(); break;
                case "--page": r.Page = Value() ?? Bad(); break;
                case "--screenshot": r.Screenshot = Value() ?? Bad(); break;
                case "--rect-file": r.RectFile = Value() ?? Bad(); break;
                case "--size":
                    if (Value()?.Split('x') is [var w, var h] && int.TryParse(w, out var wi) && int.TryParse(h, out var hi) && wi > 0 && hi > 0)
                        (r.Width, r.Height) = (wi, hi);
                    else Bad();
                    break;
                default: r.Unknown.Add(flag); break;
            }
        }
        return r;
    }
}
