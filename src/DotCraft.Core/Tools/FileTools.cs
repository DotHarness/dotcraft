using System.ComponentModel;
using System.Text;
using System.Text.RegularExpressions;
using DotCraft.Lsp;
using DotCraft.Security;
using DotCraft.Sessions;
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
    TimeSpan? searchTimeout = null,
    IReadOnlyList<string>? workspaceRoots = null,
    bool managedSearchOnly = false)
{
    private const int DefaultGrepMatches = 100;

    private const int MaxGrepMatches = 2000;

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
        trustedReadPaths,
        workspaceRoots);
    private readonly RipgrepFileSearcher _ripgrep = new(ripgrepPath);
    private readonly TimeSpan _searchTimeout = NormalizeSearchTimeout(searchTimeout);

    [Description("Read a text file with line numbers, view an image (.png, .jpg, .jpeg, .gif, .webp, .bmp), or list a directory. Use offset/limit or GrepFiles for large text files. Read images without offset/limit. PDF and other binary formats are not supported.")]
    [Tool(Icon = "📄", DisplayType = typeof(CoreToolDisplays), DisplayMethod = nameof(CoreToolDisplays.ReadFile), MaxResultChars = 0)]
    [ToolRpc]
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

            if (TextFileReadLimiter.IsPagedRead(offset, limit))
                return ReadFileTextResult(await WithSharingViolationRetryAsync(
                    () => TextFileReadLimiter.ReadPageAsync(fullPath, path, offset, limit, cancellationToken),
                    cancellationToken));

            if (fileInfo.Length > TextFileReadLimiter.MaxUnpaginatedTextBytes)
                return ReadFileTextResult(TextFileReadLimiter.FormatUnpaginatedTooLarge(path, fileInfo.Length));

            var (content, _, error) = await WithSharingViolationRetryAsync(
                () => TextFileEncoding.ReadAsync(fullPath, path, cancellationToken),
                cancellationToken);
            return ReadFileTextResult(error ?? TextFileReadLimiter.FormatInMemory(content!, offset, limit));
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

    [Description("Create or overwrite a file, creating parent directories as needed. Use EditFile for targeted changes to existing files.")]
    [Tool(Icon = "✏️", DisplayType = typeof(CoreToolDisplays), DisplayMethod = nameof(CoreToolDisplays.WriteFile))]
    [ToolRpc]
    public async Task<ToolExecutionResult> WriteFile(
        [Description("The workspace-relative or absolute file path to write to.")] string path,
        [Description("The content to write.")] string content)
    {
        var outcome = new FileWriteOutcome();
        try
        {
            var fullPath = ResolvePath(path);
            var validateResult = await ValidatePathAsync(fullPath, "write", path);
            if (validateResult != null)
                return outcome.Fail(validateResult, ToolErrorCodes.AccessDenied);

            using (await PathAsyncMutex.AcquireAsync(fullPath))
            {
                var directory = Path.GetDirectoryName(fullPath);
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);

                var existedBefore = File.Exists(fullPath);
                var (before, encoding, error) = existedBefore
                    ? await TextFileEncoding.ReadAsync(fullPath, path)
                    : (null, Utf8NoBom, null);
                if (error != null)
                    return outcome.Fail(error, ToolErrorCodes.InputInvalid);
                content = RestoreLineEndings(NormalizeToLf(content), before != null && UsesCrLf(before));

                outcome.BeginWrite();
                await WriteAllTextEnsuringDirectoryAsync(fullPath, content, encoding);
                var lineCount = content.Split('\n').Length;
                var result = outcome.Complete(
                    CreateFileChange(fullPath, existedBefore ? FileChangeKind.Update : FileChangeKind.Add, before, content),
                    $"Successfully wrote {content.Length} bytes ({lineCount} lines) to {path}", ChangeReporter);
                await NotifyLspFileChangedAsync(fullPath, content);
                return result;
            }
        }
        catch (Exception ex)
        {
            return outcome.Fail(ex, "Error writing file");
        }
    }

    [Description("Replace a text snippet in an existing file. Copy oldText from the file, using a small snippet with enough context to identify the intended location. Use WriteFile for new files or full rewrites.")]
    [Tool(Icon = "🔄", DisplayType = typeof(CoreToolDisplays), DisplayMethod = nameof(CoreToolDisplays.EditFile))]
    [ToolRpc]
    public async Task<ToolExecutionResult> EditFile(
        [Description("The workspace-relative or absolute file path to edit.")] string path,
        [Description("The text to replace. Must identify one location unless replaceAll is true.")] string oldText = "",
        [Description("The replacement text, including the intended indentation. Use an empty string to delete the matched text.")] string newText = "",
        [Description("Replace every exact occurrence of oldText. Defaults to false.")] bool replaceAll = false)
    {
        var outcome = new FileWriteOutcome();
        try
        {
            var fullPath = ResolvePath(path);
            var validateResult = await ValidatePathAsync(fullPath, "edit", path);
            if (validateResult != null)
                return outcome.Fail(validateResult, ToolErrorCodes.AccessDenied);

            if (string.IsNullOrEmpty(oldText))
                return outcome.Fail("Error: oldText is required. Provide the exact snippet to find and replace.", ToolErrorCodes.InputInvalid);

            ToolExecutionResult result;
            string? writtenContent;
            using (await PathAsyncMutex.AcquireAsync(fullPath))
            {
                if (!File.Exists(fullPath))
                    return outcome.Fail($"Error: File not found: {path}", ToolErrorCodes.ExecutionFailed);

                var (content, encoding, error) = await TextFileEncoding.ReadAsync(fullPath, path);
                if (error != null)
                    return outcome.Fail(error, ToolErrorCodes.InputInvalid);

                var prepared = PrepareSearchReplaceEdit(path, content!, oldText, newText, replaceAll);
                if (prepared.WrittenContent == null)
                    return outcome.Fail(prepared.Result, ToolErrorCodes.InputInvalid);

                outcome.BeginWrite();
                await TextFileEncoding.WriteAsync(fullPath, prepared.WrittenContent, encoding);
                result = outcome.Complete(CreateFileChange(fullPath, FileChangeKind.Update, content, prepared.WrittenContent),
                    prepared.Result, ChangeReporter);
                writtenContent = prepared.WrittenContent;
            }

            if (writtenContent != null)
                await NotifyLspFileChangedAsync(fullPath, writtenContent);

            return result;
        }
        catch (Exception ex)
        {
            return outcome.Fail(ex, "Error editing file");
        }
    }

    [Description("Search file contents using a regular expression. Returns matching lines with file paths and line numbers. Skips binary files and .git/node_modules directories.")]
    [Tool(Icon = "🔍", DisplayType = typeof(CoreToolDisplays), DisplayMethod = nameof(CoreToolDisplays.GrepFiles), MaxResultChars = 20_000)]
    [ToolRpc]
    public async Task<string> GrepFiles(
        [Description("The regular expression pattern to search for.")] string pattern,
        [Description("The file or directory to search. Defaults to workspace root.")] string path = "",
        [Description("File name pattern for directory searches (e.g. \"*.cs\", \"*.json\"). An explicit file path searches that file.")] string include = "",
        [Description("Maximum number of matching lines to return. Defaults to 100, up to 2000.")] int limit = 0,
        CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var searchPath = string.IsNullOrEmpty(path) ? _workspaceRoot : ResolvePath(path);
            var validateResult = await ValidatePathAsync(searchPath, "read", string.IsNullOrEmpty(path) ? "." : path);
            if (validateResult != null)
                return validateResult;

            var isFile = File.Exists(searchPath);
            if (!isFile && !Directory.Exists(searchPath))
                return $"Error: Path not found: {path}";
            var searchRoot = isFile ? Path.GetDirectoryName(searchPath)! : searchPath;

            var maxMatches = limit > 0 ? Math.Min(limit, MaxGrepMatches) : DefaultGrepMatches;
            var ripgrepResult = managedSearchOnly ? null : await _ripgrep.SearchAsync(new RipgrepSearchRequest(
                searchPath,
                pattern,
                string.IsNullOrEmpty(include) ? null : include,
                maxMatches,
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
                var files = isFile ? [searchPath] : EnumerateSearchableFiles(searchPath, includePattern, fallbackCancellationToken);
                foreach (var filePath in files)
                {
                    fallbackCancellationToken.ThrowIfCancellationRequested();
                    if (totalMatches >= maxMatches)
                        break;

                    try
                    {
                        var fileInfo = new FileInfo(filePath);
                        if (fileInfo.Length > MaxGrepFileSize || fileInfo.Length == 0)
                            continue;

                        if (IsBinaryFile(filePath))
                            continue;

                        // Lenient on purpose, like ripgrep: a legacy-encoded file still yields its ASCII matches.
                        var lines = await File.ReadAllLinesAsync(filePath, fallbackCancellationToken);
                        for (var i = 0; i < lines.Length; i++)
                        {
                            fallbackCancellationToken.ThrowIfCancellationRequested();
                            if (regex.IsMatch(lines[i]))
                            {
                                totalMatches++;
                                matches.Add((filePath, i + 1, lines[i]));
                                if (totalMatches >= maxMatches)
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
            sb.AppendLine($"Found {matches.Count} matches{(totalMatches >= maxMatches ? $" (showing first {maxMatches}, there may be more)" : "")}:");

            var currentFile = "";
            foreach (var match in matches)
            {
                var relativePath = Path.GetRelativePath(searchRoot, match.FilePath);
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

    [Description("Find files by name pattern. Searches recursively, skipping .git and node_modules directories.")]
    [Tool(Icon = "📂", DisplayType = typeof(CoreToolDisplays), DisplayMethod = nameof(CoreToolDisplays.FindFiles))]
    [ToolRpc]
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

    private IEnumerable<string> EnumerateSearchableFiles(
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

    private IEnumerable<string> EnumerateFilesRecursive(
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
                if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) == 0
                    && blacklist?.IsBlacklisted(file) != true)
                    yield return file;
            }

            try
            {
                foreach (var subDir in Directory.EnumerateDirectories(dir))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var dirName = Path.GetFileName(subDir);
                    if (SkipDirectories.Contains(dirName)
                        || (File.GetAttributes(subDir) & FileAttributes.ReparsePoint) != 0
                        || blacklist?.IsBlacklisted(subDir) == true)
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

    internal Func<FileChangeRecord, System.Text.Json.JsonElement> ChangeReporter { get; init; } = FileChangeStructuredContent.Build;

    private FileChangeRecord CreateFileChange(string fullPath, FileChangeKind kind, string? before, string after)
    {
        var relative = Path.GetRelativePath(_workspaceRoot, fullPath).Replace('\\', '/');
        var insideWorkspace = !Path.IsPathRooted(relative)
            && relative != ".."
            && !relative.StartsWith("../", StringComparison.Ordinal);
        var displayPath = insideWorkspace ? relative : fullPath.Replace('\\', '/');
        return new FileChangeRecord(fullPath, displayPath, kind, before, after);
    }

    private static (string Result, string? WrittenContent) PrepareSearchReplaceEdit(
        string displayPath, string content, string oldText, string newText, bool replaceAll)
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
            await TextFileEncoding.WriteAsync(fullPath, content, encoding);
        }
        catch (DirectoryNotFoundException)
        {
            var directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            await TextFileEncoding.WriteAsync(fullPath, content, encoding);
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
