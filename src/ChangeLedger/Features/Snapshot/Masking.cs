using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace ChangeLedger.Features;

/// <summary>
/// Secrets never reach the disk: values become "•••• sha256:&lt;8 hex&gt;" (salted), so a change stays visible and the value does not.
/// </summary>
public static partial class Masking
{
    public const string Mark = "••••";

    public static bool IsSecretName(string name, IEnumerable<string> extra) =>
        SecretName().IsMatch(name) || extra.Any(e => string.Equals(e.Trim(), name, StringComparison.OrdinalIgnoreCase));

    public static string Hash(string value, byte[] salt) =>
        $"{Mark} sha256:{Convert.ToHexString(SHA256.HashData([.. salt, .. Encoding.UTF8.GetBytes(value)]))[..8].ToLowerInvariant()}";

    /// <summary>"--token=abc", "-ApiKey:abc", "password abc" in command lines.</summary>
    public static string? CommandLine(string? cmd, byte[] salt) =>
        cmd is null ? null : SecretArg().Replace(cmd, m => m.Groups[1].Value + m.Groups[2].Value + Hash(m.Groups[3].Value, salt));

    [GeneratedRegex("(?i)(KEY|TOKEN|SECRET|PASS|PWD|CRED|AUTH)")] private static partial Regex SecretName();
    [GeneratedRegex(@"(?i)(token|key|secret|password|pwd)([=: ])(\S+)")] private static partial Regex SecretArg();
}
