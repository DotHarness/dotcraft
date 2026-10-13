namespace DotCraft.Tools;

/// <summary>The path argument and access of each built-in file tool, shared by every approval boundary.</summary>
public static class FileToolAccess
{
    public readonly record struct Access(string PathArgument, string Operation);

    public static bool TryGet(string toolName, out Access access)
    {
        access = toolName switch
        {
            nameof(FileTools.ReadFile) or nameof(FileTools.GrepFiles) or nameof(FileTools.FindFiles) => new("path", "read"),
            nameof(FileTools.WriteFile) => new("path", "write"),
            nameof(FileTools.EditFile) => new("path", "edit"),
            nameof(LspTool.LSP) => new("filePath", "read"),
            _ => default
        };
        return access.PathArgument is not null;
    }
}
