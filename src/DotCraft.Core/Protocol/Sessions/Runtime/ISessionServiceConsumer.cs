namespace DotCraft.Protocol;

/// <summary>
/// Implemented by services that need the host's shared <see cref="ISessionService"/> after it is
/// constructed. This keeps late-bound runtimes and channel services out of the Session service's
/// construction dependency graph.
/// </summary>
public interface ISessionServiceConsumer
{
    /// <summary>
    /// Supplies the shared session service used by the host for agent threads. Implementations must
    /// tolerate rebinding when a host runtime is recreated.
    /// </summary>
    void SetSessionService(ISessionService service);
}
