using DotCraft.Protocol;

namespace DotCraft.Abstractions;

/// <summary>
/// Observes workspace thread runtime lifecycle signals emitted by the session service.
/// </summary>
public interface IThreadRuntimeSignalObserver
{
    /// <summary>
    /// Called when a thread runtime signal is broadcast.
    /// </summary>
    void OnThreadRuntimeSignal(string workspaceCraftPath, string threadId, SessionThreadRuntimeSignal signal);
}
