using System.Security.Cryptography;
using System.Text;

namespace DotCraft.Tools;

internal static class GitBlobOid
{
    internal const string Zero = "0000000000000000000000000000000000000000";

    internal static string Compute(string text)
    {
        var content = Encoding.UTF8.GetBytes(text);
        using var sha1 = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
        sha1.AppendData(Encoding.ASCII.GetBytes($"blob {content.Length}\0"));
        sha1.AppendData(content);
        return Convert.ToHexStringLower(sha1.GetHashAndReset());
    }
}
