using System.Text.Json.Serialization;

namespace DotCraft.Sessions.Wire;

public sealed record ContextUsageBreakdown
{
    public required ContextUsageSnapshot ContextUsage { get; init; }

    public required IReadOnlyList<ContextUsageCategory> Categories { get; init; }
}

public sealed record ContextUsageCategory
{
    public required string Id { get; init; }

    public long Tokens { get; init; }

    public bool OnDemand { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<ContextUsageItem>? Items { get; init; }
}

public sealed record ContextUsageItem(string Name, long Tokens);

public static class ContextUsageCategoryIds
{
    public const string SystemPrompt = "systemPrompt";
    public const string MemoryFiles = "memoryFiles";
    public const string Skills = "skills";
    public const string Subagents = "subagents";
    public const string BuiltInTools = "builtInTools";
    public const string McpTools = "mcpTools";
    public const string Messages = "messages";
}
