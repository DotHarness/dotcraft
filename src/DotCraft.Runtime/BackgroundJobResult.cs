namespace DotCraft.Runtime;

/// <summary>Describes a completed background runtime operation.</summary>
public sealed record BackgroundJobResult(
    string Source,
    string? JobId,
    string? JobName,
    string? Result,
    string? Error,
    string? ThreadId = null,
    int? InputTokens = null,
    int? OutputTokens = null);
