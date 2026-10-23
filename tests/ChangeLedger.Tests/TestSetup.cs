using System.IO;
using ChangeLedger.Shell;

namespace ChangeLedger.Tests;

[TestClass]
public static class TestSetup
{
    /// <summary>
    /// Tests run as a dev instance in any configuration (CI runs -c Release): data in TEMP, own mutex, never the installed copy's folders.
    /// Set before anything touches AppInfo. Tests of corrupt files log warnings; logs stay off.
    /// </summary>
    [AssemblyInitialize]
    public static void Init(TestContext _)
    {
        Environment.SetEnvironmentVariable(AppInfo.Name.ToUpperInvariant() + "_DATA_DIR", Path.Combine(Path.GetTempPath(), AppInfo.Name + ".Tests"));
        Log.Off = true;
    }
}
