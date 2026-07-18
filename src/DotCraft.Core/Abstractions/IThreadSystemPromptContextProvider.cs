using DotCraft.Context;

namespace DotCraft.Abstractions;

/// <summary>
/// Supplies a thread-scoped system-prompt section through a named context page.
/// </summary>
public interface IThreadSystemPromptContextProvider
{
    /// <summary>
    /// Stable cache key for this provider's prompt section.
    /// </summary>
    ContextPageKey ContextPageKey { get; }

    /// <summary>
    /// Builds the model-visible system-prompt section for the current thread.
    /// </summary>
    string? GetSystemPromptSection(ThreadSystemPromptContext context);
}

public sealed record ThreadSystemPromptContext(
    string ThreadId,
    string WorkspacePath,
    string? OriginChannel = null);
