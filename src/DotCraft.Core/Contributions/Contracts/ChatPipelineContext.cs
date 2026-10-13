using DotCraft.Agents;

namespace DotCraft.Contributions;

/// <summary>The agent pipeline being composed, passed to every <see cref="IChatMiddleware"/>.</summary>
public sealed class ChatPipelineContext
{
    /// <summary>Creates a pipeline context.</summary>
    public ChatPipelineContext(string? threadId = null)
    {
        ThreadId = threadId;
    }

    /// <summary>Gets the owning thread, or <see langword="null"/> when there is none.</summary>
    public string? ThreadId { get; }

    /// <summary>Gets the host inputs the built-in middleware reads, or <see langword="null"/> when the pipeline is composed without them.</summary>
    internal ChatPipelineHostInputs? Host { get; init; }
}
