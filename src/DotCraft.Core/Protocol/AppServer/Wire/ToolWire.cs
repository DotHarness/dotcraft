namespace DotCraft.Protocol.AppServer;

// ───── tool/* (spec Section 18A) ─────

public sealed class ToolListParams
{
    /// <summary>
    /// Optional operational-mode filter. When <c>"plan"</c>, only Plan-available tools are returned.
    /// When omitted or <c>"agent"</c>, the full built-in catalog is returned with each tool's
    /// <see cref="ToolInfoWire.PlanMode"/> annotation. Unknown values are treated as <c>"agent"</c>.
    /// </summary>
    public string? Mode { get; set; }
}

/// <summary>
/// Wire projection of a built-in tool for <c>tool/list</c>.
/// </summary>
public sealed class ToolInfoWire
{
    /// <summary>Canonical model-visible tool name, used in Agent Profile <c>tools.allow</c> / <c>tools.deny</c>.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Human-readable description from the tool method metadata. May be empty.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>Display icon (emoji). Falls back to a generic glyph when the tool declares none.</summary>
    public string Icon { get; set; } = string.Empty;

    /// <summary>Tool origin. Always <c>"builtin"</c> for tools returned by <c>tool/list</c>.</summary>
    public string Source { get; set; } = "builtin";

    /// <summary>
    /// True when the tool is allowed while the thread is in Plan (read-only) mode. False for mutating
    /// tools that Plan mode hard-denies. Tools that are only conditionally restricted in Plan mode
    /// (for example shell <c>Exec</c>) report true.
    /// </summary>
    public bool PlanMode { get; set; }
}

public sealed class ToolListResult
{
    public List<ToolInfoWire> Tools { get; set; } = [];
}
