namespace DotCraft.Sessions;

public sealed class ThreadNotFoundException(string threadId, string? detail = null)
    : KeyNotFoundException($"Thread '{threadId}' not found.{(detail is null ? string.Empty : " " + detail)}")
{
    public string ThreadId { get; } = threadId;
}
