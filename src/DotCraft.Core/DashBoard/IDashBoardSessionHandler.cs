namespace DotCraft.DashBoard;

/// <summary>
/// Handles session/thread lifecycle operations invoked by the dashboard API.
/// Implementations bridge dashboard actions to the underlying persistence layer
/// (e.g. ThreadStore, wire protocol, or a remote session service).
/// </summary>
public interface IDashBoardSessionHandler
{
    /// <summary>
    /// Permanently removes the thread associated with the given session key.
    /// Implementations should tolerate missing threads (e.g. tracing-only sessions).
    /// </summary>
    Task DeleteThreadAsync(string sessionKey);

    /// <summary>
    /// Permanently removes all threads for the given set of session keys.
    /// Called when the dashboard "clear all" action is triggered.
    /// </summary>
    Task DeleteAllThreadsAsync(IEnumerable<string> sessionKeys);
}

/// <summary>
/// Convenience implementation that adapts a single <c>deleteOne</c> delegate into
/// <see cref="IDashBoardSessionHandler"/>. Batch deletion iterates the delegate
/// per key, swallowing <see cref="KeyNotFoundException"/> for threads that no longer exist.
/// </summary>
public sealed class DelegateDashBoardSessionHandler(Func<string, Task> deleteOne)
    : IDashBoardSessionHandler
{
    public Task DeleteThreadAsync(string sessionKey) => deleteOne(sessionKey);

    public async Task DeleteAllThreadsAsync(IEnumerable<string> sessionKeys)
    {
        foreach (var key in sessionKeys)
        {
            try
            {
                await deleteOne(key);
            }
            catch (KeyNotFoundException)
            {
                // Thread may not exist for tracing-only sessions — ignore.
            }
        }
    }
}
