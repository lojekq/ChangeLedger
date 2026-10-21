using System.Runtime.InteropServices;

namespace ChangeLedger.Features;

internal static partial class Native
{
    /// <summary>"@%SystemRoot%\system32\audiosrv.dll,-200" → the localized string; null when it can't be resolved.</summary>
    public static unsafe string? IndirectString(string source)
    {
        const int Size = 1024;
        var buf = stackalloc char[Size];
        return SHLoadIndirectString(source, buf, Size, 0) == 0 ? new string(buf) : null;
    }

    [LibraryImport("shlwapi.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static unsafe partial int SHLoadIndirectString(string pszSource, char* pszOutBuf, uint cchOutBuf, nint ppvReserved);
}
