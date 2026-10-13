using DotCraft.Configuration;
using DotCraft.Contributions;
using DotCraft.Modules;
using Microsoft.Extensions.DependencyInjection;

namespace DotCraft.Tools;

/// <summary>One collected tool source together with the component that supplied it: <see cref="ContributionOrigin.Builtin"/> for container registrations, or a module origin for an <see cref="IToolSourceModule"/> facet.</summary>
public readonly record struct CollectedToolSource(IToolSource Source, ContributionOrigin Origin);

/// <summary>Collects constructor-injected tool sources from enabled modules and DI.</summary>
public sealed class ToolSourceCollector(
    ModuleRegistry moduleRegistry,
    IServiceProvider services,
    AppConfig config)
{
    /// <summary>Returns sources in deterministic planning order.</summary>
    public IReadOnlyList<IToolSource> Collect() =>
        [.. CollectWithOrigins().Select(collected => collected.Source)];

    /// <summary>Returns sources in the same order as <see cref="Collect"/>, each attributed to its supplier.</summary>
    public IReadOnlyList<CollectedToolSource> CollectWithOrigins()
    {
        var sources = new List<CollectedToolSource>();
        foreach (var source in services.GetServices<IToolSource>())
            sources.Add(new CollectedToolSource(source, ContributionOrigin.Builtin));
        foreach (var module in moduleRegistry.GetEnabledModules(config).OfType<IToolSourceModule>())
        {
            var origin = ContributionOrigin.Module(
                string.IsNullOrWhiteSpace(module.Name) ? module.GetType().Name : module.Name);
            foreach (var source in module.GetToolSources(services))
                sources.Add(new CollectedToolSource(source, origin));
        }

        return sources
            .DistinctBy(collected => (collected.Source.GetType(), collected.Source.SourceId))
            .OrderBy(collected => collected.Source.Priority)
            .ThenBy(collected => collected.Source.SourceId, StringComparer.Ordinal)
            .ToArray();
    }
}
