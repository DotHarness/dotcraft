using DotCraft.Configuration;
using DotCraft.Modules;
using DotCraft.Processes;
using DotCraft.Sessions;
using DotCraft.Tools;
using DotCraft.Commands.Core;
using DotCraft.Context;
using DotCraft.Hosting;
using DotCraft.Plugins;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DotCraft.DynamicWorkflows;

[DotCraftModule("dynamic-workflows", Priority = 58, Description = "Process-isolated dynamic workflow runtime")]
public sealed partial class DynamicWorkflowsModule : ModuleBase
{
    public override bool IsEnabled(AppConfig config) => true;

    public override void ConfigureServices(IServiceCollection services, ModuleContext context)
    {
        services.TryAddSingleton<IManagedChildProcessFactory, ManagedChildProcessFactory>();
        services.TryAddSingleton<DynamicWorkflowParser>();
        services.TryAddSingleton<PluginDiscoveryService>();
        services.TryAddSingleton(sp => new DynamicWorkflowCatalog(
            context.Paths.WorkspacePath,
            context.Paths.CraftPath,
            context.Config,
            sp.GetRequiredService<DynamicWorkflowParser>(),
            sp.GetRequiredService<PluginDiscoveryService>()));
        services.AddSingleton<IPluginWorkflowSummaryProvider>(sp => sp.GetRequiredService<DynamicWorkflowCatalog>());
        services.TryAddSingleton(_ => new DynamicWorkflowStore(context.Paths.CraftPath));
        services.TryAddSingleton<StructuredWorkflowResultRegistry>();
        services.TryAddSingleton<StructuredWorkflowResultToolSource>();
        services.TryAddSingleton<DynamicWorkflowToolSource>();
        services.TryAddSingleton<DynamicWorkflowProjectionService>();
        services.TryAddSingleton(sp => new DynamicWorkflowService(
            context.Paths.WorkspacePath,
            sp.GetRequiredService<DynamicWorkflowStore>(),
            sp.GetRequiredService<DynamicWorkflowParser>(),
            sp.GetRequiredService<StructuredWorkflowResultRegistry>(),
            sp.GetRequiredService<IManagedChildProcessFactory>(),
            context.Config.SubAgent.Roles.Select(role => role.Clone()).ToArray(),
            sp.GetService<Microsoft.Extensions.Logging.ILogger<DynamicWorkflowService>>()));
        services.AddSingleton<IDynamicWorkflowService>(sp => sp.GetRequiredService<DynamicWorkflowService>());
        services.AddSingleton<ISessionServiceConsumer>(sp => sp.GetRequiredService<DynamicWorkflowService>());
        services.AddSingleton<ISessionServiceConsumer>(sp => sp.GetRequiredService<DynamicWorkflowProjectionService>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<DotCraft.AppServer.IAppServerProtocolExtension, DynamicWorkflowProtocolExtension>());
        services.AddSingleton<IThreadLifecycleObserver>(sp => sp.GetRequiredService<DynamicWorkflowService>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ISubAgentGuidanceProvider, DynamicWorkflowSubAgentGuidance>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IPromptCommandProvider, DynamicWorkflowCommandProvider>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IRuntimeContextContributor, DynamicWorkflowRuntimeContextContributor>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IRuntimeCapabilityProvider, DynamicWorkflowRuntimeCapability>());
    }

    public override IEnumerable<IToolSource> GetToolSources(IServiceProvider services) =>
        [services.GetRequiredService<StructuredWorkflowResultToolSource>(), services.GetRequiredService<DynamicWorkflowToolSource>()];
}

internal sealed class DynamicWorkflowSubAgentGuidance : ISubAgentGuidanceProvider
{
    private const string Guidance = """
        ## Dynamic Workflow Task

        Complete only the current workflow operation. When the task requests a structured result,
        submit it through SubmitWorkflowResult; a valid submission completes the Turn immediately.
        Do not attempt to create or run another workflow from this child.
        """;

    public string? GetGuidance(SessionThread thread) =>
        string.Equals(thread.Source.SubAgent?.Purpose, "dynamicWorkflow", StringComparison.Ordinal)
            ? Guidance
            : null;
}
