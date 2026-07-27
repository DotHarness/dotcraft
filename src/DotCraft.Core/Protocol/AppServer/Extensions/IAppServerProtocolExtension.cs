namespace DotCraft.Protocol.AppServer;

/// <summary>
/// Handles one or more AppServer request methods contributed by a module.
/// </summary>
public interface IAppServerMethodHandler
{
    /// <summary>
    /// Method names handled by this extension.
    /// </summary>
    IReadOnlyCollection<string> Methods { get; }

    /// <summary>
    /// Handles a request routed to this extension.
    /// </summary>
    Task<object?> HandleAsync(AppServerIncomingMessage msg, AppServerExtensionContext context);
}

/// <summary>
/// Contributes server capabilities during the AppServer initialize handshake.
/// </summary>
public interface IAppServerCapabilityContributor
{
    /// <summary>
    /// Applies capability changes to the initialize result.
    /// </summary>
    void ContributeCapabilities(AppServerCapabilityBuilder builder);
}

/// <summary>
/// Unified AppServer protocol extension implemented by modules.
/// </summary>
public interface IAppServerProtocolExtension : IAppServerMethodHandler, IAppServerCapabilityContributor
{
}
