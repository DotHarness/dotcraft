namespace DotCraft.Sessions;

internal static class CacheSharingFork
{
    public const string ParentTurnKey = "dotcraft.fork.parentTurn";
    public const string CacheRootKey = "dotcraft.fork.cacheRoot";
    public const string ToolKindKey = "dotcraft.fork.toolKind";

    private const int MemoryExtractionModelRequestLimit = 5;

    public static string InternalValue(ThreadForkPurpose purpose) => purpose switch
    {
        ThreadForkPurpose.PromptSuggestion => "prompt-suggest",
        ThreadForkPurpose.MemoryExtraction => "memory-extract",
        _ => throw new ArgumentOutOfRangeException(nameof(purpose), purpose, null)
    };

    public static ThreadForkPurpose? PurposeOf(SessionThread thread)
    {
        if (!thread.Ephemeral || !thread.Metadata.TryGetValue(ThreadVisibility.InternalMetadataKey, out var kind))
            return null;
        foreach (var purpose in Enum.GetValues<ThreadForkPurpose>())
        {
            if (string.Equals(kind, InternalValue(purpose), StringComparison.Ordinal))
                return purpose;
        }

        return null;
    }

    public static int? ModelRequestLimit(SessionThread thread) =>
        PurposeOf(thread) == ThreadForkPurpose.MemoryExtraction ? MemoryExtractionModelRequestLimit : null;
}
