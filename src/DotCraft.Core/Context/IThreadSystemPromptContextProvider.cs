using DotCraft.Contributions;

namespace DotCraft.Context;

/// <summary>
/// Declares where a thread-scoped prompt section is delivered to the model.
/// </summary>
public enum ThreadPromptPlacement
{
    /// <summary>
    /// Section participates in the generated base instructions. Only valid for content that is
    /// reproducible from configuration and workspace state, because it becomes part of the
    /// provider-visible static cache prefix.
    /// </summary>
    BaseInstructions,

    /// <summary>
    /// Section is delivered as an appended thread context item in conversation history. Required for
    /// content that depends on the running thread or on an attached client connection.
    /// </summary>
    ThreadContextItem
}

/// <summary>
/// Supplies a thread-scoped prompt section through a named context page.
/// </summary>
public interface IThreadSystemPromptContextProvider : IContributionContract
{
    /// <summary>
    /// Stable cache key for this provider's prompt section.
    /// </summary>
    ContextPageKey ContextPageKey { get; }

    /// <summary>
    /// Where this provider's section is delivered. Connection-bound providers must use
    /// <see cref="ThreadPromptPlacement.ThreadContextItem"/> so a client binding change cannot
    /// invalidate the thread's cached instruction prefix.
    /// </summary>
    ThreadPromptPlacement Placement => ThreadPromptPlacement.BaseInstructions;

    /// <summary>
    /// Builds the model-visible section for the current thread.
    /// </summary>
    string? GetSystemPromptSection(ThreadSystemPromptContext context);
}

public sealed record ThreadSystemPromptContext(
    string ThreadId,
    string WorkspacePath,
    string? OriginChannel = null);
