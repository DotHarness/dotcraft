using System.ComponentModel;
using System.Text;
using System.Text.RegularExpressions;
using DotCraft.Tools;
using OpenSandbox.Models;

namespace DotCraft.Tools.Sandbox;

/// <summary>
/// File operations inside an OpenSandbox container.
/// All reads and writes happen within the sandbox's isolated filesystem.
/// No path validation or approval is needed — the container is the boundary.
/// </summary>
public sealed class SandboxFileTools
{
    private readonly SandboxSessionManager _sandboxManager;
    private readonly int _maxFileSize;

    private static readonly Regex UnicodeEscapeRegex = new(@"\\u([0-9a-fA-F]{4})", RegexOptions.Compiled);

    public SandboxFileTools(
        SandboxSessionManager sandboxManager,
        int maxFileSize = 10 * 1024 * 1024)
    {
        _sandboxManager = sandboxManager;
        _maxFileSize = maxFileSize;
    }

    [Description("Read the contents of a file or list the contents of a directory. If the path is a directory, lists its entries. Supports 1-indexed offset and limit for paginated reading of text files; limit without offset starts at line 1. Text output is line-numbered and indicates whether more lines remain. Large text files require offset/limit or GrepFiles. PDF and other binary files are rejected instead of read as text.")]
    [Tool(Icon = "📄", DisplayType = typeof(CoreToolDisplays), DisplayMethod = nameof(CoreToolDisplays.ReadFile), MaxResultChars = 0)]
    public async Task<string> ReadFile(
        [Description("Path inside the sandbox (absolute or relative to /workspace).")] string path,
        [Description("Line number to start reading from (1-indexed). Omit or pass 0 to start at line 1 when limit is provided.")] int offset = 0,
        [Description("Maximum number of lines to read. When omitted with offset, defaults to 2000. When provided without offset, reads from line 1.")] int limit = 0)
    {
        try
        {
            var sandbox = await _sandboxManager.GetOrCreateAsync();
            var fullPath = ResolveSandboxPath(path);

            // Check if it's a directory
            var checkResult = await sandbox.Commands.RunAsync($"test -d {EscapeShellArg(fullPath)} && echo DIR || echo FILE");
            var isDir = checkResult.Logs.Stdout.FirstOrDefault()?.Text?.Trim() == "DIR";

            if (isDir)
            {
                var lsResult = await sandbox.Commands.RunAsync($"ls -la {EscapeShellArg(fullPath)}");
                return FormatCommandOutput(lsResult);
            }

            var byteLength = await TryGetSandboxFileSizeAsync(sandbox, fullPath);
            if (FileContentClassifier.IsPdf(fullPath))
                return FileContentClassifier.FormatPdfUnsupportedMessage(path, byteLength);

            if (FileContentClassifier.IsKnownBinaryExtension(fullPath))
                return FileContentClassifier.FormatBinaryUnsupportedMessage(path, fullPath, byteLength);

            if (byteLength.HasValue && byteLength.Value > _maxFileSize)
                return $"Error: File too large ({byteLength.Value} bytes). Max size: {_maxFileSize} bytes.";

            var sample = await ReadSandboxFileSampleAsync(sandbox, fullPath);
            if (FileContentClassifier.LooksBinary(sample))
                return FileContentClassifier.FormatBinaryUnsupportedMessage(path, fullPath, byteLength, detectedFromSample: true);

            if (!TextFileReadLimiter.IsPagedRead(offset, limit)
                && byteLength.HasValue
                && byteLength.Value > TextFileReadLimiter.MaxUnpaginatedTextBytes)
                return TextFileReadLimiter.FormatUnpaginatedTooLarge(path, byteLength.Value);

            // Read file content
            var content = await sandbox.Files.ReadFileAsync(fullPath);

            if (content.Length > _maxFileSize)
                return $"Error: File too large ({content.Length} bytes). Max size: {_maxFileSize} bytes.";

            if (!TextFileReadLimiter.IsPagedRead(offset, limit)
                && content.Length > TextFileReadLimiter.MaxUnpaginatedTextBytes)
                return TextFileReadLimiter.FormatUnpaginatedTooLarge(path, content.Length);

            return FormatTextReadResult(content, offset, limit);
        }
        catch (OpenSandbox.Core.SandboxException ex)
        {
            return $"Sandbox error: [{ex.Error.Code}] {ex.Error.Message}";
        }
        catch (Exception ex)
        {
            return $"Error reading file in sandbox: {ex.Message}";
        }
    }

