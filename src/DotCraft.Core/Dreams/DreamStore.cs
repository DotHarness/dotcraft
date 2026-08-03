using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using DotCraft.Configuration;
using DotCraft.Sessions;

namespace DotCraft.Dreams;

/// <summary>
/// Stores reviewable Dreams memory stores and run artifacts under the workspace .craft/dreams directory.
/// </summary>
public sealed class DreamStore
{
    private static readonly ConcurrentDictionary<string, object> StoreLocks = new(StringComparer.OrdinalIgnoreCase);

    private readonly string _dreamsDir;
    private readonly string _storesDir;
    private readonly string _runsDir;
    private readonly string _activeStateFile;
    private readonly object _syncRoot;

    public DreamStore(string craftPath)
    {
        _dreamsDir = Path.Combine(craftPath, "dreams");
        _storesDir = Path.Combine(_dreamsDir, "stores");
        _runsDir = Path.Combine(_dreamsDir, "runs");
        _activeStateFile = Path.Combine(_dreamsDir, "active.json");
        Directory.CreateDirectory(_storesDir);
        Directory.CreateDirectory(_runsDir);
        _syncRoot = StoreLocks.GetOrAdd(Path.GetFullPath(_dreamsDir), static _ => new object());
    }

    /// <summary>
    /// Gets the .craft/dreams directory path.
    /// </summary>
    public string DreamsDirectoryPath => _dreamsDir;

    /// <summary>
    /// Gets the directory that contains all Dream memory stores.
    /// </summary>
    public string StoresDirectoryPath => _storesDir;

    /// <summary>
    /// Gets the directory that contains Dream run records.
    /// </summary>
    public string RunsDirectoryPath => _runsDir;

    /// <summary>
    /// Gets the active Dream store id, if one has been applied.
    /// </summary>
    public string? GetActiveStoreId()
    {
        lock (_syncRoot)
        {
            if (!File.Exists(_activeStateFile))
                return null;

            try
            {
                var state = JsonSerializer.Deserialize<ActiveDreamStoreState>(
                    File.ReadAllText(_activeStateFile, Encoding.UTF8),
                    AppConfig.SerializerOptions);
                return string.IsNullOrWhiteSpace(state?.ActiveDreamStoreId)
                    ? null
                    : state.ActiveDreamStoreId;
            }
            catch (JsonException)
            {
                return null;
            }
        }
    }

    /// <summary>
    /// Sets the active Dream store id. Future agent prompts read this store's index.
    /// </summary>
    public void SetActiveStore(string storeId)
    {
        var normalized = NormalizeStoreId(storeId);
        lock (_syncRoot)
        {
            var descriptor = GetStoreDescriptorCore(normalized);
            if (!File.Exists(descriptor.IndexPath))
                throw new ArgumentException($"Dream store is missing INDEX.md: {storeId}", nameof(storeId));

            WriteAtomic(
                _activeStateFile,
                ".active",
                JsonSerializer.Serialize(
                    new ActiveDreamStoreState { ActiveDreamStoreId = normalized },
                    AppConfig.SerializerOptions) + Environment.NewLine);
        }
    }

    /// <summary>
    /// Reads the active Dream store index.
    /// </summary>
    public string ReadDream()
    {
        lock (_syncRoot)
        {
            var storeId = GetActiveStoreId();
            return string.IsNullOrWhiteSpace(storeId) ? string.Empty : ReadIndexCore(storeId);
        }
    }

    /// <summary>
    /// Reads a Dream store index by id.
    /// </summary>
    public string ReadIndex(string storeId)
    {
        var normalized = NormalizeStoreId(storeId);
        lock (_syncRoot)
            return ReadIndexCore(normalized);
    }

    /// <summary>
    /// Reads a topic file from the active Dream store by safe relative path.
    /// </summary>
    public string ReadTopicFile(string path)
    {
        var storeId = GetActiveStoreId();
        if (string.IsNullOrWhiteSpace(storeId))
            return string.Empty;

        return ReadTopicFile(storeId, path);
    }

    /// <summary>
    /// Reads a topic file from a Dream store by safe relative path.
    /// </summary>
    public string ReadTopicFile(string storeId, string path)
    {
        var normalizedStoreId = NormalizeStoreId(storeId);
        var normalizedPath = NormalizeTopicPath(path);
        lock (_syncRoot)
        {
            var fullPath = GetTopicPath(normalizedStoreId, normalizedPath);
            return File.Exists(fullPath) ? File.ReadAllText(fullPath, Encoding.UTF8) : string.Empty;
        }
    }

    /// <summary>
    /// Lists topic files from the active Dream store.
    /// </summary>
    public IReadOnlyList<DreamsTopicFileInput> ListTopicFiles(int previewChars = 600)
    {
        var storeId = GetActiveStoreId();
        return string.IsNullOrWhiteSpace(storeId)
            ? []
            : ListTopicFiles(storeId, previewChars);
    }

