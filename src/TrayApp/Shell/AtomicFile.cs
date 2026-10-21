using System.IO;
using System.Text;

namespace TrayApp.Shell;

/// <summary>Crash-safe writes for settings and state files.</summary>
public static class AtomicFile
{
    /// <summary>.tmp → Flush(true) → File.Replace (old copy kept as .bak), or File.Move when the file does not exist yet.</summary>
    public static void WriteAllText(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var tmp = path + ".tmp";
        using (var f = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            f.Write(new UTF8Encoding(false).GetBytes(text));
            f.Flush(flushToDisk: true);
        }
        if (File.Exists(path)) File.Replace(tmp, path, path + ".bak");
        else File.Move(tmp, path);
    }

    /// <summary>
    /// Reads and parses <paramref name="path"/>. A file that does not parse is renamed to *.bad-&lt;time&gt;
    /// and the .bak copy is tried. Null when neither gives a value; a missing file is not an error.
    /// I/O errors (file locked) are thrown: treating them as corruption would overwrite good data.
    /// </summary>
    public static T? Read<T>(string path, Func<string, T?> parse) where T : class
    {
        foreach (var p in new[] { path, path + ".bak" })
        {
            if (!File.Exists(p)) continue;
            var text = File.ReadAllText(p);
            try
            {
                if (parse(text) is { } value) return value;
                throw new InvalidDataException("empty");
            }
            catch (Exception e) when (e is not IOException and not UnauthorizedAccessException)
            {
                var bad = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(p))!,
                    $"{Path.GetFileNameWithoutExtension(p)}.bad-{DateTime.Now:yyyyMMdd-HHmmss}{Path.GetExtension(p)}");
                File.Move(p, bad, overwrite: true);
                Log.Warn($"{Path.GetFileName(p)} is corrupt, renamed to {Path.GetFileName(bad)}", e);
            }
        }
        return null;
    }
}
