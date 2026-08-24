using DotCraft.Agents;

namespace DotCraft.Contributions;

/// <summary>The chat client pipeline a middleware is being composed into.</summary>
public enum ChatPipelineKind
{
    /// <summary>The main agent pipeline, including tool-call orchestration.</summary>
    Agent,

    /// <summary>The restricted SubAgent pipeline.</summary>
    SubAgent
}

/// <summary>The pipeline being composed, passed to every <see cref="IChatMiddleware"/>.</summary>
public sealed class ChatPipelineContext
{
    /// <summary>Creates a pipeline context.</summary>
    public ChatPipelineContext(ChatPipelineKind kind, string? threadId = null)
    {
        Kind = kind;
        ThreadId = threadId;
    }

    /// <summary>Gets the pipeline being composed.</summary>
    public ChatPipelineKind Kind { get; }

    /// <summary>Gets the owning thread, or <see langword="null"/> when there is none.</summary>
    public string? ThreadId { get; }

    /// <summary>Gets the host inputs the built-in middleware reads, or <see langword="null"/> when the pipeline is composed without them.</summary>
    internal ChatPipelineHostInputs? Host { get; init; }
}
