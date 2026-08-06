using Microsoft.Extensions.AI;

namespace DotCraft.Agents;

/// <summary>Provider-neutral reference to a deferred tool definition.</summary>
public sealed class DeferredToolReferenceContent(string toolName) : AIContent
{
    public string ToolName { get; } = string.IsNullOrWhiteSpace(toolName)
        ? throw new ArgumentException("A tool name is required.", nameof(toolName))
        : toolName.Trim();
}
