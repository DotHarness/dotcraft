using System.Text.Json;
using DotCraft.Agents;
using DotCraft.Memory;
using Microsoft.Extensions.AI;

namespace DotCraft.Context;

/// <summary>
/// Runtime tool policy for memory consolidation forks.
/// Keeps the visible schema stable while allowing execution only against the
/// workspace memory files.
/// </summary>
internal sealed class MemoryConsolidationToolPolicy
{
    private static readonly HashSet<string> DirectFileTools = new(StringComparer.OrdinalIgnoreCase)
    {
        "ReadFile",
        "WriteFile",
        "EditFile"
    };

    private static readonly HashSet<string> SearchTools = new(StringComparer.OrdinalIgnoreCase)
    {
        "GrepFiles",
        "FindFiles"
    };

    private static readonly HashSet<string> MemoryFileNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "MEMORY.md",
        "HISTORY.md"
    };

    private readonly string _workspaceRoot;
    private readonly string _memoryDirectoryPath;
    private readonly string _longTermFilePath;
    private readonly string _historyFilePath;

    public MemoryConsolidationToolPolicy(MemoryStore memoryStore, string workspaceRoot)
    {
        _workspaceRoot = Path.GetFullPath(workspaceRoot);
        _memoryDirectoryPath = NormalizeFullPath(memoryStore.MemoryDirectoryPath);
        _longTermFilePath = NormalizeFullPath(memoryStore.LongTermFilePath);
        _historyFilePath = NormalizeFullPath(memoryStore.HistoryFilePath);
    }

    public ModeToolPolicyDecision Evaluate(FunctionInvocationContext context)
    {
        var toolName = context.Function.Name;
        if (DirectFileTools.Contains(toolName))
            return EvaluateDirectFileTool(toolName, context.Arguments);

        if (SearchTools.Contains(toolName))
            return EvaluateSearchTool(toolName, context.Arguments);

        return Deny(
            toolName,
            "Memory consolidation may only use file read/search/edit tools for MEMORY.md and HISTORY.md.");
    }

    private ModeToolPolicyDecision EvaluateDirectFileTool(
        string toolName,
        AIFunctionArguments arguments)
    {
        var path = TryGetStringArgument(arguments, "path");
        if (!TryResolveArgumentPath(path, out var fullPath, out var reason))
            return Deny(toolName, reason);

        if (!IsAllowedMemoryFile(fullPath))
        {
            return Deny(
                toolName,
                $"Path '{path}' is outside the allowed memory files.");
        }

        if (ExistingPathIsReparsePoint(fullPath))
        {
            return Deny(
                toolName,
                $"Path '{path}' is a reparse point and cannot be used during memory consolidation.");
        }

        return ModeToolPolicyDecision.Allow;
    }

    private ModeToolPolicyDecision EvaluateSearchTool(
        string toolName,
        AIFunctionArguments arguments)
    {
        var path = TryGetStringArgument(arguments, "path");
        if (!TryResolveArgumentPath(path, out var fullPath, out var reason, allowEmpty: false))
            return Deny(toolName, reason);

        if (!IsMemoryDirectory(fullPath))
        {
            return Deny(
                toolName,
                $"Search path '{path}' must be the memory directory.");
        }

        if (ExistingPathIsReparsePoint(fullPath))
        {
            return Deny(
                toolName,
                $"Search path '{path}' is a reparse point and cannot be used during memory consolidation.");
        }

        var patternArgument = string.Equals(toolName, "FindFiles", StringComparison.OrdinalIgnoreCase)
            ? "pattern"
            : "include";
        var pattern = TryGetStringArgument(arguments, patternArgument);
        if (!PatternOnlyTargetsMemoryFiles(pattern))
        {
            return Deny(
                toolName,
                $"{patternArgument} must name only MEMORY.md and/or HISTORY.md.");
        }

        return ModeToolPolicyDecision.Allow;
    }

    private bool TryResolveArgumentPath(
        string? path,
        out string fullPath,
        out string reason,
        bool allowEmpty = false)
    {
        fullPath = string.Empty;
        reason = string.Empty;
        if (string.IsNullOrWhiteSpace(path))
        {
            if (!allowEmpty)
            {
                reason = "A path is required for memory consolidation file access.";
                return false;
            }

            path = ".";
        }

        try
        {
            var expanded = ExpandPath(path);
            fullPath = NormalizeFullPath(Path.IsPathRooted(expanded)
                ? expanded
                : Path.Combine(_workspaceRoot, expanded));
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            reason = $"Path '{path}' could not be resolved: {ex.Message}";
            return false;
        }
    }

    private static string ExpandPath(string path)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrEmpty(home))
        {
            if (path == "~" || path.StartsWith("~/", StringComparison.Ordinal) || path.StartsWith("~\\", StringComparison.Ordinal))
                path = Path.Combine(home, path[1..].TrimStart('/', '\\'));

            path = path.Replace("${HOME}", home, StringComparison.Ordinal)
                .Replace("$HOME", home, StringComparison.Ordinal);
        }

        return Environment.ExpandEnvironmentVariables(path);
    }

    private bool IsAllowedMemoryFile(string fullPath) =>
        string.Equals(fullPath, _longTermFilePath, StringComparison.OrdinalIgnoreCase)
        || string.Equals(fullPath, _historyFilePath, StringComparison.OrdinalIgnoreCase);

    private bool IsMemoryDirectory(string fullPath) =>
        string.Equals(fullPath, _memoryDirectoryPath, StringComparison.OrdinalIgnoreCase);

    private static bool PatternOnlyTargetsMemoryFiles(string? pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern))
            return false;

        var parts = pattern.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length > 0 && parts.All(MemoryFileNames.Contains);
    }

    private static bool ExistingPathIsReparsePoint(string fullPath)
    {
        if (!File.Exists(fullPath) && !Directory.Exists(fullPath))
            return false;

        var attributes = File.GetAttributes(fullPath);
        return (attributes & FileAttributes.ReparsePoint) != 0;
    }

    private static string NormalizeFullPath(string path) =>
        Path.GetFullPath(path)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private static string? TryGetStringArgument(AIFunctionArguments arguments, string name)
    {
        if (!arguments.TryGetValue(name, out var value))
            return null;

        return value switch
        {
            null => null,
            string s => s,
            JsonElement { ValueKind: JsonValueKind.String } element => element.GetString(),
            JsonElement { ValueKind: JsonValueKind.Null or JsonValueKind.Undefined } => null,
            JsonElement element => element.ToString(),
            _ => value.ToString()
        };
    }

    private string FormatAllowedFiles() =>
        $"{_longTermFilePath}; {_historyFilePath}";

    private ModeToolPolicyDecision Deny(string toolName, string reason) =>
        ModeToolPolicyDecision.DenyRecoverable(
            $"""
MEMORY_CONSOLIDATION_TOOL_POLICY_DENIED
Tool: {toolName}
AllowedActionProfile: MemoryFilesOnly
AllowedFiles: {FormatAllowedFiles()}
Reason: {reason}
NextAllowedActions: Read, search, write, or edit only MEMORY.md and HISTORY.md.
""");
}
