namespace DotCraft.Configuration;

/// <summary>
/// Represents a configuration section as used by the Dashboard config UI.
/// Maps to a single accordion panel in the settings page.
/// </summary>
public sealed class ConfigSchemaSection
{
    /// <summary>
    /// Display name shown in the Dashboard UI (e.g. "QQ Bot", "Tools › Shell").
    /// </summary>
    public required string Section { get; init; }

    /// <summary>
    /// Sort order for Dashboard UI rendering. Lower values appear first.
    /// Sourced from <see cref="ConfigSectionAttribute.Order"/>.
    /// </summary>
    public int Order { get; init; }

    /// <summary>
    /// JSON path segments from the root of config.json to this section.
    /// null or empty array means root-level fields.
    /// Example: ["Tools", "Shell"] means config.Tools.Shell
    /// </summary>
    public string[]? Path { get; init; }

    /// <summary>
    /// For top-level collection keys (e.g. McpServers, ExternalChannels), the root key name.
    /// When set, <see cref="Path"/> is ignored.
    /// </summary>
    public string? RootKey { get; init; }

    /// <summary>
    /// When non-null, this section is a homogeneous object-list collection at <see cref="RootKey"/>;
    /// each element's shape is described by these fields (structured list editor in the Dashboard).
    /// </summary>
    public List<ConfigSchemaField>? ItemFields { get; init; }

    /// <summary>
    /// Fields in this section (empty when <see cref="ItemFields"/> is used).
    /// </summary>
    public required List<ConfigSchemaField> Fields { get; init; }
}

/// <summary>
/// Represents a single editable field within a config section.
/// </summary>
public sealed class ConfigSchemaField
{
    /// <summary>
    /// The JSON property name (PascalCase, case-insensitive match in the Dashboard).
    /// </summary>
    public required string Key { get; init; }

    /// <summary>
    /// Human-friendly display name shown as the field label in the Dashboard UI.
    /// Falls back to a PascalCase-split version of <see cref="Key"/> when null.
    /// </summary>
    public string? DisplayName { get; init; }

    /// <summary>
    /// The field type for the Dashboard UI renderer.
    /// Valid values: "text", "number", "bool", "password", "select", "textarea", "json",
    /// "stringList" (List&lt;string&gt;, one line per value), "keyValueMap" (Dictionary&lt;string,string&gt;).
    /// </summary>
    public required string Type { get; init; }

    /// <summary>
    /// When true, the value is masked as "***" in Dashboard API responses.
    /// </summary>
    public bool Sensitive { get; init; }

    /// <summary>
    /// Valid option values for "select" type fields.
    /// </summary>
    public string[]? Options { get; init; }

    /// <summary>
    /// Minimum value constraint for "number" type fields.
    /// </summary>
    public int? Min { get; init; }

    /// <summary>
    /// Maximum value constraint for "number" type fields.
    /// </summary>
    public int? Max { get; init; }

    /// <summary>
    /// Help text displayed below the field in the UI.
    /// </summary>
    public string? Hint { get; init; }

    /// <summary>
    /// Declares when this field takes effect after updates.
    /// </summary>
    public ReloadBehavior Reload { get; init; } = ReloadBehavior.ProcessRestart;

    /// <summary>
    /// Optional subsystem key used when <see cref="Reload"/> is
    /// <see cref="ReloadBehavior.SubsystemRestart"/>.
    /// </summary>
    public string? SubsystemKey { get; init; }

    /// <summary>
    /// Default value shown in the UI when no value is configured.
    /// Should match the C# default for the property.
    /// </summary>
    public object? DefaultValue { get; init; }
}
