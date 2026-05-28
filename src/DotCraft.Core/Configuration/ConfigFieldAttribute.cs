namespace DotCraft.Configuration;

/// <summary>
/// Annotates a config property with metadata for the Dashboard schema builder.
/// When omitted, the builder infers all properties from the property's type and default value.
/// </summary>
[AttributeUsage(AttributeTargets.Property, Inherited = false)]
public sealed class ConfigFieldAttribute : Attribute
{
    /// <summary>
    /// When true, this field will not appear in the Dashboard config UI.
    /// Use this for complex nested sub-sections that have their own <see cref="ConfigSectionAttribute"/>,
    /// or for internal properties that should not be user-editable.
    /// </summary>
    public bool Ignore { get; set; }

    /// <summary>
    /// Human-friendly display name for the field label in the Dashboard UI.
    /// When not set, the Dashboard auto-generates a label by splitting the PascalCase property name.
    /// </summary>
    public string? DisplayName { get; set; }

    /// <summary>
    /// When true, the field value is masked as "***" in the Dashboard API response.
    /// The field is rendered as a password input in the UI.
    /// </summary>
    public bool Sensitive { get; set; }

    /// <summary>
    /// Help text displayed below the field in the Dashboard UI.
    /// </summary>
    public string? Hint { get; set; }

    /// <summary>
    /// Minimum value for numeric fields. When not set, no minimum is enforced.
    /// </summary>
    public int Min { get; set; } = int.MinValue;

    /// <summary>
    /// Maximum value for numeric fields. When not set, no maximum is enforced.
    /// </summary>
    public int Max { get; set; } = int.MaxValue;

    /// <summary>
    /// Explicitly override the inferred field type.
    /// Valid values: "text", "number", "bool", "password", "select", "textarea", "json".
    /// When not set, the type is inferred from the property's CLR type.
    /// </summary>
    public string? FieldType { get; set; }

    /// <summary>
    /// Explicit list of options for "select" type fields whose property type is string (not an enum).
    /// Not required for enum properties — their options are inferred automatically.
    /// Example: [nameof(Options), "allow", "deny", "custom"]
    /// Note: C# attributes only support array constants, so use params-style:
    ///   [ConfigField(FieldType = "select", Options = new[] { "allow", "deny", "custom" })]
    /// </summary>
    public string[]? Options { get; set; }

    /// <summary>
    /// Declares when a field change is applied.
    /// </summary>
    public ReloadBehavior Reload { get; set; } = ReloadBehavior.ProcessRestart;

    /// <summary>
    /// Indicates whether <see cref="Reload"/> is explicitly set on this field.
    /// </summary>
    public bool HasReload { get; set; }

    /// <summary>
    /// Optional subsystem key used when <see cref="Reload"/> is
    /// <see cref="ReloadBehavior.SubsystemRestart"/>.
    /// </summary>
    public string? SubsystemKey { get; set; }
}