    [Description("Write content to a file at the given path. Creates parent directories if needed. Prefer this tool for creating new files or intentional full-file rewrites. When modifying an existing file, prefer EditFile for targeted changes.")]
    [Tool(Icon = "✏️", DisplayType = typeof(CoreToolDisplays), DisplayMethod = nameof(CoreToolDisplays.WriteFile))]
    public async Task<string> WriteFile(
        [Description("Path inside the sandbox (absolute or relative to /workspace).")] string path,
        [Description("The content to write.")] string content)
    {
        try
        {
            var sandbox = await _sandboxManager.GetOrCreateAsync();
            var fullPath = ResolveSandboxPath(path);

            using (await PathAsyncMutex.AcquireKeyAsync($"sandbox:{fullPath}"))
            {
                // Ensure parent directory exists
                var parentDir = fullPath[..fullPath.LastIndexOf('/')];
                if (!string.IsNullOrEmpty(parentDir))
                {
                    await sandbox.Files.CreateDirectoriesAsync([
                        new CreateDirectoryEntry { Path = parentDir, Mode = 755 }
                    ]);
                }

                await sandbox.Files.WriteFilesAsync([
                    new WriteEntry { Path = fullPath, Data = content, Mode = 644 }
                ]);
            }

            var lineCount = content.Split('\n').Length;
            return $"Successfully wrote {content.Length} bytes ({lineCount} lines) to {path}";
        }
        catch (OpenSandbox.Core.SandboxException ex)
        {
            return $"Sandbox error: [{ex.Error.Code}] {ex.Error.Message}";
        }
        catch (Exception ex)
        {
            return $"Error writing file in sandbox: {ex.Message}";
        }
    }

    [Description("Replace text in a file: oldText (snippet to find) and newText. Prefer a minimal unique snippet (typically 2-6 lines including nearby context) instead of large pasted blocks. For existing files, prefer targeted EditFile replacements over full-file rewrites, even when many changes are needed. Use WriteFile for new files or intentional full rewrites. When replaceAll is false, same fuzzy matching as workspace EditFile (exact, line trim, indentation, whitespace, Unicode). Use replaceAll only when you intentionally want to replace every exact occurrence at once.")]
    [Tool(Icon = "🔄", DisplayType = typeof(CoreToolDisplays), DisplayMethod = nameof(CoreToolDisplays.EditFile))]
    public async Task<string> EditFile(
        [Description("Path inside the sandbox (absolute or relative to /workspace).")] string path,
        [Description("The snippet from the file to replace.")] string oldText = "",
        [Description("The replacement text.")] string newText = "",
        [Description("If true, replace all exact occurrences of oldText (no fuzzy matching). Defaults to false.")] bool replaceAll = false)
    {
        if (string.IsNullOrEmpty(oldText))
            return "Error: oldText is required.";

        try
        {
            var sandbox = await _sandboxManager.GetOrCreateAsync();
            var fullPath = ResolveSandboxPath(path);

            newText = UnescapeUnicodeSequences(newText);
            oldText = UnescapeUnicodeSequences(oldText);

            string result;
            using (await PathAsyncMutex.AcquireKeyAsync($"sandbox:{fullPath}"))
            {
                var content = await sandbox.Files.ReadFileAsync(fullPath);
                var useCrLf = content.Contains("\r\n", StringComparison.Ordinal);
                var lfContent = content.Replace("\r\n", "\n", StringComparison.Ordinal);
                var lfOld = oldText.Replace("\r\n", "\n", StringComparison.Ordinal);
                var lfNew = newText.Replace("\r\n", "\n", StringComparison.Ordinal);

                var (ok, newLfContent, error, matchKind, lineNum, oldLineCount, replaceCount) =
                    FileEditSearchReplace.Apply(lfContent, lfOld, lfNew, replaceAll);
                if (!ok)
                    return error!;

                var newContent = useCrLf ? newLfContent.Replace("\n", "\r\n", StringComparison.Ordinal) : newLfContent;

                await sandbox.Files.WriteFilesAsync([
                    new WriteEntry { Path = fullPath, Data = newContent, Mode = 644 }
                ]);

                if (replaceCount > 1)
                    result = $"Successfully replaced {replaceCount} occurrences in {path}";
                else
                {
                    var newLineCount = string.IsNullOrEmpty(lfNew) ? 0 : lfNew.Count(c => c == '\n') + 1;
                    var suffix = matchKind != null ? $" ({matchKind})" : "";
                    result = $"Successfully edited {path} at line {lineNum} ({oldLineCount} -> {newLineCount} lines){suffix}";
                }
            }

            return result;
        }
        catch (OpenSandbox.Core.SandboxException ex)
        {
            return $"Sandbox error: [{ex.Error.Code}] {ex.Error.Message}";
        }
        catch (Exception ex)
        {
            return $"Error editing file in sandbox: {ex.Message}";
        }
    }

