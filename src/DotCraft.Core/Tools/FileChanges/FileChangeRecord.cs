namespace DotCraft.Tools;

internal enum FileChangeKind
{
    Add,
    Update,
}

internal sealed record FileChangeRecord(
    string FullPath,
    string DisplayPath,
    FileChangeKind Kind,
    string? Before,
    string After);
