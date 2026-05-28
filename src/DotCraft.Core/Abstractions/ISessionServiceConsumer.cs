using DotCraft.Protocol;

namespace DotCraft.Abstractions;

/// <summary>
/// Implemented by channel services that need the host's shared <see cref="ISessionService"/>
/// after it is constructed (e.g. GatewayHost builds session service after channel instances exist).
/// </summary>
public interface ISessionServiceConsumer
{
    /// <summary>
    /// Supplies the shared session service used by the host for agent threads.
    /// </summary>
    void SetSessionService(ISessionService service);
}
