using System.Security.Cryptography;
using System.Text;
using DotCraft.Tools;

namespace DotCraft.Mcp;

/// <summary>Describes one raw MCP tool identity before model-visible normalization.</summary>
internal readonly record struct McpToolIdentityInput
{
    /// <summary>Creates an MCP tool identity input.</summary>
    public McpToolIdentityInput(string runtimeName, string? declaredName, string rawToolName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runtimeName);
        ArgumentException.ThrowIfNullOrWhiteSpace(rawToolName);
        if (declaredName is not null)
            ArgumentException.ThrowIfNullOrWhiteSpace(declaredName);

        RuntimeName = runtimeName;
        DeclaredName = declaredName;
        RawToolName = rawToolName;
    }

    /// <summary>Gets the collision-free MCP runtime name used for routing.</summary>
    public string RuntimeName { get; }

    /// <summary>Gets the user-facing declared server name, when available.</summary>
    public string? DeclaredName { get; }

    /// <summary>Gets the raw MCP tool name used for protocol calls.</summary>
    public string RawToolName { get; }
}

/// <summary>Maps one raw MCP identity to its model-visible composite and flat names.</summary>
internal readonly record struct McpToolIdentity(
    string RuntimeName,
    string? DeclaredName,
    string RawToolName,
    ToolName ToolName,
    string FlatName);

/// <summary>Normalizes raw MCP identities into deterministic provider-safe model identities.</summary>
public static class McpToolNaming
{
    /// <summary>The maximum byte length of a flattened provider tool name.</summary>
    internal const int MaxFlatNameLength = 64;

    private const int MaxNamespaceLength = 49;
    private const int HashLength = 12;
    private const string NamespacePrefix = "mcp__";
    private const string FlatDelimiter = "__";

