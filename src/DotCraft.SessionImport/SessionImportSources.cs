namespace DotCraft.SessionImport;

public static class SessionImportSources
{
    public const string ClaudeCode = "claude-code";
    public const string Codex = "codex";
    public const string Cursor = "cursor";

    public static IReadOnlyList<string> All { get; } = [ClaudeCode, Codex, Cursor];

    public static bool IsKnown(string? source) =>
        source is not null && All.Contains(source, StringComparer.Ordinal);
}
