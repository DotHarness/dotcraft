using System.Text.Json.Serialization;

namespace DotCraft.Skills;

/// <summary>
/// Status of a workspace-local skill variant.
/// </summary>
public static class SkillVariantStatus
{
    public const string Current = "current";
    public const string Stale = "stale";
    public const string Restored = "restored";
    public const string Superseded = "superseded";
}

/// <summary>
/// Minimal manifest persisted beside a skill variant snapshot.
/// </summary>
public sealed class SkillVariantManifest
{
    public int SchemaVersion { get; set; } = 1;

    public string VariantId { get; set; } = string.Empty;

    public SkillVariantSource Source { get; set; } = new();

    public SkillVariantTarget Target { get; set; } = new();

    public string Status { get; set; } = SkillVariantStatus.Current;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public string? ParentVariantId { get; set; }

    public SkillVariantProvenance Provenance { get; set; } = new();

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Summary { get; set; }
}

public sealed class SkillVariantSource
{
    public string Name { get; set; } = string.Empty;

    public string SourceKind { get; set; } = string.Empty;

    public string Path { get; set; } = string.Empty;

    public string Fingerprint { get; set; } = string.Empty;
}

public sealed class SkillVariantTarget
{
    public string Harness { get; set; } = "dotcraft";

    public string HarnessVersion { get; set; } = "0.0.0";

    public string Model { get; set; } = string.Empty;

    public string Os { get; set; } = string.Empty;

    public string Shell { get; set; } = string.Empty;

    public string Sandbox { get; set; } = string.Empty;

    public string ToolProfileHash { get; set; } = string.Empty;

    public string ApprovalPolicy { get; set; } = string.Empty;

    public string WorkspaceHash { get; set; } = string.Empty;
}

public sealed class SkillVariantProvenance
{
    public string Kind { get; set; } = "selfLearning";
}

/// <summary>
/// Resolved source-or-variant skill file.
/// </summary>
public sealed record EffectiveSkill(string Name, string Content, string Path, string Origin);
