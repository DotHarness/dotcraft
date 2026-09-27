namespace DotCraft.Sessions;

internal static class PromptSuggestionThread
{
    public static bool IsSuggestion(SessionThread thread) => thread.Ephemeral
        && thread.Metadata.TryGetValue(ThreadVisibility.InternalMetadataKey, out var kind)
        && kind == InternalValue;

    public const string ParentTurnKey = "dotcraft.promptSuggest.parentTurn";
    public const string InternalValue = "prompt-suggest";
    public const string CacheRootKey = "dotcraft.promptSuggest.cacheRoot";
    public const string ToolKindKey = "dotcraft.promptSuggest.toolKind";
}
