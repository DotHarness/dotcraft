using System.Text.Json.Nodes;
using DotCraft.Security;

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

    /// <summary>Resolves the full path a built-in file tool call targets; an omitted path means the workspace root.</summary>
    public static bool TryResolveTarget(
        string toolName,
        JsonObject? arguments,
        string? workspacePath,
        out Access access,
        out string fullPath)
    {
        fullPath = string.Empty;
        if (!TryGet(toolName, out access) || string.IsNullOrWhiteSpace(workspacePath))
            return false;

        var path = arguments?[access.PathArgument] is JsonValue value && value.TryGetValue<string>(out var text)
            ? text
            : null;
        try
        {
            fullPath = FileAccessGuard.ResolvePath(workspacePath, string.IsNullOrWhiteSpace(path) ? "." : path);
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