    /// <summary>
    /// Lists topic files from a Dream store.
    /// </summary>
    public IReadOnlyList<DreamsTopicFileInput> ListTopicFiles(string storeId, int previewChars = 600)
    {
        var normalizedStoreId = NormalizeStoreId(storeId);
        lock (_syncRoot)
        {
            var topicDir = GetStoreDescriptorCore(normalizedStoreId).TopicDirectoryPath;
            if (!Directory.Exists(topicDir))
                return [];

            var files = new List<DreamsTopicFileInput>();
            foreach (var file in Directory.EnumerateFiles(topicDir, "*.md", SearchOption.TopDirectoryOnly)
                         .OrderBy(static path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase))
            {
                var info = new FileInfo(file);
                var content = File.ReadAllText(file, Encoding.UTF8);
                files.Add(new DreamsTopicFileInput(
                    Path.GetFileName(file),
                    info.Length,
                    info.LastWriteTimeUtc,
                    TrimPreview(content, previewChars)));
            }

            return files;
        }
    }

    /// <summary>
    /// Creates an empty output store for a Dream run.
    /// </summary>
    public DreamStoreDescriptor CreateOutputStore(string runId, DateTimeOffset startedAt)
    {
        _ = NormalizeRunId(runId);
        var storeId = $"store_{startedAt:yyyyMMddHHmmss}_{Guid.NewGuid():N}"[..37];
        lock (_syncRoot)
        {
            var descriptor = GetStoreDescriptorCore(storeId);
            Directory.CreateDirectory(descriptor.DirectoryPath);
            Directory.CreateDirectory(descriptor.TopicDirectoryPath);
            return descriptor;
        }
    }

    /// <summary>
    /// Gets a Dream store descriptor by id.
    /// </summary>
    public DreamStoreDescriptor GetStoreDescriptor(string storeId)
    {
        var normalized = NormalizeStoreId(storeId);
        lock (_syncRoot)
            return GetStoreDescriptorCore(normalized);
    }

    /// <summary>
    /// Permanently deletes a Dream store directory when it exists.
    /// Callers must protect the active store before invoking this method.
    /// </summary>
    public bool DeleteStore(string storeId)
    {
        var normalized = NormalizeStoreId(storeId);
        lock (_syncRoot)
        {
            var directory = GetStoreDescriptorCore(normalized).DirectoryPath;
            if (!Directory.Exists(directory))
                return false;

            var attributes = File.GetAttributes(directory);
            Directory.Delete(directory, recursive: (attributes & FileAttributes.ReparsePoint) == 0);
            return true;
        }
    }

    /// <summary>
    /// Gets a run directory path by run id.
    /// </summary>
    public string GetRunDirectory(string runId) =>
        Path.Combine(_runsDir, NormalizeRunId(runId));

    /// <summary>
    /// Gets the run input snapshot directory path.
    /// </summary>
    public string GetRunInputDirectory(string runId) =>
        Path.Combine(GetRunDirectory(runId), "input");

