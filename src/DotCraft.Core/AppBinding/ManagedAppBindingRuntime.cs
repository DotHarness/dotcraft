using System.Text.Json.Nodes;
using DotCraft.Protocol;
using DotCraft.Protocol.AppServer;

namespace DotCraft.AppBinding;

public static class AppBindingCatalogSurfaces
{
    public const string PluginDetail = "pluginDetail";
    public const string Welcome = "welcome";
    public const string ThreadBinding = "threadBinding";
    public const string SdkDefault = "sdk/default";

    public static readonly IReadOnlySet<string> None = new HashSet<string>(StringComparer.Ordinal);

    public static string Normalize(string? surface) =>
        string.IsNullOrWhiteSpace(surface) ? SdkDefault : surface.Trim();
}

public static class ManagedAppBindingToolSurfaces
{
    public const string Default = "default";
    public const string ThreadBinding = AppBindingCatalogSurfaces.ThreadBinding;
}

/// <summary>
/// In-process App Binding runtime contributed by a first-party DotCraft module.
/// Managed runtimes use the same descriptor, binding, tool, context, and audit
/// contracts as external apps, but dispatch tool calls without an external transport.
/// </summary>
public interface IManagedAppBindingRuntime
{
    /// <summary>
    /// App descriptor exposed through the App Binding catalog.
    /// </summary>
    AppDescriptor Descriptor { get; }

    /// <summary>
    /// Owning plugin id that gates user-visible catalog discovery for this runtime.
    /// A null value keeps the runtime internal-only.
    /// </summary>
    string? OwningPluginId => null;

    /// <summary>
    /// App list surfaces where this managed runtime may appear after its owning
    /// plugin is installed and enabled.
    /// </summary>
    IReadOnlySet<string> CatalogSurfaces => AppBindingCatalogSurfaces.None;

    /// <summary>
    /// Whether Desktop should render native app connect/handoff flows for this runtime.
    /// </summary>
    bool RequiresExternalConnection => false;

    /// <summary>
    /// Runtime Dynamic Tool specs exposed to threads bound to this app.
    /// </summary>
    IReadOnlyList<DynamicToolSpec> ToolSpecs { get; }

    /// <summary>
    /// Descriptor projected to a specific catalog surface.
    /// </summary>
    AppDescriptor GetCatalogDescriptor(string surface) => Descriptor;

    /// <summary>
    /// Tool specs attached to a managed binding for a specific surface.
    /// </summary>
    IReadOnlyList<DynamicToolSpec> GetToolSpecsForSurface(string surface) => ToolSpecs;

    /// <summary>
    /// Allows this first-party managed runtime to expose mutating tools directly
    /// when it attaches tools itself. External app attachments are still forced
    /// through deferred exposure for mutating tools.
    /// </summary>
    bool AllowDirectMutatingToolExposure { get; }

    /// <summary>
    /// Invokes one app-bound tool for an active managed binding.
    /// </summary>
    ValueTask<DynamicToolCallResult> InvokeToolAsync(
        ManagedAppBindingToolCallContext context,
        JsonObject arguments,
        CancellationToken cancellationToken);
}

/// <summary>
/// Execution context for an in-process App Binding tool call.
/// </summary>
public sealed record ManagedAppBindingToolCallContext(
    string WorkspaceCraftPath,
    string WorkspacePath,
    string BindingId,
    string ThreadId,
    string TurnId,
    string CallId,
    string AppId,
    string GrantId,
    string ToolName)
{
    public AppBindingService? AppBindingService { get; init; }

    public ISessionService? SessionService { get; init; }
}
