namespace DotCraft.Sessions;

/// <summary>
/// Thrown when a thread does not exist, as distinct from other missing keys that also raise <see cref="KeyNotFoundException"/>.
/// </summary>
public sealed class ThreadNotFoundException(string threadId, string? detail = null)
    : KeyNotFoundException($"Thread '{threadId}' not found.{(detail is null ? string.Empty : " " + detail)}")
{
    public string ThreadId { get; } = threadId;
}
