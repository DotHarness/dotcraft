using DotCraft.Protocol;

namespace DotCraft.AppServer;

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

/// <summary>
/// Bundled extension whose methods are registered exclusively through executable contract descriptors.
/// </summary>
public interface IAppServerContractExtension : IAppServerProtocolExtension
{
    /// <summary>Stable request descriptors owned by the bundled extension.</summary>
    IReadOnlyCollection<IRpcMethodDescriptor> ContractMethods { get; }

    /// <summary>Handles an already-deserialized Contracts params object.</summary>
    Task<object?> HandleContractAsync(
        IRpcMethodDescriptor descriptor,
        object parameters,
        AppServerIncomingMessage message,
        AppServerExtensionContext context);

    Task<object?> IAppServerMethodHandler.HandleAsync(
        AppServerIncomingMessage message,
        AppServerExtensionContext context) =>
        throw new InvalidOperationException(
            $"Contract extension '{GetType().FullName}' must be invoked through its typed descriptor route.");
}
