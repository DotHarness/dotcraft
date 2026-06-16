using System.Text.Json.Serialization;

namespace DotCraft.Protocol.AppServer;


// ───── skills/* (spec Section 18) ─────

public sealed class SkillsListParams
{
    /// <summary>When false, skills with unmet requirements are excluded. Default true.</summary>
    public bool? IncludeUnavailable { get; set; }
}

public sealed class SkillsListResult
{
    public List<SkillInfoWire> Skills { get; set; } = [];
}

/// <summary>
/// Wire projection of <see cref="DotCraft.Skills.SkillsLoader.SkillInfo"/> for skills/list and skills/setEnabled.
/// </summary>
public sealed class SkillInfoWire
{
    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DisplayName { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ShortDescription { get; set; }

    public string Source { get; set; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PluginId { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PluginDisplayName { get; set; }

    public bool Available { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? UnavailableReason { get; set; }

    public bool Enabled { get; set; } = true;

    public string Path { get; set; } = string.Empty;

    /// <summary>
    /// True when the current runtime resolves this skill through a workspace variant.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool HasVariant { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? IconSmallDataUrl { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? IconLargeDataUrl { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DefaultPrompt { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, string>? Metadata { get; set; }
}

public sealed class SkillsReadParams
{
    public string Name { get; set; } = string.Empty;
}

public sealed class SkillsReadResult
{
    public string Name { get; set; } = string.Empty;

    public string Content { get; set; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, string>? Metadata { get; set; }
}

public sealed class SkillsViewParams
{
    public string Name { get; set; } = string.Empty;
}

public sealed class SkillsViewResult
{
    public string Name { get; set; } = string.Empty;

    public string Content { get; set; } = string.Empty;
}

public sealed class SkillsRestoreOriginalParams
{
    public string Name { get; set; } = string.Empty;
}

public sealed class SkillsRestoreOriginalResult
{
    public string Name { get; set; } = string.Empty;

    public bool Restored { get; set; }
}

public sealed class SkillsSetEnabledParams
{
    public string Name { get; set; } = string.Empty;

    public bool Enabled { get; set; }
}

public sealed class SkillsSetEnabledResult
{
    public SkillInfoWire Skill { get; set; } = new();
}

public sealed class SkillsUninstallParams
{
    public string Name { get; set; } = string.Empty;
}

public sealed class SkillsUninstallResult
{
    public string Name { get; set; } = string.Empty;

    public bool Uninstalled { get; set; }

    public string Source { get; set; } = string.Empty;

    public string RemovedSourcePath { get; set; } = string.Empty;

    public int RemovedVariantCount { get; set; }
}
