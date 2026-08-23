using System.Text.Json;
using DotCraft.Contributions;

namespace DotCraft.Plugins;

/// <summary>Identifies one process-local plugin generation.</summary>
public sealed record PluginIdentity(
    string Id,
    string Version,
    string GenerationId);

/// <summary>Entry point implemented by every in-process DotCraft plugin.</summary>
/// <remarks>The implementing type is named by the manifest <c>dotnet.entryType</c> and must be public, concrete, non-generic, with a public parameterless constructor.</remarks>
public interface IDotCraftPlugin
{
    /// <summary>Activates the plugin and declares its generation-owned contributions.</summary>
    /// <param name="cancellationToken">Cancelled when the bounded activation budget expires.</param>
    ValueTask ActivateAsync(
        IPluginActivationContext context,
        CancellationToken cancellationToken);
}

/// <summary>Provides the stable Host contract available to one plugin generation.</summary>
public interface IPluginActivationContext
{
    /// <summary>Gets the identity of this activation generation.</summary>
    PluginIdentity Plugin { get; }

    /// <summary>Gets the immutable generation shadow-copy root.</summary>
    string ContentRoot { get; }

    /// <summary>Gets the mutable user-owned plugin data root.</summary>
    string DataRoot { get; }

    /// <summary>Gets the active workspace root.</summary>
    string WorkspaceRoot { get; }

    /// <summary>Gets this plugin generation's settings snapshot, or an empty object when none were configured at activation.</summary>
    JsonElement Settings { get; }

    /// <summary>Gets a filtered view of Host-owned application services. Resolved services must never be disposed by the plugin.</summary>
    IServiceProvider Services { get; }

    /// <summary>Gets the activation-only contribution registrar owned by this generation. Registrations are sealed when activation commits and mass-revoked at teardown.</summary>
    IContributionRegistrar Contributions { get; }

    /// <summary>Gets the activation-only service export registrar.</summary>
    IPluginServiceExportRegistrar Exports { get; }

    /// <summary>Gets the activation-only direct dependency resolver.</summary>
    IPluginDependencyResolver Dependencies { get; }

    /// <summary>Gets the generation lifetime registrar.</summary>
    IPluginLifetime Lifetime { get; }
}

/// <summary>Registers resources and background work owned by one generation.</summary>
public interface IPluginLifetime
{
    /// <summary>Gets the token cancelled when generation teardown starts.</summary>
    CancellationToken Stopping { get; }

    /// <summary>Registers a synchronous resource for reverse-order cleanup.</summary>
    void Own(IDisposable resource);

    /// <summary>Registers an asynchronous resource for reverse-order cleanup.</summary>
    void OwnAsync(IAsyncDisposable resource);

    /// <summary>Registers background work that starts only after activation commits, and must complete promptly once its token is cancelled.</summary>
    void Run(Func<CancellationToken, Task> operation);
}

/// <summary>Registers typed services exported by a provider plugin.</summary>
public interface IPluginServiceExportRegistrar
{
    /// <summary>Exports one implementation for a contract declared by an assembly listed in <c>dotnet.exportedApiAssemblies</c>.</summary>
    void Add<TContract>(TContract service) where TContract : class;
}

/// <summary>Resolves typed services from direct plugin dependencies.</summary>
public interface IPluginDependencyResolver
{
    /// <summary>Resolves one required service from a provider declared in the manifest <c>dependencies</c> block.</summary>
    TContract GetRequired<TContract>(string providerPluginId)
        where TContract : class;
}
