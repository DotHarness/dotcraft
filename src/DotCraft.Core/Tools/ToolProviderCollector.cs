using System.Reflection;
using DotCraft.Abstractions;
using DotCraft.Configuration;
using DotCraft.Cron;
using DotCraft.Mcp;
using DotCraft.Modules;
using DotCraft.Tools.Sandbox;

namespace DotCraft.Tools;

/// <summary>
/// Collects tool providers from modules and system sources.
/// </summary>
public static class ToolProviderCollector
{
    /// <summary>
    /// Collects tool providers from enabled modules and system providers.
    /// </summary>
    /// <param name="registry">The module registry.</param>
    /// <param name="config">The application configuration.</param>
    /// <param name="includeSystemProviders">Whether to include system providers (Cron, Mcp).</param>
    /// <returns>A list of tool providers.</returns>
    public static List<IAgentToolProvider> Collect(
        ModuleRegistry registry,
        AppConfig config,
        bool includeSystemProviders = true)
    {
        var providers = new List<IAgentToolProvider>();

        // Collect providers from all enabled modules
        foreach (var module in registry.GetEnabledModules(config))
        {
            providers.AddRange(module.GetToolProviders());
        }

        // Add system providers (these don't belong to any specific module)
        if (includeSystemProviders)
        {
            providers.Add(new CoreToolProvider());
            providers.Add(new GoalToolProvider());
            providers.Add(new AgentProfileBuilderToolProvider());
            providers.Add(new NodeReplPluginFunctionProvider());
            providers.Add(new CronToolProvider());

            providers.Add(new McpToolProvider());

            // When sandbox mode is enabled, SandboxToolProvider replaces
            // CoreToolProvider's shell/file tools with sandboxed equivalents.
            // CoreToolProvider.CreateTools returns [] when sandbox is enabled.
            if (config.Tools.Sandbox.Enabled)
            {
                providers.Add(new SandboxToolProvider());
            }
        }

        return providers;
    }

    /// <summary>
    /// Scans the DotCraft.Core assembly and all enabled module assemblies for tool metadata.
    /// Should be called once at application startup.
    /// </summary>
    public static void ScanToolIcons(ModuleRegistry registry, AppConfig config)
    {
        ToolRegistry.ScanAssembly(typeof(ToolProviderCollector).Assembly);

        foreach (var module in registry.GetEnabledModules(config))
            ToolRegistry.ScanAssembly(module.GetType().Assembly);
    }

    /// <summary>
    /// Scans assemblies for tool metadata and registers them.
    /// Should be called once at application startup.
    /// </summary>
    public static void ScanToolIcons(params Assembly[] assemblies)
    {
        ToolRegistry.ScanAssemblies(assemblies);
    }
}
