namespace DotCraft.Sessions;

public readonly record struct ThreadMessageSearchCursor(long ItemOrdinal, int TextOffset);

public sealed record ThreadMessageOccurrence(
    string TurnId,
    string ItemId,
    string Snippet,
    int SnippetMatchStart,
    int SnippetMatchEnd,
    long TurnOrdinal);

public sealed record ThreadMessageSearchPage(
    IReadOnlyList<ThreadMessageOccurrence> Data,
    ThreadMessageSearchCursor? NextCursor);
