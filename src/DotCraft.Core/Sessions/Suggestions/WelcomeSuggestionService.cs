using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using DotCraft.Configuration;
using DotCraft.Memory;
using DotCraft.Tools;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace DotCraft.Sessions;

public interface IWelcomeSuggestionService
{
    Task<WelcomeSuggestionSnapshot> SuggestAsync(
        WelcomeSuggestionRequest parameters,
        CancellationToken cancellationToken = default);

    void ScheduleRefresh(string workspacePath, string? triggerThreadId = null);

    void ClearWorkspaceCache(string workspacePath);
}

public sealed class WelcomeSuggestionService(
    ISessionService sessionService,
    SessionPersistenceService persistence,
    MemoryStore memoryStore,
    string workspaceRoot,
    ILogger<WelcomeSuggestionService>? logger = null) : IWelcomeSuggestionService, IAsyncDisposable
{
    private const int DefaultMaxItems = 4;
    private const int MaxItemsLimit = 4;
    private const int MinSnippetLength = 15;
    private const int MaxSnippetLength = 300;
    private const int MaxHighlightCount = 5;
    internal const int MemoryCharsLimit = 5_000;
    internal const int HistoryTailCharsLimit = 3_000;
    internal const int TotalMemoryCharsLimit = 8_000;
    private static readonly TimeSpan SuggestTimeout = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan RefreshDebounce = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan MinRefreshInterval = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan PersistedWriteTimeout = TimeSpan.FromSeconds(5);
    private const int PersistedCacheSchemaVersion = 2;
    private static readonly Regex FileExtensionPattern = new(@"\.[A-Za-z0-9]{1,6}\b", RegexOptions.Compiled);
    private static readonly Regex PathPattern = new(@"[A-Za-z0-9_.\-]+[\\/][A-Za-z0-9_.\-]+", RegexOptions.Compiled);
    private static readonly Regex BacktickPattern = new(@"\x60[^\x60]+\x60", RegexOptions.Compiled);
    private static readonly Regex IdentifierShapePattern = new(
        @"\b[a-z][a-z0-9]+[A-Z][A-Za-z0-9]*\b|\b[A-Z][a-z0-9]+[A-Z][A-Za-z0-9]*\b|\b[A-Za-z0-9]+_[A-Za-z0-9_]+\b|\b[a-z][a-z0-9]+(?:-[a-z0-9]+)+\b",
        RegexOptions.Compiled);
    private static readonly Regex AtOrHashRefPattern = new(@"(?<=^|\s)[@#][A-Za-z0-9_./\\-]+", RegexOptions.Compiled);

    private readonly ConcurrentDictionary<string, WelcomeSuggestionCacheEntry> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _refreshLock = new();
    private readonly CancellationTokenSource _lifetimeCts = new();
    private CancellationTokenSource? _pendingRefreshCts;
    private Task _latestRefreshTask = Task.CompletedTask;
    private DateTimeOffset _lastRefreshCompletedAt = DateTimeOffset.MinValue;

    public async Task<WelcomeSuggestionSnapshot> SuggestAsync(
        WelcomeSuggestionRequest parameters,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        if (parameters.Identity == null)
            throw new InvalidOperationException("identity is required.");

        var workspacePath = NormalizeWorkspacePath(parameters.Identity.WorkspacePath);
        if (string.IsNullOrWhiteSpace(workspacePath))
            throw new InvalidOperationException("identity.workspacePath is required.");
        if (!string.Equals(workspacePath, NormalizeWorkspacePath(workspaceRoot), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Requested workspace is not hosted by this AppServer instance.");

        var maxItems = ClampMaxItems(parameters.MaxItems);
        if (!IsWelcomeSuggestionsEnabled(workspacePath))
            return BuildNoSuggestionsResult(string.Empty);

        if (_cache.TryGetValue(workspacePath, out var cached) && cached.ExpiresAt > DateTimeOffset.UtcNow)
            return LimitResult(cached.Result, maxItems);

        var persisted = await LoadPersistedAsync(workspacePath, cancellationToken).ConfigureAwait(false);
        if (persisted != null)
        {
            _cache[workspacePath] = new WelcomeSuggestionCacheEntry(
                persisted,
                DateTimeOffset.UtcNow.Add(CacheTtl));
            return LimitResult(persisted, maxItems);
        }

        return BuildNoSuggestionsResult(string.Empty);
    }

    public void ScheduleRefresh(string workspacePath, string? triggerThreadId = null)
    {
        var normalizedWorkspace = NormalizeWorkspacePath(workspacePath);
        if (!string.Equals(normalizedWorkspace, NormalizeWorkspacePath(workspaceRoot), StringComparison.OrdinalIgnoreCase))
            return;

        lock (_refreshLock)
        {
            _pendingRefreshCts?.Cancel();
            _pendingRefreshCts?.Dispose();
            _pendingRefreshCts = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCts.Token);
            var refreshCt = _pendingRefreshCts.Token;
            _latestRefreshTask = Task.Run(
                () => RunScheduledRefreshAsync(normalizedWorkspace, triggerThreadId, refreshCt),
                CancellationToken.None);
        }
    }

    public void ClearWorkspaceCache(string workspacePath)
    {
        var normalizedWorkspace = NormalizeWorkspacePath(workspacePath);
        if (!string.Equals(normalizedWorkspace, NormalizeWorkspacePath(workspaceRoot), StringComparison.OrdinalIgnoreCase))
            return;

        _cache.TryRemove(normalizedWorkspace, out _);
        var persistedPath = BuildPersistedCachePath(normalizedWorkspace);
        if (File.Exists(persistedPath))
            File.Delete(persistedPath);
    }

    public async ValueTask DisposeAsync()
    {
        _lifetimeCts.Cancel();
        Task latestTask;
        lock (_refreshLock)
        {
            _pendingRefreshCts?.Cancel();
            _pendingRefreshCts?.Dispose();
            _pendingRefreshCts = null;
            latestTask = _latestRefreshTask;
        }

        try
        {
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await latestTask.WaitAsync(timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            logger?.LogDebug("Timed out while waiting for background welcome suggestion refresh to stop.");
        }
        catch (Exception ex)
        {
            logger?.LogDebug(ex, "Background welcome suggestion refresh ended with an error during disposal.");
        }
        finally
        {
            _lifetimeCts.Dispose();
        }
    }

    private async Task RunScheduledRefreshAsync(
        string workspacePath,
        string? triggerThreadId,
        CancellationToken cancellationToken)
    {
        try
        {
            if (!IsWelcomeSuggestionsEnabled(workspacePath))
                return;
            if (!string.IsNullOrWhiteSpace(triggerThreadId)
                && await IsInternalThreadAsync(triggerThreadId, cancellationToken).ConfigureAwait(false))
            {
                return;
            }

            await Task.Delay(RefreshDebounce, cancellationToken).ConfigureAwait(false);
            var minNextRefreshAt = _lastRefreshCompletedAt + MinRefreshInterval;
            if (minNextRefreshAt > DateTimeOffset.UtcNow)
            {
                var cooldown = minNextRefreshAt - DateTimeOffset.UtcNow;
                await Task.Delay(cooldown, cancellationToken).ConfigureAwait(false);
            }

            await RefreshSuggestionsAsync(workspacePath, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Canceled by a newer trigger or service shutdown.
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Background welcome suggestion refresh failed.");
        }
    }

    private async Task RefreshSuggestionsAsync(string workspacePath, CancellationToken cancellationToken)
    {
        const int RefreshMaxItems = DefaultMaxItems;
        var evidence = await BuildEvidenceAsync(workspacePath, RefreshMaxItems, cancellationToken).ConfigureAwait(false);
        if (!evidence.HasSufficientContext)
            return;
        if (await HasCurrentSnapshotAsync(workspacePath, evidence.Fingerprint, cancellationToken).ConfigureAwait(false))
        {
            _lastRefreshCompletedAt = DateTimeOffset.UtcNow;
            return;
        }

        var identity = new SessionIdentity
        {
            ChannelName = WelcomeSuggestionConstants.ChannelName,
            UserId = WelcomeSuggestionConstants.InternalUserId,
            ChannelContext = $"welcome-refresh:{evidence.Fingerprint}",
            WorkspacePath = workspacePath
        };

        var result = await GenerateDynamicSuggestionsAsync(identity, evidence, RefreshMaxItems, cancellationToken)
            .ConfigureAwait(false);
        if (!string.Equals(result.Source, "dynamic", StringComparison.OrdinalIgnoreCase)
            || result.Items.Count == 0)
        {
            return;
        }

        _cache[workspacePath] = new WelcomeSuggestionCacheEntry(
            result,
            DateTimeOffset.UtcNow.Add(CacheTtl));
        await SavePersistedBestEffortAsync(workspacePath, result).ConfigureAwait(false);
        _lastRefreshCompletedAt = DateTimeOffset.UtcNow;
    }

    private async Task<bool> HasCurrentSnapshotAsync(
        string workspacePath,
        string fingerprint,
        CancellationToken cancellationToken)
    {
        if (_cache.TryGetValue(workspacePath, out var cached)
            && cached.ExpiresAt > DateTimeOffset.UtcNow
            && IsDynamicSnapshotForFingerprint(cached.Result, fingerprint))
        {
            return true;
        }

        var persisted = await LoadPersistedAsync(workspacePath, cancellationToken).ConfigureAwait(false);
        if (!IsDynamicSnapshotForFingerprint(persisted, fingerprint))
            return false;

        _cache[workspacePath] = new WelcomeSuggestionCacheEntry(
            persisted!,
            DateTimeOffset.UtcNow.Add(CacheTtl));
        return true;
    }

    private async Task<bool> IsInternalThreadAsync(string threadId, CancellationToken cancellationToken)
    {
        var normalizedThreadId = threadId.Trim();
        if (normalizedThreadId.Length == 0)
            return false;
        var summaries = await persistence.LoadIndexAsync(cancellationToken).ConfigureAwait(false);
        var summary = summaries.FirstOrDefault(item => string.Equals(item.Id, normalizedThreadId, StringComparison.Ordinal));
        return summary != null && IsInternalThread(summary);
    }

    private static WelcomeSuggestionSnapshot LimitResult(WelcomeSuggestionSnapshot source, int maxItems)
    {
        var items = source.Items.Take(maxItems).ToList();
        return new WelcomeSuggestionSnapshot
        {
            Source = source.Source,
            Fingerprint = source.Fingerprint,
            GeneratedAt = source.GeneratedAt,
            Items = items
        };
    }

    private static string BuildPersistedCachePath(string workspacePath) =>
        Path.Combine(workspacePath, ".craft", "cache", "welcome-suggestions.json");

    private static string BuildPersistedTempPath(string persistedPath) => $"{persistedPath}.tmp";

    private async Task SavePersistedAsync(
        string workspacePath,
        WelcomeSuggestionSnapshot result,
        CancellationToken cancellationToken)
    {
        var persistedPath = BuildPersistedCachePath(workspacePath);
        var parentDir = Path.GetDirectoryName(persistedPath);
        if (!string.IsNullOrWhiteSpace(parentDir))
            Directory.CreateDirectory(parentDir);

        var payload = new PersistedWelcomeSuggestionsPayload
        {
            SchemaVersion = PersistedCacheSchemaVersion,
            Result = result
        };
        var json = JsonSerializer.Serialize(payload);
        var tempPath = BuildPersistedTempPath(persistedPath);
        await File.WriteAllTextAsync(tempPath, json, Encoding.UTF8, cancellationToken).ConfigureAwait(false);
        File.Move(tempPath, persistedPath, overwrite: true);
    }

    private async Task SavePersistedBestEffortAsync(
        string workspacePath,
        WelcomeSuggestionSnapshot result)
    {
        try
        {
            using var writeCts = new CancellationTokenSource(PersistedWriteTimeout);
            await SavePersistedAsync(workspacePath, result, writeCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            logger?.LogDebug("Timed out while writing persisted welcome suggestions cache.");
        }
        catch (Exception ex)
        {
            logger?.LogDebug(ex, "Failed to write persisted welcome suggestions cache; keeping previous snapshot if present.");
        }
    }

    private async Task<WelcomeSuggestionSnapshot?> LoadPersistedAsync(
        string workspacePath,
        CancellationToken cancellationToken)
    {
        var persistedPath = BuildPersistedCachePath(workspacePath);
        if (!File.Exists(persistedPath))
            return null;

        try
        {
            var json = await File.ReadAllTextAsync(persistedPath, Encoding.UTF8, cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(json))
                return null;
            var payload = JsonSerializer.Deserialize<PersistedWelcomeSuggestionsPayload>(json);
            if (payload is not { SchemaVersion: PersistedCacheSchemaVersion })
                return null;
            if (payload.Result == null || !string.Equals(payload.Result.Source, "dynamic", StringComparison.OrdinalIgnoreCase))
                return null;
            if (payload.Result.Items.Count == 0)
                return null;
            return payload.Result;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger?.LogDebug(ex, "Failed to read persisted welcome suggestions cache; ignoring file.");
            return null;
        }
    }

    private static bool IsDynamicSnapshotForFingerprint(WelcomeSuggestionSnapshot? result, string fingerprint) =>
        result != null
        && string.Equals(result.Source, "dynamic", StringComparison.OrdinalIgnoreCase)
        && result.Items.Count > 0
        && string.Equals(result.Fingerprint, fingerprint, StringComparison.Ordinal);

    private async Task<WelcomeSuggestionSnapshot> GenerateDynamicSuggestionsAsync(
        SessionIdentity identity,
        WelcomeSuggestionEvidence evidence,
        int maxItems,
        CancellationToken cancellationToken)
    {
        string? tempThreadId = null;
        try
        {
            using var timeoutCts = new CancellationTokenSource(SuggestTimeout);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            var tempThread = await sessionService.CreateThreadAsync(
                    new SessionIdentity
                    {
                        ChannelName = WelcomeSuggestionConstants.ChannelName,
                        UserId = WelcomeSuggestionConstants.InternalUserId,
                        WorkspacePath = identity.WorkspacePath,
                        ChannelContext = $"welcome-suggest:{evidence.Fingerprint}"
                    },
                    new ThreadConfiguration
                    {
                        Mode = "agent",
                        ToolProfile = WelcomeSuggestionConstants.ToolProfileName,
                        UseToolProfileOnly = true,
                        ApprovalPolicy = ApprovalPolicy.AutoApprove,
                        AgentInstructions = WelcomeSuggestionInstructions.SystemPrompt
                    },
                    HistoryMode.Server,
                    displayName: "[internal] Welcome suggestions",
                    ct: linked.Token)
                .ConfigureAwait(false);

            tempThreadId = tempThread.Id;
            tempThread.Metadata[WelcomeSuggestionConstants.InternalMetadataKey] =
                WelcomeSuggestionConstants.InternalMetadataValue;
            await persistence.SaveThreadAsync(tempThread, linked.Token).ConfigureAwait(false);

            List<WelcomeSuggestion>? items = null;
            await foreach (var evt in sessionService.SubmitInputAsync(
                               tempThreadId,
                               [new TextContent(BuildGenerationPrompt(maxItems))],
                               ct: linked.Token).ConfigureAwait(false))
            {
                if (evt.EventType != SessionEventType.ItemCompleted || evt.ItemPayload == null)
                    continue;
                if (evt.ItemPayload.Type != ItemType.ToolCall)
                    continue;
                var tc = evt.ItemPayload.AsToolCall;
                if (tc == null || !string.Equals(tc.ToolName, WelcomeSuggestionMethods.ToolName, StringComparison.Ordinal))
                    continue;

                items = ParseSuggestionItems(tc.Arguments, maxItems);
                if (items.Count == maxItems)
                    break;
            }

            if (items == null || items.Count != maxItems)
                throw new InvalidOperationException("The model did not emit the expected welcome suggestions.");

            return new WelcomeSuggestionSnapshot
            {
                Items = items,
                Source = "dynamic",
                GeneratedAt = DateTimeOffset.UtcNow,
                Fingerprint = evidence.Fingerprint
            };
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger?.LogWarning("Welcome suggestion generation timed out; returning no personalized suggestions.");
            return BuildNoSuggestionsResult(evidence.Fingerprint);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger?.LogWarning(ex, "Welcome suggestion generation failed; returning no personalized suggestions.");
            return BuildNoSuggestionsResult(evidence.Fingerprint);
        }
        finally
        {
            if (tempThreadId != null)
            {
                try
                {
                    using var cleanupCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                    await sessionService.DeleteThreadPermanentlyAsync(tempThreadId, cleanupCts.Token)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    logger?.LogWarning("Timeout while deleting ephemeral welcome-suggest thread {ThreadId}", tempThreadId);
                }
                catch (Exception ex)
                {
                    logger?.LogWarning(ex, "Failed to delete ephemeral welcome-suggest thread {ThreadId}", tempThreadId);
                }
            }
        }
    }

    private Task<WelcomeSuggestionEvidence> BuildEvidenceAsync(
        string workspacePath,
        int maxItems,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var memoryText = TrimToLimit(memoryStore.ReadLongTerm(), MemoryCharsLimit);
        var historyText = ReadHistoryTailFromFile(memoryStore.HistoryFilePath, HistoryTailCharsLimit);
        var combinedMemory = CombineMemory(memoryText, historyText, TotalMemoryCharsLimit);
        var fingerprint = BuildFingerprint(
            workspacePath,
            maxItems,
            memoryStore.LongTermFilePath,
            memoryStore.HistoryFilePath,
            combinedMemory);

        return Task.FromResult(new WelcomeSuggestionEvidence(
            fingerprint,
            !string.IsNullOrWhiteSpace(combinedMemory)));
    }

    private bool IsWelcomeSuggestionsEnabled(string workspacePath)
    {
        try
        {
            var configPath = Path.Combine(workspacePath, ".craft", "config.json");
            var mergedConfig = AppConfig.LoadWithGlobalFallback(configPath);
            return mergedConfig.WelcomeSuggestions.Enabled;
        }
        catch (Exception ex)
        {
            logger?.LogDebug(ex, "Failed to resolve welcome suggestion config; defaulting to enabled.");
            return true;
        }
    }

    private static string BuildGenerationPrompt(int maxItems) =>
        $"Inspect workspace MEMORY.md and HISTORY.md, infer the likely next tasks, and call {WelcomeSuggestionMethods.ToolName} exactly once with exactly {maxItems} concrete suggestions. If you cannot produce {maxItems} concrete suggestions from memory evidence, do not call the tool.";

    private static List<WelcomeSuggestion> ParseSuggestionItems(JsonObject? arguments, int maxItems)
    {
        if (arguments == null)
            return [];

        if (!arguments.TryGetPropertyValue("items", out var itemsNode) || itemsNode is not JsonArray itemsArray)
            return [];

        var items = new List<WelcomeSuggestion>(maxItems);
        foreach (var node in itemsArray)
        {
            if (node is not JsonObject obj)
                continue;

            var title = SanitizeSuggestionField(obj["title"]?.GetValue<string>(), 80);
            var prompt = SanitizeSuggestionField(obj["prompt"]?.GetValue<string>(), 500);
            var reason = SanitizeSuggestionField(obj["reason"]?.GetValue<string>(), 200);
            if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(prompt))
                continue;
            if (!IsSpecificSuggestion(title, prompt))
                continue;

            items.Add(new WelcomeSuggestion
            {
                Title = title,
                Prompt = prompt,
                Reason = reason
            });
        }

        return items.Take(maxItems).ToList();
    }

    private static string SanitizeSuggestionField(string? value, int maxChars)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var trimmed = value.Trim();
        trimmed = trimmed.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        trimmed = string.Join(" ", trimmed.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        if (trimmed.Length > maxChars)
            trimmed = trimmed[..maxChars].TrimEnd();
        return trimmed;
    }

    internal static string? NormalizeSnippet(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var collapsed = string.Join(
            " ",
            text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        if (collapsed.Length < MinSnippetLength || collapsed.Length > MaxSnippetLength)
            return null;
        if (IsSlashCommand(collapsed))
            return null;
        if (IsAcknowledgement(collapsed))
            return null;
        return collapsed;
    }

    private static bool IsSlashCommand(string text)
    {
        if (!text.StartsWith("/", StringComparison.Ordinal))
            return false;

        return !text.Contains('\n') && text.Count(ch => ch == ' ') <= 1 && text.Length <= 48;
    }

    private static bool IsAcknowledgement(string text)
    {
        var normalized = text.Trim().ToLowerInvariant();
        return normalized is "ok" or "okay" or "thanks" or "thank you" or "got it" or "continue" or "继续" or "好的" or "收到" or "明白了";
    }

    internal static string[] ExtractMemoryHighlights(string memoryText, string historyText)
    {
        return ExtractCandidateHighlights(memoryText)
            .Concat(ExtractCandidateHighlights(historyText))
            .Where(text => ScoreSnippetSpecificity(text) > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(ScoreSnippetSpecificity)
            .ThenByDescending(text => text.Length)
            .Take(MaxHighlightCount)
            .Select(text => SanitizeSuggestionField(text, 180))
            .ToArray();
    }

    internal static string CombineMemory(string memoryText, string historyText, int totalMemoryCharsLimit)
    {
        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(memoryText))
        {
            sb.AppendLine("## MEMORY.md");
            sb.AppendLine(memoryText.Trim());
        }

        if (!string.IsNullOrWhiteSpace(historyText))
        {
            if (sb.Length > 0)
                sb.AppendLine();
            sb.AppendLine("## HISTORY.md (tail)");
            sb.AppendLine(historyText.Trim());
        }

        var combined = sb.ToString().Trim();
        return TrimToLimit(combined, totalMemoryCharsLimit);
    }

    internal static string ReadHistoryTailFromFile(string historyFilePath, int maxChars)
    {
        if (!File.Exists(historyFilePath))
            return string.Empty;

        try
        {
            var content = File.ReadAllText(historyFilePath, Encoding.UTF8);
            if (content.Length <= maxChars)
                return content;
            return content[^maxChars..];
        }
        catch
        {
            return string.Empty;
        }
    }

    internal static string TrimToLimit(string? text, int maxChars)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;
        var trimmed = text.Trim();
        return trimmed.Length <= maxChars ? trimmed : trimmed[^maxChars..].TrimStart();
    }

    private static bool IsSpecificSuggestion(string title, string prompt)
    {
        return HasSpecificitySignal(title) || HasSpecificitySignal(prompt);
    }

    private static bool HasSpecificitySignal(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        return FileExtensionPattern.IsMatch(text)
            || PathPattern.IsMatch(text)
            || BacktickPattern.IsMatch(text)
            || IdentifierShapePattern.IsMatch(text)
            || AtOrHashRefPattern.IsMatch(text)
            || text.Contains('`');
    }

    internal static int ScoreSnippetSpecificity(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return 0;

        var score = 0;
        score += FileExtensionPattern.Matches(text).Count * 3;
        score += PathPattern.Matches(text).Count * 2;
        score += BacktickPattern.Matches(text).Count * 2;
        score += IdentifierShapePattern.Matches(text).Count * 2;
        score += AtOrHashRefPattern.Matches(text).Count;
        if (text.Contains('`'))
            score += 1;
        return score;
    }

    private static IEnumerable<string> ExtractCandidateHighlights(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            yield break;

        var parts = text
            .Split(['\r', '\n', '.', '!', '?', '。', '！', '？'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var part in parts)
        {
            var normalized = NormalizeSnippet(part);
            if (normalized != null)
                yield return normalized;
        }
    }

    private static WelcomeSuggestionSnapshot BuildNoSuggestionsResult(string fingerprint) =>
        new()
        {
            Items = [],
            Source = "none",
            GeneratedAt = DateTimeOffset.UtcNow,
            Fingerprint = fingerprint
        };

    private static string BuildFingerprint(
        string workspacePath,
        int maxItems,
        string memoryPath,
        string historyPath,
        string memoryContext)
    {
        var sb = new StringBuilder();
        sb.AppendLine(workspacePath);
        sb.AppendLine($"maxItems:{maxItems}");
        sb.AppendLine($"memoryMtime:{GetFileTimestamp(memoryPath):O}");
        sb.AppendLine($"historyMtime:{GetFileTimestamp(historyPath):O}");
        sb.AppendLine(memoryContext);

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static DateTimeOffset GetFileTimestamp(string path) =>
        File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.UnixEpoch;

    internal static bool IsInternalThread(ThreadSummary summary)
    {
        return ThreadVisibility.IsInternal(summary);
    }

    internal static string NormalizeWorkspacePath(string? path) =>
        string.IsNullOrWhiteSpace(path)
            ? string.Empty
            : Path.GetFullPath(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

    private static int ClampMaxItems(int? maxItems)
    {
        if (maxItems is not > 0)
            return DefaultMaxItems;
        return Math.Min(maxItems.Value, MaxItemsLimit);
    }

    private sealed record WelcomeSuggestionEvidence(
        string Fingerprint,
        bool HasSufficientContext);

    private sealed record WelcomeSuggestionCacheEntry(
        WelcomeSuggestionSnapshot Result,
        DateTimeOffset ExpiresAt);

    private sealed class PersistedWelcomeSuggestionsPayload
    {
        public int SchemaVersion { get; set; }
        public WelcomeSuggestionSnapshot? Result { get; set; }
    }
}
