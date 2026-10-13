namespace DotCraft.Tools;

/// <summary>
/// Marks a tool method for source generation and the built-in tool catalog.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public class ToolAttribute : Attribute
{
    /// <summary>
    /// Overrides the model-visible tool name. The method name is used when omitted.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// Maximum tool result length in characters before spill-to-disk. Use <c>-1</c> for the global default
    /// from configuration, and <c>0</c> for unlimited (no spill for size).
    /// </summary>
    public int MaxResultChars { get; set; } = -1;

    /// <summary>
    /// Whether this runtime implementation should be listed in the global built-in tool catalog.
    /// Alternate implementations can opt out while preserving runtime metadata.
    /// </summary>
    public bool CatalogVisible { get; set; } = true;
}

/// <summary>
/// Marks a generated native tool as eligible for Remote Tool Host export and runtime routing.
/// This marker does not grant authority or affect ordinary CLR method calls.
/// </summary>
[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public sealed class ToolRpcAttribute : Attribute;

/// <summary>
/// Selects the trusted local renderer for a generated tool. Provenance decides which renderers may
/// honor it, so the same declaration on a plugin tool selects only plugin renderers.
/// </summary>
[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public sealed class ToolPresentationAttribute(string id) : Attribute
{
    public string Id { get; } = id;

    /// <summary>Sets the renderer's <c>operation</c> option.</summary>
    public string? Operation { get; set; }
}

/// <summary>Adds one bounded string option to a method's <see cref="ToolPresentationAttribute"/>.</summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
public sealed class ToolPresentationOptionAttribute(string key, string value) : Attribute
{
    public string Key { get; } = key;

    public string Value { get; } = value;
}
