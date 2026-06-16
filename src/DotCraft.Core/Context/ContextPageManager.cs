using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace DotCraft.Context;

/// <summary>
/// Describes when a model-visible context page may be refreshed.
/// </summary>
public enum ContextPageLifecycle
{
    /// <summary>
    /// Load the page every time it is requested.
    /// </summary>
    Immediate,

    /// <summary>
    /// Keep the page stable for a thread until context compaction succeeds.
    /// </summary>
    StableUntilCompaction
}

/// <summary>
/// Stable identity for a discrete model-visible context page.
/// </summary>
public sealed record ContextPageKey(string Scope, string Name, string Variant)
{
    /// <summary>
    /// Returns true when this key marks every variant for the same scope and name.
    /// </summary>
    public bool IsVariantWildcard => string.Equals(Variant, "*", StringComparison.Ordinal);
}

/// <summary>
/// Cached content for a context page.
/// </summary>
public sealed record ContextPageSnapshot(
    ContextPageKey Key,
    string Content,
    string Fingerprint,
    DateTimeOffset LoadedAt,
    ContextPageLifecycle Lifecycle);

/// <summary>
/// Holds per-thread context pages that should remain prompt-cache stable until compaction.
/// </summary>
public interface IContextPageManager
{
    /// <summary>
    /// Gets an existing context page for the thread or loads and caches it.
    /// </summary>
    ContextPageSnapshot GetOrAdd(
        string? threadId,
        ContextPageKey key,
        ContextPageLifecycle lifecycle,
        Func<string> loader);

    /// <summary>
    /// Marks a context page as changed. Stable pages remain pinned until released.
    /// </summary>
    void MarkDirty(ContextPageKey key);

    /// <summary>
    /// Releases stable pages for a thread so the next request reloads them.
    /// </summary>
    void ReleaseStablePages(string threadId);

    /// <summary>
    /// Releases a stable context page for a thread so the next request reloads it.
    /// </summary>
    void ReleaseStablePage(string threadId, ContextPageKey key);

    /// <summary>
    /// Removes every cached page for a thread.
    /// </summary>
    void ForgetThread(string threadId);
}

/// <summary>
/// In-memory <see cref="IContextPageManager"/> used for AppServer-lived prompt prefix stability.
/// </summary>
public sealed class ContextPageManager : IContextPageManager
{
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<ContextPageKey, ContextPageSnapshot>> _stablePages =
        new(StringComparer.Ordinal);

    private readonly ConcurrentDictionary<ContextPageKey, byte> _dirtyKeys = new();

    /// <inheritdoc />
    public ContextPageSnapshot GetOrAdd(
        string? threadId,
        ContextPageKey key,
        ContextPageLifecycle lifecycle,
        Func<string> loader)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(loader);

        if (lifecycle == ContextPageLifecycle.Immediate || string.IsNullOrWhiteSpace(threadId))
            return CreateSnapshot(key, loader(), lifecycle);

        var normalizedThreadId = threadId.Trim();
        var pages = _stablePages.GetOrAdd(
            normalizedThreadId,
            static _ => new ConcurrentDictionary<ContextPageKey, ContextPageSnapshot>());

        var snapshot = pages.GetOrAdd(key, static (pageKey, state) =>
        {
            var content = state.Loader();
            state.Manager.ClearDirty(pageKey);
            return CreateSnapshot(
                pageKey,
                content,
                ContextPageLifecycle.StableUntilCompaction);
        }, (Manager: this, Loader: loader));

        return snapshot;
    }

    /// <inheritdoc />
    public void MarkDirty(ContextPageKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        _dirtyKeys[key] = 0;
    }

    /// <inheritdoc />
    public void ReleaseStablePages(string threadId)
    {
        if (string.IsNullOrWhiteSpace(threadId))
            return;

        _stablePages.TryRemove(threadId.Trim(), out _);
    }

    /// <inheritdoc />
    public void ReleaseStablePage(string threadId, ContextPageKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (string.IsNullOrWhiteSpace(threadId))
            return;

        if (!_stablePages.TryGetValue(threadId.Trim(), out var pages))
            return;

        if (!key.IsVariantWildcard)
        {
            pages.TryRemove(key, out _);
        }
        else
        {
            foreach (var existing in pages.Keys.Where(existing =>
                         string.Equals(existing.Scope, key.Scope, StringComparison.Ordinal)
                         && string.Equals(existing.Name, key.Name, StringComparison.Ordinal)).ToArray())
            {
                pages.TryRemove(existing, out _);
            }
        }

        if (pages.IsEmpty)
            _stablePages.TryRemove(threadId.Trim(), out _);
    }

    /// <inheritdoc />
    public void ForgetThread(string threadId) => ReleaseStablePages(threadId);

    private void ClearDirty(ContextPageKey key)
    {
        _dirtyKeys.TryRemove(key, out _);
        foreach (var dirty in _dirtyKeys.Keys.Where(dirty =>
                     dirty.IsVariantWildcard
                     && string.Equals(dirty.Scope, key.Scope, StringComparison.Ordinal)
                     && string.Equals(dirty.Name, key.Name, StringComparison.Ordinal)).ToArray())
        {
            _dirtyKeys.TryRemove(dirty, out _);
        }
    }

    private static ContextPageSnapshot CreateSnapshot(
        ContextPageKey key,
        string content,
        ContextPageLifecycle lifecycle) =>
        new(
            key,
            content,
            ComputeFingerprint(content),
            DateTimeOffset.UtcNow,
            lifecycle);

    private static string ComputeFingerprint(string content)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(content));
        return "sha256:" + Convert.ToHexString(bytes).ToLowerInvariant();
    }
}

/// <summary>
/// Shared context page key helpers.
/// </summary>
public static class ContextPageKeys
{
    public static ContextPageKey MemoryLongTerm(string variant = "") =>
        new("memory", "longTerm", variant);

    public static ContextPageKey SkillsAlways(string variant) =>
        new("skills", "always", variant);

    public static ContextPageKey SkillsSummary(string variant) =>
        new("skills", "summary", variant);

    public static ContextPageKey SkillsWildcard() =>
        new("skills", "*", "*");

    public static ContextPageKey BootstrapFiles(string variant) =>
        new("bootstrap", "files", variant);

    public static ContextPageKey CustomCommandsSummary(string variant) =>
        new("customCommands", "summary", variant);

    public static ContextPageKey AppContextBlocks() =>
        new("appBinding", "contextBlocks", "");

    public static ContextPageKey RuntimeAdditionalContext() =>
        new("runtime", "additionalContext", "");

    public static ContextPageKey AgentBuilderTarget(string variant) =>
        new("agentBuilder", "target", variant);
}
