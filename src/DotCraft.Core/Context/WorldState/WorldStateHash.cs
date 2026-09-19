using System.Security.Cryptography;
using System.Text;

namespace DotCraft.Context.WorldState;

public static class WorldStateHash
{
    public static string Of(string value)
    {
        var normalized = value.Replace("\r\n", "\n", StringComparison.Ordinal);
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexStringLower(digest)[..32];
    }
}
