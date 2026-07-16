using System.ComponentModel;
using System.Text.Json;
using DotCraft.GeneratedTools.Core;
using DotCraft.Memory;
using DotCraft.Protocol;
using Microsoft.Extensions.AI;

namespace DotCraft.Tools;

/// <summary>
/// Tool profile for ephemeral welcome-suggestion threads.
/// </summary>
public sealed class WelcomeSuggestionToolSource(
    MemoryStore memoryStore) : AIFunctionToolSource
{
    private readonly WelcomeSuggestionToolMethods _methods =
        new(memoryStore);

    /// <inheritdoc />
    public override string SourceId => "welcome-suggestion";

    /// <inheritdoc />
    protected override IEnumerable<AIFunction> CreateFunctions(ToolPlanningContext context)
    {
        yield return GeneratedToolFunctions.WelcomeSuggestionToolMethods_ReadWelcomeWorkspaceMemory(_methods);
        yield return GeneratedToolFunctions.WelcomeSuggestionToolMethods_EmitWelcomeSuggestions(_methods);
    }
}

public sealed class WelcomeWorkspaceMemoryResult
{
    [Description("Tail-trimmed MEMORY.md content.")]
    public string Memory { get; set; } = string.Empty;

    [Description("Tail-trimmed HISTORY.md content.")]
    public string HistoryTail { get; set; } = string.Empty;

    [Description("Combined workspace memory context.")]
    public string Combined { get; set; } = string.Empty;

    [Description("Short highlights extracted from workspace memory and recent history.")]
    public string[] MemoryHighlights { get; set; } = [];
}

public sealed class WelcomeSuggestionToolItem
{
    [Description("Short list title shown in the welcome suggestions UI.")]
    public string Title { get; set; } = string.Empty;

    [Description("Full prompt text inserted into the welcome composer when clicked.")]
    public string Prompt { get; set; } = string.Empty;

    [Description("Brief explanation of which history or memory signals inspired this suggestion.")]
    public string Reason { get; set; } = string.Empty;
}

internal sealed class WelcomeSuggestionToolMethods(MemoryStore memoryStore)
{
    private const int MemoryCharsLimit = 5_000;
    private const int HistoryTailCharsLimit = 3_000;
    private const int TotalMemoryCharsLimit = 8_000;

    private static readonly JsonSerializerOptions JsonOptions = JsonSerializerOptions.Web;

    [Tool(
        Icon = "🧠",
        DisplayType = typeof(WelcomeSuggestionToolDisplays),
        DisplayMethod = nameof(WelcomeSuggestionToolDisplays.ReadWelcomeWorkspaceMemory))]
    [Description("Read workspace MEMORY.md and the recent tail of HISTORY.md for welcome suggestion grounding. Returns a compact JSON string.")]
    public Task<string> ReadWelcomeWorkspaceMemory()
    {
        var memoryText = WelcomeSuggestionService.TrimToLimit(memoryStore.ReadLongTerm(), MemoryCharsLimit);
        var historyTail = WelcomeSuggestionService.ReadHistoryTailFromFile(memoryStore.HistoryFilePath, HistoryTailCharsLimit);
        var combined = WelcomeSuggestionService.CombineMemory(memoryText, historyTail, TotalMemoryCharsLimit);

        return Task.FromResult(Serialize(new WelcomeWorkspaceMemoryResult
        {
            Memory = memoryText,
            HistoryTail = historyTail,
            Combined = combined,
            MemoryHighlights = WelcomeSuggestionService.ExtractMemoryHighlights(memoryText, historyTail)
        }));
    }

    [Tool(
        Icon = "✨",
        DisplayType = typeof(WelcomeSuggestionToolDisplays),
        DisplayMethod = nameof(WelcomeSuggestionToolDisplays.EmitWelcomeSuggestions))]
    [Description("Submit the generated welcome suggestions as one batch.")]
    public string EmitWelcomeSuggestions(
        [Description("Exactly the requested number of welcome suggestions.")]
        WelcomeSuggestionToolItem[] items)
    {
        _ = items;
        return "Recorded.";
    }

    private static string Serialize<T>(T value) =>
        JsonSerializer.Serialize(value, JsonOptions);
}

public static class WelcomeSuggestionMethods
{
    public const string ReadWelcomeWorkspaceMemoryToolName = "ReadWelcomeWorkspaceMemory";
    public const string ToolName = "EmitWelcomeSuggestions";
}

public static class WelcomeSuggestionToolDisplays
{
    public static string ReadWelcomeWorkspaceMemory(IDictionary<string, object?>? args)
    {
        _ = args;
        return WelcomeSuggestionMethods.ReadWelcomeWorkspaceMemoryToolName;
    }

    public static string EmitWelcomeSuggestions(IDictionary<string, object?>? args)
    {
        var count = "items";
        if (args != null && args.TryGetValue("items", out var raw) && raw is System.Collections.ICollection collection)
            count = $"{collection.Count} items";
        return $"{WelcomeSuggestionMethods.ToolName} ({count})";
    }
}
