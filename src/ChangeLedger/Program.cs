using System.IO;
using ChangeLedger.Shell;

namespace ChangeLedger;

public static class Program
{
    // App verbs (00-shared §2.3): run before the mutex and without a WPF Application,
    // return the exit code and never write to the console (stdio belongs to the verb, e.g. --mcp).
    internal static readonly Dictionary<string, Func<string[], int>> Verbs = new()
    {
        ["--snapshot"] = Features.Runner.Verb,
    };

    [STAThread]
    public static int Main(string[] args)
    {
        // --data-dir <path> = CHANGELEDGER_DATA_DIR, set before AppInfo is first touched; child processes inherit it
        var d = Array.IndexOf(args, "--data-dir");
        if (d >= 0 && d + 1 < args.Length)
        {
            Environment.SetEnvironmentVariable(AppInfo.Name.ToUpperInvariant() + "_DATA_DIR", Path.GetFullPath(args[d + 1]));
            args = [.. args[..d], .. args[(d + 2)..]];
        }
        if (args.Length > 0 && Verbs.TryGetValue(args[0], out var verb))
        {
            Log.Verb = args[0].TrimStart('-');
            try { return verb(args[1..]); }
            catch (Exception e) { Log.Error($"{args[0]} failed", e); return 1; }
        }
        Args.Current = Args.Parse(args);
        // Here, not in OnStartup: the UI culture is AsyncLocal, and a value set inside the async OnStartup
        // is undone when it awaits, so strings built later fell back to the Windows language
        Settings.Load();
        Loc.Apply(Settings.Current.Language);
        var app = new App();
        app.InitializeComponent();
        return app.Run();
    }
}