    [Description("Search file contents using a regular expression pattern. Returns matching lines with file paths and line numbers. Skips binary files and .git/node_modules directories.")]
    [Tool(Icon = "🔍", DisplayType = typeof(CoreToolDisplays), DisplayMethod = nameof(CoreToolDisplays.GrepFiles), MaxResultChars = 20_000)]
    public async Task<string> GrepFiles(
        [Description("The regular expression pattern to search for.")] string pattern,
        [Description("Directory to search in (relative to /workspace).")] string path = "",
        [Description("File name pattern to include (e.g. \"*.cs\").")] string include = "")
    {
        try
        {
            var sandbox = await _sandboxManager.GetOrCreateAsync();
            var searchPath = string.IsNullOrEmpty(path) ? "/workspace" : ResolveSandboxPath(path);

            // Use grep inside the sandbox
            var includeArg = string.IsNullOrEmpty(include) ? "" : $"--include={EscapeShellArg(include)}";
            var command = $"grep -rn {includeArg} --max-count=100 -P {EscapeShellArg(pattern)} {EscapeShellArg(searchPath)} 2>/dev/null | head -100";

            var result = await sandbox.Commands.RunAsync(command);
            var output = FormatCommandOutput(result);

            return string.IsNullOrWhiteSpace(output) || output == "(no output)"
                ? "No matches found."
                : output;
        }
        catch (OpenSandbox.Core.SandboxException ex)
        {
            return $"Sandbox error: [{ex.Error.Code}] {ex.Error.Message}";
        }
        catch (Exception ex)
        {
            return $"Error searching files in sandbox: {ex.Message}";
        }
    }

    [Description("Find files by name pattern. Searches recursively, skipping .git and node_modules directories. Use semicolons to separate multiple patterns (e.g. \"*.cs;*.json\").")]
    [Tool(Icon = "📂", DisplayType = typeof(CoreToolDisplays), DisplayMethod = nameof(CoreToolDisplays.FindFiles))]
    public async Task<string> FindFiles(
        [Description("File name pattern (e.g. \"*.cs\", \"*.json\"). Use semicolons for multiple patterns.")] string pattern,
        [Description("Directory to search in (relative to /workspace).")] string path = "")
    {
        try
        {
            var sandbox = await _sandboxManager.GetOrCreateAsync();
            var searchPath = string.IsNullOrEmpty(path) ? "/workspace" : ResolveSandboxPath(path);

            // Build find command with multiple patterns
            var patterns = pattern.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var nameArgs = string.Join(" -o ", patterns.Select(p => $"-name {EscapeShellArg(p)}"));
            if (patterns.Length > 1) nameArgs = $"\\( {nameArgs} \\)";

            var command = $"find {EscapeShellArg(searchPath)} {nameArgs} -not -path '*/.git/*' -not -path '*/node_modules/*' 2>/dev/null | head -200";

            var result = await sandbox.Commands.RunAsync(command);
            var output = FormatCommandOutput(result);

            return string.IsNullOrWhiteSpace(output) || output == "(no output)"
                ? "No files found."
                : output;
        }
        catch (OpenSandbox.Core.SandboxException ex)
        {
            return $"Sandbox error: [{ex.Error.Code}] {ex.Error.Message}";
        }
        catch (Exception ex)
        {
            return $"Error finding files in sandbox: {ex.Message}";
        }
    }

    private static string ResolveSandboxPath(string path)
    {
        if (path.StartsWith('/'))
            return path;
        if (path.StartsWith("..") || path.Contains("/../") || path.EndsWith("/.."))
            throw new ArgumentException("Path traversal not allowed");
        if (path.StartsWith("./"))
            return "/workspace/" + path[2..];
        return "/workspace/" + path;
    }

    private static string FormatCommandOutput(OpenSandbox.Models.Execution execution)
    {
        var sb = new StringBuilder();
        foreach (var line in execution.Logs.Stdout)
        {
            if (line.Text != null) sb.AppendLine(line.Text);
        }
        var output = sb.ToString().TrimEnd();
        return string.IsNullOrWhiteSpace(output) ? "(no output)" : output;
    }

    internal static string FormatTextReadResult(string content, int offset, int limit)
        => TextFileReadLimiter.FormatInMemory(content, offset, limit);

    private static async Task<long?> TryGetSandboxFileSizeAsync(OpenSandbox.Sandbox sandbox, string fullPath)
    {
        var arg = EscapeShellArg(fullPath);
        var result = await sandbox.Commands.RunAsync($"stat -c%s {arg} 2>/dev/null || wc -c < {arg} 2>/dev/null");
        var output = FormatCommandOutput(result);
        if (output == "(no output)")
            return null;

        var firstLine = output.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim();
        return long.TryParse(firstLine, out var size) ? size : null;
    }

    private static async Task<byte[]> ReadSandboxFileSampleAsync(OpenSandbox.Sandbox sandbox, string fullPath)
    {
        var result = await sandbox.Commands.RunAsync(
            $"head -c {FileContentClassifier.SampleSize} {EscapeShellArg(fullPath)} 2>/dev/null | base64 -w0");
        var output = FormatCommandOutput(result).Trim();
        if (string.IsNullOrWhiteSpace(output) || output == "(no output)")
            return [];

        try
        {
            return Convert.FromBase64String(output);
        }
        catch (FormatException)
        {
            return [];
        }
    }

    private static string UnescapeUnicodeSequences(string input)
    {
        if (!input.Contains("\\u"))
            return input;

        return UnicodeEscapeRegex.Replace(input, match =>
            ((char)Convert.ToInt32(match.Groups[1].Value, 16)).ToString());
    }

    private static string EscapeShellArg(string arg)
    {
        return "'" + arg.Replace("'", "'\\''") + "'";
    }
}
