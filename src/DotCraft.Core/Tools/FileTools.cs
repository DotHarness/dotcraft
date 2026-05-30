using System.ComponentModel;
using System.Text;
using System.Text.RegularExpressions;
using DotCraft.Lsp;
using DotCraft.Security;
using Microsoft.Extensions.AI;

namespace DotCraft.Tools;

/// <summary>
/// File system tools: read, write, edit, search files with safety guards.
/// </summary>
/// <remarks>
/// Write and edit operations serialize per canonical file path across the process so concurrent
/// tool calls cannot overlap their read-modify-write sections.
/// </remarks>
public sealed class FileTools(
    string workspaceRoot,
    bool requireApprovalOutsideWorkspace = true,
    int maxFileSize = 10 * 1024 * 1024,
    IApprovalService? approvalService = null,
    PathBlacklist? blacklist = null,
    IReadOnlyList<string>? trustedReadPaths = null,
    LspServerManager? lspServerManager = null,
    string? ripgrepPath = null,
    TimeSpan? searchTimeout = null)
{
    private const int MaxGrepMatches = 100;
    
    private const int MaxFindResults = 200;
    
    private const int MaxGrepFileSize = 5 * 1024 * 1024;

    private const int MaxLineLength = TextFileReadLimiter.MaxLineLength;

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private static readonly HashSet<string> SkipDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", "node_modules"
    };

    /// <summary>
    /// Image extensions returned as <see cref="DataContent"/> for vision models.
    /// </summary>
    private static readonly Dictionary<string, string> ImageExtensionToMediaType = new(StringComparer.OrdinalIgnoreCase)
    {
        [".png"] = "image/png",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".gif"] = "image/gif",
        [".webp"] = "image/webp",
        [".bmp"] = "image/bmp",
    };

    private readonly string _workspaceRoot = Path.GetFullPath(workspaceRoot);
    private readonly FileAccessGuard _fileAccessGuard = new(
        workspaceRoot,
        requireApprovalOutsideWorkspace,
        approvalService,
        blacklist,
        trustedReadPaths);
    private readonly RipgrepFileSearcher _ripgrep = new(ripgrepPath);
    private readonly TimeSpan _searchTimeout = NormalizeSearchTimeout(searchTimeout);

    [Description("Read the contents of a file or list the contents of a directory. If the path is a directory, lists its entries. Supports 1-indexed offset and limit for paginated reading of text files; limit without offset starts at line 1. Text output is line-numbered and indicates whether more lines remain. Large text files require offset/limit or GrepFiles. Image files (.png, .jpg, .jpeg, .gif, .webp, .bmp) are returned as vision input for the model (full file only; offset/limit do not apply). PDF and other binary files are rejected instead of read as text.")]
    [Tool(Icon = "📄", DisplayType = typeof(CoreToolDisplays), DisplayMethod = nameof(CoreToolDisplays.ReadFile), MaxResultChars = 0)]
    public async Task<IList<AIContent>> ReadFile(
        [Description("The workspace-relative or absolute path to read.")] string path,
        [Description("The line number to start reading from (1-indexed). Omit or pass 0 to start at line 1 when limit is provided.")] int offset = 0,
        [Description("The maximum number of lines to read. When omitted with offset, defaults to 2000. When provided without offset, reads from line 1.")] int limit = 0,
        CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fullPath = ResolvePath(path);
            var validateResult = await ValidatePathAsync(fullPath, "read", path);
            if (validateResult != null)
                return ReadFileTextResult(validateResult);

            if (Directory.Exists(fullPath))
                return ReadFileTextResult(FormatDirectoryListing(fullPath, path));

            if (!File.Exists(fullPath))
                return ReadFileTextResult($"Error: File not found: {path}");

            var fileInfo = new FileInfo(fullPath);
            if (fileInfo.Length > maxFileSize)
                return ReadFileTextResult($"Error: File too large ({fileInfo.Length} bytes). Max size: {maxFileSize} bytes.");

            if (TryGetImageMediaType(fullPath, out var mediaType))
            {
                if (TextFileReadLimiter.IsPagedRead(offset, limit))
                {
                    return ReadFileTextResult(
                        "Error: Line offset/limit pagination is not supported for image files; call ReadFile without offset and limit to load the image as vision input.");
                }

                var bytes = await WithSharingViolationRetryAsync(
                    () => File.ReadAllBytesAsync(fullPath, cancellationToken),
                    cancellationToken);
                var summary = $"Image: {path} ({bytes.Length:N0} bytes, {mediaType})";
                return [new TextContent(summary), new DataContent(bytes, mediaType)];
            }

            if (FileContentClassifier.IsPdf(fullPath))
                return ReadFileTextResult(FileContentClassifier.FormatPdfUnsupportedMessage(path, fileInfo.Length));

            if (FileContentClassifier.IsKnownBinaryExtension(fullPath))
                return ReadFileTextResult(FileContentClassifier.FormatBinaryUnsupportedMessage(path, fullPath, fileInfo.Length));

            if (await FileContentClassifier.LooksBinaryFileAsync(fullPath))
                return ReadFileTextResult(FileContentClassifier.FormatBinaryUnsupportedMessage(path, fullPath, fileInfo.Length, detectedFromSample: true));

            var encoding = DetectFileEncoding(fullPath);

            if (TextFileReadLimiter.IsPagedRead(offset, limit))
                return ReadFileTextResult(await WithSharingViolationRetryAsync(
                    () => TextFileReadLimiter.ReadPageAsync(fullPath, encoding, offset, limit, cancellationToken),
                    cancellationToken));

            if (fileInfo.Length > TextFileReadLimiter.MaxUnpaginatedTextBytes)
                return ReadFileTextResult(TextFileReadLimiter.FormatUnpaginatedTooLarge(path, fileInfo.Length));

            var content = await WithSharingViolationRetryAsync(
                () => File.ReadAllTextAsync(fullPath, encoding, cancellationToken),
                cancellationToken);
            return ReadFileTextResult(TextFileReadLimiter.FormatInMemory(content, offset, limit));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (UnauthorizedAccessException)
        {
            return ReadFileTextResult($"Error: Permission denied: {path}");
        }
        catch (Exception ex)
        {
            return ReadFileTextResult($"Error reading file: {ex.Message}");
        }
    }

    private static IList<AIContent> ReadFileTextResult(string text) => [new TextContent(text)];

    private static bool TryGetImageMediaType(string fullPath, out string mediaType)
    {
        var ext = Path.GetExtension(fullPath);
        return ImageExtensionToMediaType.TryGetValue(ext, out mediaType!);
    }

    private static TimeSpan NormalizeSearchTimeout(TimeSpan? timeout)
    {
        var value = timeout.GetValueOrDefault(TimeSpan.FromSeconds(30));
        return value > TimeSpan.Zero ? value : TimeSpan.FromSeconds(30);
    }

    [Description("Write content to a file at the given path. Creates parent directories if needed. Prefer this tool for creating new files or intentional full-file rewrites. When modifying an existing file, prefer EditFile for targeted changes.")]
    [Tool(Icon = "✏️", DisplayType = typeof(CoreToolDisplays), DisplayMethod = nameof(CoreToolDisplays.WriteFile))]
    public async Task<string> WriteFile(
        [Description("The workspace-relative or absolute file path to write to.")] string path,
        [Description("The content to write.")] string content)
    {
        try
        {
            var fullPath = ResolvePath(path);
            var validateResult = await ValidatePathAsync(fullPath, "write", path);
            if (validateResult != null)
                return validateResult;

            using (await PathAsyncMutex.AcquireAsync(fullPath))
            {
                var directory = Path.GetDirectoryName(fullPath);
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);

                var encoding = File.Exists(fullPath) ? DetectFileEncoding(fullPath) : Utf8NoBom;
                if (File.Exists(fullPath))
                {
                    var existing = await File.ReadAllTextAsync(fullPath, encoding);
                    content = RestoreLineEndings(NormalizeToLf(content), UsesCrLf(existing));
                }
                else
                {
                    content = NormalizeToLf(content);
                }

                await WriteAllTextEnsuringDirectoryAsync(fullPath, content, encoding);
            }

            await NotifyLspFileChangedAsync(fullPath, content);
            var lineCount = content.Split('\n').Length;
            return $"Successfully wrote {content.Length} bytes ({lineCount} lines) to {path}";
        }
        catch (UnauthorizedAccessException)
        {
            return $"Error: Permission denied: {path}";
        }
        catch (Exception ex)
        {
            return $"Error writing file: {ex.Message}";
        }
    }

    [Description("Replace text in a file: provide oldText (snippet to find) and newText. Prefer a minimal unique snippet (typically 2-6 lines including nearby context) instead of large pasted blocks. For existing files, prefer targeted EditFile replacements over full-file rewrites, even when many changes are needed. Use WriteFile for new files or intentional full rewrites. When replaceAll is false (default), matching tries exact text first, then fuzzy fallbacks (line trim, indentation, collapsed whitespace, Unicode punctuation); oldText must match exactly one location unless you set replaceAll to true. Use replaceAll only when you intentionally want to replace every exact occurrence at once.")]
    [Tool(Icon = "🔄", DisplayType = typeof(CoreToolDisplays), DisplayMethod = nameof(CoreToolDisplays.EditFile))]
    public async Task<string> EditFile(
        [Description("The workspace-relative or absolute file path to edit.")] string path,
        [Description("The exact snippet from the file to replace. Include enough surrounding lines to be unique when replaceAll is false.")] string oldText = "",
        [Description("The replacement text.")] string newText = "",
        [Description("If true, replace all exact occurrences of oldText (no fuzzy matching). Defaults to false.")] bool replaceAll = false)
    {
        try
        {
            var fullPath = ResolvePath(path);
            var validateResult = await ValidatePathAsync(fullPath, "edit", path);
            if (validateResult != null)
                return validateResult;

            newText = UnescapeUnicodeSequences(newText);

            if (string.IsNullOrEmpty(oldText))
                return "Error: oldText is required. Provide the exact snippet to find and replace.";

            oldText = UnescapeUnicodeSequences(oldText);

            string result;
            string? writtenContent;
            using (await PathAsyncMutex.AcquireAsync(fullPath))
            {
                if (!File.Exists(fullPath))
                    return $"Error: File not found: {path}";

                var encoding = DetectFileEncoding(fullPath);
                var content = await File.ReadAllTextAsync(fullPath, encoding);
                (result, writtenContent) = await ApplySearchReplaceEdit(fullPath, path, content, oldText, newText, encoding, replaceAll);
            }

            if (writtenContent != null)
                await NotifyLspFileChangedAsync(fullPath, writtenContent);

            return result;
        }
        catch (UnauthorizedAccessException)
        {
            return $"Error: Permission denied: {path}";
        }
        catch (Exception ex)
        {
            return $"Error editing file: {ex.Message}";
        }
    }

    [Description("Search file contents using a regular expression pattern. Returns matching lines with file paths and line numbers. Skips binary files and .git/node_modules directories. For open-ended searches requiring multiple rounds or broad codebase exploration, use SpawnAgent instead.")]
    [Tool(Icon = "🔍", DisplayType = typeof(CoreToolDisplays), DisplayMethod = nameof(CoreToolDisplays.GrepFiles), MaxResultChars = 20_000)]
    public async Task<string> GrepFiles(
        [Description("The regular expression pattern to search for.")] string pattern,
        [Description("The directory to search in. Defaults to workspace root.")] string path = "",
        [Description("File name pattern to include (e.g. \"*.cs\", \"*.json\"). Searches all text files if not specified.")] string include = "",
        CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var searchPath = string.IsNullOrEmpty(path) ? _workspaceRoot : ResolvePath(path);
            var validateResult = await ValidatePathAsync(searchPath, "read", string.IsNullOrEmpty(path) ? "." : path);
            if (validateResult != null)
                return validateResult;

            if (!Directory.Exists(searchPath))
                return $"Error: Directory not found: {path}";

            var ripgrepResult = await _ripgrep.SearchAsync(new RipgrepSearchRequest(
                searchPath,
                pattern,
                string.IsNullOrEmpty(include) ? null : include,
                MaxGrepMatches,
                MaxLineLength,
                MaxGrepFileSize,
                _searchTimeout),
                cancellationToken);
            if (ripgrepResult != null)
                return ripgrepResult;

            Regex regex;
            try
            {
                regex = new Regex(pattern, RegexOptions.Compiled, TimeSpan.FromSeconds(5));
            }
            catch (ArgumentException ex)
            {
                return $"Error: Invalid regex pattern: {ex.Message}";
            }

            var includePattern = string.IsNullOrEmpty(include) ? null : include;
            var matches = new List<(string FilePath, int LineNum, string LineText)>();
            var totalMatches = 0;

            using var fallbackTimeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            fallbackTimeoutCts.CancelAfter(_searchTimeout);
            var fallbackCancellationToken = fallbackTimeoutCts.Token;

            try
            {
                foreach (var filePath in EnumerateSearchableFiles(searchPath, includePattern, fallbackCancellationToken))
                {
                    fallbackCancellationToken.ThrowIfCancellationRequested();
                    if (totalMatches >= MaxGrepMatches)
                        break;

                    try
                    {
                        var fileInfo = new FileInfo(filePath);
                        if (fileInfo.Length > MaxGrepFileSize || fileInfo.Length == 0)
                            continue;

                        if (IsBinaryFile(filePath))
                            continue;

                        var lines = await File.ReadAllLinesAsync(filePath, DetectFileEncoding(filePath), fallbackCancellationToken);
                        for (var i = 0; i < lines.Length; i++)
                        {
                            fallbackCancellationToken.ThrowIfCancellationRequested();
                            if (regex.IsMatch(lines[i]))
                            {
                                totalMatches++;
                                matches.Add((filePath, i + 1, lines[i]));
                                if (totalMatches >= MaxGrepMatches)
                                    break;
                            }
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch
                    {
                        // ignored
                    }
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && fallbackTimeoutCts.IsCancellationRequested)
            {
                return RipgrepFileSearcher.FormatTimeout(_searchTimeout);
            }

            if (matches.Count == 0)
                return "No matches found.";

            var sb = new StringBuilder();
            sb.AppendLine($"Found {matches.Count} matches{(totalMatches >= MaxGrepMatches ? $" (showing first {MaxGrepMatches}, there may be more)" : "")}:");

            var currentFile = "";
            foreach (var match in matches)
            {
                var relativePath = Path.GetRelativePath(searchPath, match.FilePath);
                if (currentFile != relativePath)
                {
                    if (currentFile != "")
                        sb.AppendLine();
                    currentFile = relativePath;
                    sb.AppendLine($"{relativePath}:");
                }
                var lineText = match.LineText.Length > MaxLineLength
                    ? match.LineText[..MaxLineLength] + "..."
                    : match.LineText;
                sb.AppendLine($"  Line {match.LineNum}: {lineText}");
            }

            return sb.ToString();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return $"Error searching files: {ex.Message}";
        }
    }

    [Description("Find files by name pattern. Searches recursively, skipping .git and node_modules directories. Use semicolons to separate multiple patterns (e.g. \"*.cs;*.json\"). When you need to explore an unfamiliar codebase structure with multiple rounds of discovery, consider using SpawnAgent instead.")]
    [Tool(Icon = "📂", DisplayType = typeof(CoreToolDisplays), DisplayMethod = nameof(CoreToolDisplays.FindFiles))]
    public async Task<string> FindFiles(
        [Description("The file name pattern to match (e.g. \"*.cs\", \"*.json\"). Use semicolons for multiple patterns.")] string pattern,
        [Description("The directory to search in. Defaults to workspace root.")] string path = "",
        CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var searchPath = string.IsNullOrEmpty(path) ? _workspaceRoot : ResolvePath(path);
            var validateResult = await ValidatePathAsync(searchPath, "read", string.IsNullOrEmpty(path) ? "." : path);
            if (validateResult != null)
                return validateResult;

            if (!Directory.Exists(searchPath))
                return $"Error: Directory not found: {path}";

            var patterns = pattern.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var p in patterns)
            {
                foreach (var f in EnumerateFilesRecursive(searchPath, p, cancellationToken))
                    files.Add(f);
            }

            var sorted = files
                .Select(f =>
                {
                    try { return (Path: f, ModTime: File.GetLastWriteTimeUtc(f)); }
                    catch { return (Path: f, ModTime: DateTime.MinValue); }
                })
                .OrderByDescending(f => f.ModTime)
                .Take(MaxFindResults)
                .ToList();

            if (sorted.Count == 0)
                return "No files found.";

            var truncated = files.Count > MaxFindResults;
            var sb = new StringBuilder();
            sb.AppendLine($"Found {files.Count} files{(truncated ? $" (showing first {MaxFindResults})" : "")}:");
            foreach (var f in sorted)
            {
                sb.AppendLine(Path.GetRelativePath(searchPath, f.Path));
            }

            if (truncated)
                sb.AppendLine($"\n(Results truncated. Consider using a more specific path or pattern.)");

            return sb.ToString();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return $"Error finding files: {ex.Message}";
        }
    }

    #region Private Helpers

    /// <summary>
    /// Detect file encoding by inspecting the BOM (Byte Order Mark).
    /// Falls back to UTF-8 without BOM when no BOM is found.
    /// </summary>
    private static Encoding DetectFileEncoding(string filePath)
    {
        Span<byte> bom = stackalloc byte[4];
        int bytesRead;
        using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        {
            bytesRead = fs.Read(bom);
        }

        // UTF-32 BE: 00 00 FE FF (check before UTF-16 BE)
        if (bytesRead >= 4 && bom[0] == 0x00 && bom[1] == 0x00 && bom[2] == 0xFE && bom[3] == 0xFF)
            return new UTF32Encoding(bigEndian: true, byteOrderMark: true);

        // UTF-32 LE: FF FE 00 00 (check before UTF-16 LE)
        if (bytesRead >= 4 && bom[0] == 0xFF && bom[1] == 0xFE && bom[2] == 0x00 && bom[3] == 0x00)
            return new UTF32Encoding(bigEndian: false, byteOrderMark: true);

        // UTF-8 BOM: EF BB BF
        if (bytesRead >= 3 && bom[0] == 0xEF && bom[1] == 0xBB && bom[2] == 0xBF)
            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);

        // UTF-16 LE: FF FE
        if (bytesRead >= 2 && bom[0] == 0xFF && bom[1] == 0xFE)
            return Encoding.Unicode;

        // UTF-16 BE: FE FF
        if (bytesRead >= 2 && bom[0] == 0xFE && bom[1] == 0xFF)
            return Encoding.BigEndianUnicode;

        return Utf8NoBom;
    }

    private static string FormatDirectoryListing(string fullPath, string originalPath)
    {
        var items = Directory.GetFileSystemEntries(fullPath)
            .OrderBy(x => x)
            .Select(x =>
            {
                var name = Path.GetFileName(x);
                var prefix = Directory.Exists(x) ? "[DIR] " : "[FILE] ";
                return $"{prefix}{name}";
            });

        var result = string.Join("\n", items);
        return string.IsNullOrWhiteSpace(result) ? $"Directory {originalPath} is empty" : result;
    }

    private static IEnumerable<string> EnumerateSearchableFiles(
        string rootPath,
        string? includePattern,
        CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrEmpty(includePattern))
        {
            var patterns = includePattern.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var p in patterns)
            {
                foreach (var f in EnumerateFilesRecursive(rootPath, p, cancellationToken))
                    yield return f;
            }
        }
        else
        {
            foreach (var f in EnumerateFilesRecursive(rootPath, cancellationToken: cancellationToken))
                yield return f;
        }
    }

    private static IEnumerable<string> EnumerateFilesRecursive(
        string rootPath,
        string searchPattern = "*",
        CancellationToken cancellationToken = default)
    {
        var dirs = new Stack<string>();
        dirs.Push(rootPath);

        while (dirs.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var dir = dirs.Pop();

            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(dir, searchPattern);
            }
            catch { continue; }

            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return file;
            }

            try
            {
                foreach (var subDir in Directory.EnumerateDirectories(dir))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var dirName = Path.GetFileName(subDir);
                    if (SkipDirectories.Contains(dirName))
                        continue;
                    dirs.Push(subDir);
                }
            }
            catch
            {
                // ignored
            }
        }
    }

    private static bool IsBinaryFile(string filePath)
        => FileContentClassifier.IsKnownBinaryExtension(filePath);

    private string ResolvePath(string path)
        => _fileAccessGuard.ResolvePath(path);

    private static readonly Regex UnicodeEscapeRegex = new(@"\\u([0-9a-fA-F]{4})", RegexOptions.Compiled);

    private static string UnescapeUnicodeSequences(string input)
    {
        if (!input.Contains("\\u"))
            return input;

        return UnicodeEscapeRegex.Replace(input, match =>
            ((char)Convert.ToInt32(match.Groups[1].Value, 16)).ToString());
    }

    private async Task<string?> ValidatePathAsync(string fullPath, string operation, string originalPath)
        => await _fileAccessGuard.ValidatePathAsync(fullPath, operation, originalPath);

    private async Task NotifyLspFileChangedAsync(string fullPath, string content)
    {
        if (lspServerManager == null)
            return;

        try
        {
            await lspServerManager.ChangeFileAsync(fullPath, content);
            await lspServerManager.SaveFileAsync(fullPath);
        }
        catch
        {
            // LSP sync is best-effort and should not fail write/edit operations.
        }
    }

    private static async Task<(string Result, string? WrittenContent)> ApplySearchReplaceEdit(
        string fullPath, string displayPath, string content, string oldText, string newText,
        Encoding encoding, bool replaceAll)
    {
        // Normalize all inputs to LF for consistent matching, restore on write
        var useCrLf = UsesCrLf(content);
        content = NormalizeToLf(content);
        oldText = NormalizeToLf(oldText);
        newText = NormalizeToLf(newText);

        var (ok, newLfContent, error, matchKind, lineNum, oldLineCount, replaceCount) =
            FileEditSearchReplace.Apply(content, oldText, newText, replaceAll);
        if (!ok)
            return (error!, null);

        var newContent = RestoreLineEndings(newLfContent, useCrLf);
        await File.WriteAllTextAsync(fullPath, newContent, encoding);

        if (replaceCount > 1)
            return ($"Successfully replaced {replaceCount} occurrences in {displayPath}", newContent);

        var newLineCount = string.IsNullOrEmpty(newText) ? 0 : newText.Count(c => c == '\n') + 1;
        var suffix = matchKind != null ? $" ({matchKind})" : "";
        return ($"Successfully edited {displayPath} at line {lineNum} ({oldLineCount} -> {newLineCount} lines){suffix}", newContent);
    }

    private static async Task WriteAllTextEnsuringDirectoryAsync(string fullPath, string content, Encoding encoding)
    {
        try
        {
            await File.WriteAllTextAsync(fullPath, content, encoding);
        }
        catch (DirectoryNotFoundException)
        {
            var directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            await File.WriteAllTextAsync(fullPath, content, encoding);
        }
    }

    private static async Task<T> WithSharingViolationRetryAsync<T>(
        Func<Task<T>> operation,
        CancellationToken cancellationToken = default)
    {
        var delays = new[] { 20, 40, 80 };
        for (var attempt = 0; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return await operation();
            }
            catch (IOException ex) when (attempt < delays.Length && IsSharingOrLockViolation(ex))
            {
                await Task.Delay(delays[attempt], cancellationToken);
            }
        }
    }

    private static bool IsSharingOrLockViolation(IOException ex)
        => ex.HResult is unchecked((int)0x80070020) or unchecked((int)0x80070021);

    private static bool UsesCrLf(string content)
        => content.Contains("\r\n");

    private static string NormalizeToLf(string content)
        => content.Replace("\r\n", "\n");

    private static string RestoreLineEndings(string content, bool useCrLf)
        => useCrLf ? content.Replace("\n", "\r\n") : content;

    #endregion
}
