using DotCraft.Abstractions;
using DotCraft.Protocol;
using Microsoft.Extensions.AI;

namespace DotCraft.Plugins;

/// <summary>
/// Bridges thread-scoped plugin function providers into Session Core's runtime tool hook.
/// </summary>
public sealed class ThreadPluginFunctionToolProvider(
    IEnumerable<IThreadPluginFunctionProvider> providers,
    PluginDiagnosticsStore? diagnosticsStore = null)
    : IThreadRuntimeToolProvider, IReservedRuntimeToolNameConfigurator
{
    public int Priority => 100;

    private readonly IReadOnlyList<IThreadPluginFunctionProvider> _providers =
        providers.OrderBy(provider => provider.Priority).ToArray();

    public IReadOnlyList<AITool> CreateToolsForThread(
        SessionThread thread,
        IReadOnlySet<string> reservedToolNames)
    {
        var diagnostics = new List<PluginDiagnostic>();
        var registrations = new List<PluginFunctionRegistration>();
        foreach (var provider in _providers)
        {
            registrations.AddRange(provider.CreateFunctionsForThread(thread, reservedToolNames));
        }

        var resolved = PluginFunctionConflictResolver.ResolveRegistrations(
            registrations,
            diagnostics,
            reservedToolNames);
        diagnosticsStore ??= PluginDiagnosticsStore.Shared;
        diagnosticsStore.Append(diagnostics);
        PluginDiagnosticsLogger.Write(diagnostics);

        return resolved
            .Select(registration => (AITool)new PluginFunctionRuntimeFunction(registration))
            .ToArray();
    }

    public void ConfigureReservedToolNames(IEnumerable<string> toolNames)
    {
        foreach (var provider in _providers)
        {
            if (provider is IReservedRuntimeToolNameConfigurator configurator)
                configurator.ConfigureReservedToolNames(toolNames);
        }
    }
}

/// <summary>
/// Combines all thread-scoped runtime tool providers into the Session Core hook.
/// </summary>
public sealed class CompositeChannelRuntimeToolProvider(IEnumerable<IThreadRuntimeToolProvider> providers)
    : IChannelRuntimeToolProvider, IReservedRuntimeToolNameConfigurator
{
    private readonly IReadOnlyList<IThreadRuntimeToolProvider> _providers =
        providers.OrderBy(provider => provider.Priority).ToArray();

    public IReadOnlyList<AITool> CreateToolsForThread(
        SessionThread thread,
        IReadOnlySet<string> reservedToolNames)
    {
        var tools = new List<AITool>();
        var reserved = new HashSet<string>(reservedToolNames, StringComparer.Ordinal);

        foreach (var provider in _providers)
        {
            var provided = provider.CreateToolsForThread(thread, reserved);
            if (provided.Count == 0)
                continue;

            tools.AddRange(provided);
            foreach (var tool in provided)
                reserved.Add(tool.Name);
        }

        return tools;
    }

    public void ConfigureReservedToolNames(IEnumerable<string> toolNames)
    {
        foreach (var provider in _providers)
        {
            if (provider is IReservedRuntimeToolNameConfigurator configurator)
                configurator.ConfigureReservedToolNames(toolNames);
        }
    }
}
