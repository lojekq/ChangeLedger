using System.Net.Http;
using System.Text.Json;
using System.Windows;

namespace TrayApp.Shell;

/// <summary>
/// At start and every 24 h: GET releases/latest of AppInfo.Repo (no pre-releases, no drafts). Never downloads anything,
/// errors go to the log only. Off in Debug and under --demo: 8 parallel sessions would eat the 60 requests/h anonymous limit.
/// </summary>
public static class Updates
{
#if DEBUG
    static readonly bool IsDebug = true;
#else
    static readonly bool IsDebug = false;
#endif

    /// <summary>Raised on the UI thread with the version ("0.2.0") and the release page.</summary>
    public static event Action<string, string>? Found;

    public static void Start()
    {
        if (IsDebug || Args.Current.Demo) return;
        _ = Loop();
    }

    static async Task Loop()
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"{AppInfo.Name}/{AppInfo.Version}");
        http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        while (true)
        {
            if (Settings.Current.CheckUpdates)
            {
                try
                {
                    using var doc = JsonDocument.Parse(await http.GetStringAsync($"https://api.github.com/repos/{AppInfo.Repo}/releases/latest"));
                    var tag = doc.RootElement.GetProperty("tag_name").GetString() ?? "";
                    var url = doc.RootElement.GetProperty("html_url").GetString() ?? "";
                    if (IsNewer(tag, AppInfo.Version))
                        Application.Current.Dispatcher.Invoke(() => Found?.Invoke(tag.TrimStart('v'), url));
                }
                catch (Exception e) { Log.Warn($"update check failed: {e.GetType().Name}"); }
            }
            await Task.Delay(TimeSpan.FromHours(24));
        }
    }

    /// <summary>"v0.2.0" vs "0.1.0" → true. Same numbers: a release is newer than a pre-release of it ("0.2.0-beta.1", "0.0.0-dev").</summary>
    public static bool IsNewer(string tag, string current)
    {
        static (Version? Num, bool Pre) Split(string v)
        {
            var dash = v.IndexOf('-');
            return (Version.TryParse((dash < 0 ? v : v[..dash]).TrimStart('v'), out var n) ? n : null, dash >= 0);
        }
        var (t, tPre) = Split(tag);
        var (c, cPre) = Split(current);
        if (t is null || c is null) return false;
        var cmp = t.CompareTo(c);
        return cmp > 0 || (cmp == 0 && cPre && !tPre);
    }
}
