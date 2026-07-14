using DotCraft.Configuration;
using DotCraft.Modules;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;

namespace DotCraft.Tools;

/// <summary>Collects constructor-injected tool sources from enabled modules and DI.</summary>
public sealed class ToolSourceCollector(
    ModuleRegistry moduleRegistry,
    IServiceProvider services,
    AppConfig config)
{
    /// <summary>Returns sources in deterministic planning order.</summary>
    public IReadOnlyList<IToolSource> Collect()
    {
        var sources = new List<IToolSource>();
        sources.AddRange(services.GetServices<IToolSource>());
        foreach (var module in moduleRegistry.GetEnabledModules(config))
            sources.AddRange(module.GetToolSources(services));

        return sources
            .DistinctBy(source => (source.GetType(), source.SourceId))
            .OrderBy(source => source.Priority)
            .ThenBy(source => source.SourceId, StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>Scans core and enabled module assemblies for presentation metadata.</summary>
    public static void ScanToolIcons(ModuleRegistry registry, AppConfig config)
    {
        ToolRegistry.ScanAssembly(typeof(ToolSourceCollector).Assembly);
        foreach (var module in registry.GetEnabledModules(config))
            ToolRegistry.ScanAssembly(module.GetType().Assembly);
    }

    /// <summary>Scans explicit assemblies for presentation metadata.</summary>
    public static void ScanToolIcons(params Assembly[] assemblies) =>
        ToolRegistry.ScanAssemblies(assemblies);
}
