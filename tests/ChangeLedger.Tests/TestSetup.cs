using ChangeLedger.Shell;

namespace ChangeLedger.Tests;

[TestClass]
public static class TestSetup
{
    /// <summary>Tests of corrupt files log warnings; they must not create %APPDATA%\ChangeLedger\logs on the machine.</summary>
    [AssemblyInitialize]
    public static void Init(TestContext _) => Log.Off = true;
}
