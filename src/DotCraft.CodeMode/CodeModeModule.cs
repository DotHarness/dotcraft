using DotCraft.Configuration;
using DotCraft.Modules;
using DotCraft.Processes;
using DotCraft.Sessions;
using DotCraft.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace DotCraft.CodeMode;

[DotCraftModule("code-mode", Priority = 59, Description = "Scripted tool calls through the CodeMode tool")]
public sealed partial class CodeModeModule : ModuleBase
{
    public override bool IsEnabled(AppConfig config) => true;

    public override void ConfigureServices(IServiceCollection services, ModuleContext context)
    {
        services.TryAddSingleton<IManagedChildProcessFactory, ManagedChildProcessFactory>();
        services.TryAddSingleton<CodeModeStore>();
        services.TryAddSingleton(sp => new CodeModeWorkerHost(
            sp.GetRequiredService<IManagedChildProcessFactory>(),
            context.Paths.WorkspacePath,
            new CodeModeLimits(),
            sp.GetService<ILogger<CodeModeWorkerHost>>()));
        services.AddSingleton<IToolSnapshotFinalizer>(sp =>
        {
            var monitor = sp.GetService<IAppConfigMonitor>();
            return new CodeModeToolFinalizer(
                () => monitor?.Current ?? context.Config,
                sp.GetRequiredService<CodeModeWorkerHost>(),
                sp.GetRequiredService<CodeModeStore>(),
                sp.GetRequiredService<IToolDispatcher>(),
                sp.GetService<ILogger<CodeModeToolFinalizer>>());
        });
        services.AddSingleton<IThreadLifecycleObserver>(sp => new CodeModeThreadObserver(
            sp.GetRequiredService<CodeModeWorkerHost>(),
            sp.GetRequiredService<CodeModeStore>()));
    }
}

internal sealed class CodeModeThreadObserver(CodeModeWorkerHost host, CodeModeStore store) : IThreadLifecycleObserver
{
    public Task OnThreadDeletingAsync(SessionThread thread, CancellationToken cancellationToken = default)
    {
        host.CancelThread(thread.Id);
        store.Clear(thread.Id);
        return Task.CompletedTask;
    }
}
