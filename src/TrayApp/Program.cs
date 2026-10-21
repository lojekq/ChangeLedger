using TrayApp.Shell;

namespace TrayApp;

public static class Program
{
    // App verbs (00-shared §2.3): run before the mutex and without a WPF Application,
    // return the exit code and never write to the console (stdio belongs to the verb, e.g. --mcp).
    // Example: ["--snapshot"] = Snapshot.Run,
    internal static readonly Dictionary<string, Func<string[], int>> Verbs = new()
    {
    };

    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Length > 0 && Verbs.TryGetValue(args[0], out var verb))
        {
            Log.Verb = args[0].TrimStart('-');
            try { return verb(args[1..]); }
            catch (Exception e) { Log.Error($"{args[0]} failed", e); return 1; }
        }
        Args.Current = Args.Parse(args);
        var app = new App();
        app.InitializeComponent();
        return app.Run();
    }
}
