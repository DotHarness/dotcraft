namespace DotCraft.Tools;

/// <summary>
/// Marks a tool method with display metadata: icon and an optional static formatter.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public class ToolAttribute : Attribute
{
    /// <summary>
    /// Emoji icon to display for this tool (e.g., "📄", "🔍").
    /// </summary>
    public string Icon { get; set; } = string.Empty;

    /// <summary>
    /// The type that declares the static display formatter method.
    /// Must be paired with <see cref="DisplayMethod"/>.
    /// </summary>
    public Type? DisplayType { get; set; }

    /// <summary>
    /// Name of a public static method on <see cref="DisplayType"/> with the signature:
    /// <c>static string MethodName(IDictionary&lt;string, object?&gt;? args)</c>
    /// </summary>
    public string? DisplayMethod { get; set; }

    /// <summary>
    /// Maximum tool result length in characters before spill-to-disk. Use <c>-1</c> for the global default
    /// from configuration, and <c>0</c> for unlimited (no spill for size).
    /// </summary>
    public int MaxResultChars { get; set; } = -1;
}