    /// <summary>
    /// Validates a generated Dream store and returns the index contents.
    /// </summary>
    public string ValidateStore(string storeId)
    {
        var normalized = NormalizeStoreId(storeId);
        lock (_syncRoot)
        {
            var descriptor = GetStoreDescriptorCore(normalized);
            if (!File.Exists(descriptor.IndexPath))
                throw new InvalidOperationException("dream_output_index_missing");

            var index = File.ReadAllText(descriptor.IndexPath, Encoding.UTF8);
            if (!DreamsMarkdownValidation.IsValid(index))
                throw new InvalidOperationException("invalid_dream_index_markdown");

            foreach (var file in Directory.EnumerateFiles(descriptor.DirectoryPath, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(descriptor.DirectoryPath, file).Replace('\\', '/');
                if (string.Equals(relative, "INDEX.md", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(relative, "PRUNING_NOTES.md", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (relative.StartsWith("memory/", StringComparison.OrdinalIgnoreCase)
                    && relative.Count(static ch => ch == '/') == 1)
                {
                    _ = NormalizeTopicPath(relative["memory/".Length..]);
                    continue;
                }

                throw new InvalidOperationException($"dream_store_unexpected_file:{relative}");
            }

            foreach (var file in Directory.Exists(descriptor.TopicDirectoryPath)
                         ? Directory.EnumerateFiles(descriptor.TopicDirectoryPath, "*.md", SearchOption.TopDirectoryOnly)
                         : [])
            {
                var relative = Path.GetFileName(file);
                _ = NormalizeTopicPath(relative);
                if (new FileInfo(file).Length > 100_000)
                    throw new InvalidOperationException($"dream_topic_too_large:{relative}");
            }

            return DreamsMarkdownValidation.Normalize(index);
        }
    }

    /// <summary>
    /// Saves one Dream run write-set as a newly applied active store.
    /// This is a test and compatibility helper; normal Dreams runs create pending output stores.
    /// </summary>
    public DreamStoreWriteResult SaveDreamRun(string? dreamMarkdown, string? historyEntry)
        => SaveDreamRun(dreamMarkdown, null, null, historyEntry);

    /// <summary>
    /// Saves one Dream run write-set as a newly applied active store.
    /// This is a test and compatibility helper; normal Dreams runs create pending output stores.
    /// </summary>
    public DreamStoreWriteResult SaveDreamRun(
        string? dreamMarkdown,
        IReadOnlyList<DreamTopicFileWrite>? topicFiles,
        IReadOnlyList<string>? deletedTopicPaths,
        string? historyEntry)
    {
        _ = historyEntry;
        var startedAt = DateTimeOffset.UtcNow;
        var descriptor = CreateOutputStore($"compat_{startedAt:yyyyMMddHHmmss}", startedAt);
        var normalizedDream = string.IsNullOrWhiteSpace(dreamMarkdown)
            ? null
            : DreamsMarkdownValidation.Normalize(dreamMarkdown);
        if (string.IsNullOrWhiteSpace(normalizedDream) || !DreamsMarkdownValidation.IsValid(normalizedDream))
            throw new ArgumentException("Dream index markdown is invalid.", nameof(dreamMarkdown));

        var normalizedWrites = NormalizeTopicWrites(topicFiles);
        var normalizedDeletes = NormalizeTopicDeletes(deletedTopicPaths);
        EnsureNoTopicConflicts(normalizedWrites, normalizedDeletes);

        lock (_syncRoot)
        {
            WriteAtomic(descriptor.IndexPath, ".INDEX", normalizedDream);
            foreach (var write in normalizedWrites)
                WriteAtomic(GetTopicPath(descriptor.StoreId, write.Path), ".dream-topic", write.Content);
            foreach (var path in normalizedDeletes)
            {
                var target = GetTopicPath(descriptor.StoreId, path);
                if (File.Exists(target))
                    File.Delete(target);
            }

            SetActiveStore(descriptor.StoreId);
            var written = new List<string> { $"stores/{descriptor.StoreId}/INDEX.md" };
            written.AddRange(normalizedWrites.Select(write => $"stores/{descriptor.StoreId}/memory/{write.Path}"));
            return new DreamStoreWriteResult(true, false, normalizedWrites.Count, normalizedDeletes.Count, written);
        }
    }

    /// <summary>
    /// Clears every file and subdirectory in the Dreams directory while preserving the root directory.
    /// </summary>
    public void ClearAll()
    {
        lock (_syncRoot)
        {
            RejectReparsePointRoot();
            Directory.CreateDirectory(_dreamsDir);

            foreach (var entry in Directory.EnumerateFileSystemEntries(_dreamsDir).ToArray())
            {
                var attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.Directory) != 0)
                {
                    Directory.Delete(entry, recursive: (attributes & FileAttributes.ReparsePoint) == 0);
                }
                else
                {
                    File.Delete(entry);
                }
            }

            Directory.CreateDirectory(_storesDir);
            Directory.CreateDirectory(_runsDir);
        }
    }

    private DreamStoreDescriptor GetStoreDescriptorCore(string storeId)
    {
        var dir = Path.Combine(_storesDir, storeId);
        return new DreamStoreDescriptor(
            storeId,
            dir,
            Path.Combine(dir, "INDEX.md"),
            Path.Combine(dir, "memory"));
    }

    private string ReadIndexCore(string storeId)
    {
        var descriptor = GetStoreDescriptorCore(storeId);
        return File.Exists(descriptor.IndexPath)
            ? File.ReadAllText(descriptor.IndexPath, Encoding.UTF8)
            : string.Empty;
    }

    private string GetTopicPath(string storeId, string normalizedPath) =>
        Path.Combine(GetStoreDescriptorCore(storeId).TopicDirectoryPath, normalizedPath);

    private void WriteAtomic(string targetPath, string tempPrefix, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
        var tempFile = Path.Combine(Path.GetDirectoryName(targetPath)!, $"{tempPrefix}.{Guid.NewGuid():N}.tmp");
        File.WriteAllText(tempFile, content, Encoding.UTF8);
        try
        {
            if (File.Exists(targetPath))
            {
                File.Replace(tempFile, targetPath, null);
            }
            else
            {
                File.Move(tempFile, targetPath);
            }
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    private void RejectReparsePointRoot()
    {
        if (!Directory.Exists(_dreamsDir))
            return;

        var attributes = File.GetAttributes(_dreamsDir);
        if ((attributes & FileAttributes.ReparsePoint) != 0)
            throw new IOException($"Refusing to clear reparse-point Dreams directory: {_dreamsDir}");
    }

    private static string TrimPreview(string value, int maxChars)
    {
        if (string.IsNullOrEmpty(value) || maxChars <= 0)
            return string.Empty;
        return value.Length <= maxChars ? value : value[..maxChars] + "\n[trimmed]";
    }

    private static string NormalizeStoreId(string storeId)
    {
        if (string.IsNullOrWhiteSpace(storeId))
            throw new ArgumentException("Dream store id is required.", nameof(storeId));
        var normalized = storeId.Trim();
        foreach (var ch in normalized)
        {
            if (char.IsAsciiLetterOrDigit(ch) || ch is '-' or '_')
                continue;
            throw new ArgumentException($"Dream store id contains invalid characters: {storeId}", nameof(storeId));
        }

        return normalized;
    }

    private static string NormalizeRunId(string runId)
    {
        if (string.IsNullOrWhiteSpace(runId))
            throw new ArgumentException("Dream run id is required.", nameof(runId));
        var normalized = runId.Trim();
        foreach (var ch in normalized)
        {
            if (char.IsAsciiLetterOrDigit(ch) || ch is '-' or '_')
                continue;
            throw new ArgumentException($"Dream run id contains invalid characters: {runId}", nameof(runId));
        }

        return normalized;
    }

    private static List<DreamTopicFileWrite> NormalizeTopicWrites(IReadOnlyList<DreamTopicFileWrite>? topicFiles)
    {
        if (topicFiles is not { Count: > 0 })
            return [];

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var writes = new List<DreamTopicFileWrite>(topicFiles.Count);
        foreach (var topic in topicFiles)
        {
            var path = NormalizeTopicPath(topic.Path);
            if (!seen.Add(path))
                throw new ArgumentException($"Duplicate Dreams topic path: {path}", nameof(topicFiles));
            if (string.IsNullOrWhiteSpace(topic.Content))
                throw new ArgumentException($"Dreams topic content is empty: {path}", nameof(topicFiles));
            if (Encoding.UTF8.GetByteCount(topic.Content) > 100_000)
                throw new ArgumentException($"Dreams topic file is too large: {path}", nameof(topicFiles));
            writes.Add(new DreamTopicFileWrite
            {
                Path = path,
                Content = topic.Content.TrimEnd() + Environment.NewLine
            });
        }

        return writes;
    }

    private static List<string> NormalizeTopicDeletes(IReadOnlyList<string>? deletedTopicPaths)
    {
        if (deletedTopicPaths is not { Count: > 0 })
            return [];

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var deletes = new List<string>();
        foreach (var pathValue in deletedTopicPaths)
        {
            var path = NormalizeTopicPath(pathValue);
            if (seen.Add(path))
                deletes.Add(path);
        }

        return deletes;
    }

    private static void EnsureNoTopicConflicts(
        IReadOnlyList<DreamTopicFileWrite> writes,
        IReadOnlyList<string> deletes)
    {
        if (writes.Count == 0 || deletes.Count == 0)
            return;

        var deleteSet = deletes.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var write in writes)
        {
            if (deleteSet.Contains(write.Path))
                throw new ArgumentException($"Dreams topic path cannot be both written and deleted: {write.Path}");
        }
    }

    private static string NormalizeTopicPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Dreams topic path is required.", nameof(path));
        if (Path.IsPathRooted(path))
            throw new ArgumentException($"Dreams topic path must be relative: {path}", nameof(path));

        var normalized = path.Trim().Replace('\\', '/').TrimStart('/');
        if (normalized.Contains("..", StringComparison.Ordinal) || normalized.Contains('/', StringComparison.Ordinal))
            throw new ArgumentException($"Dreams topic path must be a safe top-level markdown file: {path}", nameof(path));
        if (!normalized.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"Dreams topic path must end with .md: {path}", nameof(path));
        if (normalized.Length > 120)
            throw new ArgumentException($"Dreams topic path is too long: {path}", nameof(path));

        foreach (var ch in normalized)
        {
            if (char.IsAsciiLetterOrDigit(ch) || ch is '-' or '_' or '.')
                continue;
            throw new ArgumentException($"Dreams topic path must use slug characters: {path}");
        }

        return normalized.ToLowerInvariant();
    }

    private sealed class ActiveDreamStoreState
    {
        public string? ActiveDreamStoreId { get; set; }
    }
}

/// <summary>
/// Describes which Dreams artifacts changed during a store write.
/// </summary>
public readonly record struct DreamStoreWriteResult(
    bool DreamWritten,
    bool HistoryWritten,
    int TopicFilesWritten,
    int TopicFilesDeleted,
    IReadOnlyList<string> WrittenPaths)
{
    public bool AnyWritten => DreamWritten || HistoryWritten || TopicFilesWritten > 0 || TopicFilesDeleted > 0;
}
