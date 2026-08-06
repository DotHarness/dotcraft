using System.Collections.Frozen;
using System.Security.Cryptography;
using System.Text;

namespace DotCraft.Tools;

/// <summary>Projects canonical identities to deterministic provider-safe flat call names.</summary>
public static class ProviderToolProjector
{
    /// <summary>The maximum UTF-8 byte length of a projected provider flat name.</summary>
    public const int MaximumNameBytes = 64;

    /// <summary>Projects a complete canonical name set.</summary>
    public static IReadOnlyDictionary<ToolName, string> Project(IReadOnlyCollection<ToolName> names)
    {
        ArgumentNullException.ThrowIfNull(names);
        if (names.Count != names.Distinct().Count())
            throw new ArgumentException("Canonical names must be unique before provider projection.", nameof(names));

        var candidates = names.ToDictionary(name => name, CreateCandidate);
        var collisionNames = candidates
            .GroupBy(pair => pair.Value, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .SelectMany(group => group.Select(pair => pair.Key))
            .ToHashSet();

        return names
            .OrderBy(name => name.Namespace, StringComparer.Ordinal)
            .ThenBy(name => name.Name, StringComparer.Ordinal)
            .ToFrozenDictionary(
                name => name,
                name => collisionNames.Contains(name) || Encoding.UTF8.GetByteCount(candidates[name]) > MaximumNameBytes
                    ? AppendIdentityHash(candidates[name], name)
                    : candidates[name]);
    }

    private static string CreateCandidate(ToolName name)
    {
        var raw = name.Namespace is null ? name.Name : $"{name.Namespace}__{name.Name}";
        var builder = new StringBuilder(raw.Length);
        foreach (var character in raw)
            builder.Append(IsProviderNameCharacter(character) ? character : '_');
        return builder.Length == 0 ? "_" : builder.ToString();
    }

    private static bool IsProviderNameCharacter(char value) =>
        value is >= 'a' and <= 'z'
            or >= 'A' and <= 'Z'
            or >= '0' and <= '9'
            or '_';

    private static string AppendIdentityHash(string candidate, ToolName name)
    {
        var identity = $"{name.Namespace}\0{name.Name}";
        var hash = Convert.ToHexStringLower(SHA1.HashData(Encoding.UTF8.GetBytes(identity)))[..12];
        const int suffixLength = 13;
        var prefixLength = Math.Min(candidate.Length, MaximumNameBytes - suffixLength);
        return $"{candidate[..prefixLength]}_{hash}";
    }
}
