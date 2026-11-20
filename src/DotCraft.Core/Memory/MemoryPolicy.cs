using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using DotCraft.Context;

namespace DotCraft.Memory;

public sealed class MemoryTypeDefinition
{
    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public bool Personal { get; set; }
}

public sealed record MemoryPolicy(
    IReadOnlyList<MemoryTypeDefinition> Types,
    string SaveGuidance,
    string ExtractionGuidance,
    string ConsolidationGuidance,
    int ExtractionInterval)
{
    public static MemoryPolicy Default { get; } = new(
        MemoryPolicyDefaults.CreateTypes(),
        MemoryPolicyDefaults.SaveGuidance,
        MemoryPolicyDefaults.ExtractionGuidance,
        MemoryPolicyDefaults.ConsolidationGuidance,
        MemoryPolicyDefaults.ExtractionInterval);

    public static MemoryPolicy From(MemoryConfig config) => new(
        [.. config.Types],
        config.SaveGuidance,
        config.ExtractionGuidance,
        config.ConsolidationGuidance,
        config.ExtractionInterval);

    public IReadOnlySet<string> TypeNames => Types.Select(static type => type.Name).ToHashSet(StringComparer.Ordinal);

    public IReadOnlyList<string> PersonalTypeNames => [.. Types.Where(static type => type.Personal).Select(static type => type.Name)];

    public string InstructionsFingerprint => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
        string.Join("\n", Types.Select(static type => $"{type.Name}\t{type.Description}\t{type.Personal}")) + "\n\0" + SaveGuidance)));
}

public static partial class MemoryPolicyRules
{
    public const int MaxTypes = 8;
    public const int MaxTypeNameLength = 40;
    public const int MaxTypeDescriptionLength = 200;
    public const int MaxGuidanceLength = 8000;
    public const int MinExtractionInterval = 1;
    public const int MaxExtractionInterval = 50;

    public static string? Validate(MemoryPolicy policy) =>
        ValidateTypes(policy.Types)
        ?? ValidateGuidance("Memory.SaveGuidance", policy.SaveGuidance)
        ?? ValidateGuidance("Memory.ExtractionGuidance", policy.ExtractionGuidance)
        ?? ValidateGuidance("Memory.ConsolidationGuidance", policy.ConsolidationGuidance)
        ?? ValidateInterval(policy.ExtractionInterval);

    public static string? ValidateTypes(IReadOnlyList<MemoryTypeDefinition>? types)
    {
        if (types is not { Count: > 0 and <= MaxTypes })
            return $"'Memory.Types' must hold between 1 and {MaxTypes} types.";
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var type in types)
        {
            if (type?.Name is not { Length: > 0 and <= MaxTypeNameLength } name || !SlugPattern().IsMatch(name))
                return $"Each 'Memory.Types' name must be a kebab-case slug of at most {MaxTypeNameLength} characters.";
            if (!names.Add(name))
                return $"'Memory.Types' lists '{name}' more than once.";
            if (string.IsNullOrWhiteSpace(type.Description)
                || type.Description.Length > MaxTypeDescriptionLength
                || type.Description.Contains('\n') || type.Description.Contains('\r'))
            {
                return $"The description of memory type '{name}' must be one line of at most {MaxTypeDescriptionLength} characters.";
            }
        }

        return null;
    }

    public static string? ValidateGuidance(string keyPath, string? guidance) =>
        string.IsNullOrWhiteSpace(guidance) || guidance.Length > MaxGuidanceLength
            ? $"'{keyPath}' must be non-empty text of at most {MaxGuidanceLength:N0} characters."
            : null;

    public static string? ValidateInterval(int interval) =>
        interval is < MinExtractionInterval or > MaxExtractionInterval
            ? $"'Memory.ExtractionInterval' must be between {MinExtractionInterval} and {MaxExtractionInterval}."
            : null;

    [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$")]
    private static partial Regex SlugPattern();
}
