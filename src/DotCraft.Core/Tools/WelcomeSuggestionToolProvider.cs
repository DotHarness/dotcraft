using System.ComponentModel;
using DotCraft.GeneratedTools.Core;
using Microsoft.Extensions.AI;

namespace DotCraft.Tools;

/// <summary>
/// Tool profile for ephemeral welcome-suggestion threads.
/// </summary>
public sealed class WelcomeSuggestionToolSource : AIFunctionToolSource
{
    private readonly WelcomeSuggestionToolMethods _methods = new();

    /// <inheritdoc />
    public override string SourceId => "welcome-suggestion";

    /// <inheritdoc />
    protected override IEnumerable<AIFunction> CreateFunctions(ToolPlanningContext context)
    {
        yield return GeneratedToolFunctions.WelcomeSuggestionToolMethods_EmitWelcomeSuggestions(_methods);
    }
}

public sealed class WelcomeSuggestionToolItem
{
    [Description("Short list title shown in the welcome suggestions UI.")]
    public string Title { get; set; } = string.Empty;

    [Description("Full prompt text inserted into the welcome composer when clicked.")]
    public string Prompt { get; set; } = string.Empty;

    [Description("Brief explanation of which memory signals inspired this suggestion.")]
    public string Reason { get; set; } = string.Empty;
}

internal sealed class WelcomeSuggestionToolMethods
{
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
}

public static class WelcomeSuggestionMethods
{
    public const string ToolName = "EmitWelcomeSuggestions";
}

public static class WelcomeSuggestionToolDisplays
{
    public static string EmitWelcomeSuggestions(IDictionary<string, object?>? args)
    {
        var count = "items";
        if (args != null && args.TryGetValue("items", out var raw) && raw is System.Collections.ICollection collection)
            count = $"{collection.Count} items";
        return $"{WelcomeSuggestionMethods.ToolName} ({count})";
    }
}
