namespace DotCraft.Sessions;

public sealed record SessionInputContext
{
    public required string Id { get; init; }
    public required string Kind { get; init; }
    public string? Path { get; init; }
    public string? FileName { get; init; }
    public string? Preview { get; init; }
    public int? CharacterCount { get; init; }
    public string? ThreadId { get; init; }
    public string? TurnId { get; init; }
    public string? ItemId { get; init; }
    public string? SelectedText { get; init; }
    public string? Comment { get; init; }
    public string? Side { get; init; }
    public int? StartLine { get; init; }
    public int? EndLine { get; init; }
    public string? Url { get; init; }
    public string? Title { get; init; }
    public string? SelectionKind { get; init; }
    public string? Text { get; init; }
    public SessionContextImage? Image { get; init; }
}

public sealed record SessionContextImage
{
    public required string TempPath { get; init; }
    public required string FileName { get; init; }
    public required string MimeType { get; init; }
}
