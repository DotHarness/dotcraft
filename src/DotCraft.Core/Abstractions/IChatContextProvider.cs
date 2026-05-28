namespace DotCraft.Abstractions;

/// <summary>
/// Provides channel-specific context contributions for the agent prompt.
/// Implementations read from their own AsyncLocal scope at call time, so a single
/// registered instance serves all concurrent requests correctly.
/// </summary>
public interface IChatContextProvider
{
    /// <summary>
    /// Returns the static system prompt section for this channel, or null when no
    /// active context exists. Content must be stable within a session to allow
    /// LLM prompt cache reuse.
    /// </summary>
    string? GetSystemPromptSection();

    /// <summary>
    /// Returns dynamic context lines to append to the current user message.
    /// Use for per-message values (e.g. sender identity in a shared group session)
    /// that must not live in the system prompt.
    /// </summary>
    IEnumerable<string> GetRuntimeContextLines();
}