    /// <summary>Returns a provider-safe MCP namespace for a single server identity.</summary>
    public static string CanonicalNamespace(string serverName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serverName);
        return NormalizeBatch([new McpToolIdentityInput(serverName, null, "_")])[0]
            .ToolName.Namespace!;
    }

    /// <summary>Creates a provider-safe MCP tool name when no cross-server collision context exists.</summary>
    public static ToolName CanonicalToolName(string serverName, string rawToolName) =>
        NormalizeBatch([new McpToolIdentityInput(serverName, null, rawToolName)])[0].ToolName;

    /// <summary>
    /// Normalizes a complete MCP inventory. The result preserves input order, while collision
    /// resolution is independent of enumeration order.
    /// </summary>
    internal static IReadOnlyList<McpToolIdentity> NormalizeBatch(
        IEnumerable<McpToolIdentityInput> identities)
    {
        ArgumentNullException.ThrowIfNull(identities);
        var inputs = identities.ToArray();
        if (inputs.Length == 0)
            return [];

        ValidateServerDeclarations(inputs);

        var namespaceBases = inputs
            .Select(static input => new NamespaceCandidate(
                input.RuntimeName,
                NamespacePrefix + SanitizeComponent(input.DeclaredName ?? input.RuntimeName),
                NamespacePrefix + (input.DeclaredName ?? input.RuntimeName)))
            .Distinct()
            .ToArray();
        var collidingNamespaceBases = namespaceBases
            .GroupBy(static candidate => candidate.Base, StringComparer.Ordinal)
            .Where(static group => group.Select(static candidate => candidate.RuntimeName)
                .Distinct(StringComparer.Ordinal).Skip(1).Any())
            .Select(static group => group.Key)
            .ToHashSet(StringComparer.Ordinal);
        var namespaces = ResolveNamespaces(namespaceBases, collidingNamespaceBases);

        var candidates = inputs
            .Select(input => new ToolCandidate(
                namespaces[input.RuntimeName],
                SanitizeComponent(input.RawToolName),
                input.RuntimeName + "\0" + input.RawToolName,
                input.RawToolName))
            .ToArray();
        var collidingToolBases = candidates
            .GroupBy(static candidate => (candidate.Namespace, candidate.Base))
            .Where(static group => group.Select(static candidate => candidate.RawIdentity)
                .Distinct(StringComparer.Ordinal).Skip(1).Any())
            .Select(static group => group.Key)
            .ToHashSet();
        var resolvedTools = ResolveTools(candidates, collidingToolBases);

        return inputs.Select(input =>
        {
            var rawIdentity = input.RuntimeName + "\0" + input.RawToolName;
            var toolName = resolvedTools[rawIdentity];
            return new McpToolIdentity(
                input.RuntimeName,
                input.DeclaredName,
                input.RawToolName,
                toolName,
                $"{toolName.Namespace}{FlatDelimiter}{toolName.Name}");
        }).ToArray();
    }

    private static void ValidateServerDeclarations(IReadOnlyList<McpToolIdentityInput> inputs)
    {
        foreach (var group in inputs.GroupBy(static input => input.RuntimeName, StringComparer.Ordinal))
        {
            var declarations = group.Select(static input => input.DeclaredName)
                .Distinct(StringComparer.Ordinal)
                .Take(2)
                .ToArray();
            if (declarations.Length > 1)
            {
                throw new ArgumentException(
                    $"MCP runtime server '{group.Key}' has inconsistent declared names.",
                    nameof(inputs));
            }
        }
    }

    private static Dictionary<string, string> ResolveNamespaces(
        IReadOnlyList<NamespaceCandidate> candidates,
        IReadOnlySet<string> collidingBases)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        var used = new HashSet<string>(StringComparer.Ordinal);
        foreach (var candidate in candidates.OrderBy(static candidate => candidate.RuntimeName, StringComparer.Ordinal))
        {
            var collides = collidingBases.Contains(candidate.Base);
            var value = collides || candidate.Base.Length > MaxNamespaceLength
                ? FitWithHash(
                    candidate.Base,
                    collides ? candidate.RuntimeName : candidate.RawSeed,
                    MaxNamespaceLength)
                : candidate.Base;
            if (!used.Add(value))
                throw new InvalidOperationException($"MCP namespace normalization produced duplicate '{value}'.");

            result.Add(candidate.RuntimeName, value);
        }

        return result;
    }

    private static Dictionary<string, ToolName> ResolveTools(
        IReadOnlyList<ToolCandidate> candidates,
        IReadOnlySet<(string Namespace, string Base)> collidingBases)
    {
        var result = new Dictionary<string, ToolName>(StringComparer.Ordinal);
        var used = new HashSet<string>(StringComparer.Ordinal);
        foreach (var candidate in candidates
                     .GroupBy(static candidate => candidate.RawIdentity, StringComparer.Ordinal)
                     .Select(static group => group.First())
            .OrderBy(static candidate => candidate.RawIdentity, StringComparer.Ordinal))
        {
            var maxToolLength = MaxFlatNameLength - candidate.Namespace.Length - FlatDelimiter.Length;
            var collides = collidingBases.Contains((candidate.Namespace, candidate.Base));
            var localName = collides || candidate.Base.Length > maxToolLength
                ? FitWithHash(
                    candidate.Base,
                    collides ? candidate.RawIdentity : candidate.RawSeed,
                    maxToolLength)
                : candidate.Base;
            var flatName = candidate.Namespace + FlatDelimiter + localName;
            if (!used.Add(flatName))
                throw new InvalidOperationException($"MCP tool normalization produced duplicate '{flatName}'.");

            result.Add(candidate.RawIdentity, new ToolName(candidate.Namespace, localName));
        }

        return result;
    }

    private static string SanitizeComponent(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var rune in value.EnumerateRunes())
        {
            var isSafe = rune.Value <= char.MaxValue
                         && ((char)rune.Value == '_' || char.IsAsciiLetterOrDigit((char)rune.Value));
            builder.Append(isSafe ? (char)rune.Value : '_');
        }
        return builder.Length == 0 ? "_" : builder.ToString();
    }

    private static string FitWithHash(string value, string rawIdentity, int maximumLength)
    {
        var suffix = "_" + Sha1Prefix(rawIdentity);
        var prefixLength = Math.Max(0, maximumLength - suffix.Length);
        return value[..Math.Min(value.Length, prefixLength)] + suffix;
    }

    private static string Sha1Prefix(string value)
    {
        var hash = SHA1.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(hash).ToLowerInvariant()[..HashLength];
    }

    private readonly record struct NamespaceCandidate(string RuntimeName, string Base, string RawSeed);

    private readonly record struct ToolCandidate(
        string Namespace,
        string Base,
        string RawIdentity,
        string RawSeed);
}
