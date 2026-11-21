using System.Security.Cryptography;
using System.Text;

namespace DotCraft.Agents;

internal static class OpenAIResponsesLiteToolIdentity
{
    internal const string FunctionsNamespace = "functions";

    internal static string ProjectNamespace(string? localNamespace)
    {
        if (localNamespace is null)
            return FunctionsNamespace;

        return IsReservedFamily(localNamespace) ? localNamespace + "_" : localNamespace;
    }

    internal static string? ResolveNamespace(string wireNamespace) =>
        wireNamespace == FunctionsNamespace ? null :
        IsReservedFamily(wireNamespace) ? wireNamespace[..^1] : wireNamespace;

    internal static string RequiredSelector(string? localNamespace, string localName)
    {
        var identity = Encoding.UTF8.GetBytes($"{localNamespace}/{localName}");
        return $"selected-{Convert.ToHexStringLower(SHA256.HashData(identity))[..32]}";
    }

    private static bool IsReservedFamily(string value) =>
        value.StartsWith(FunctionsNamespace, StringComparison.Ordinal)
        && value.AsSpan(FunctionsNamespace.Length).IndexOfAnyExcept('_') < 0;
}
