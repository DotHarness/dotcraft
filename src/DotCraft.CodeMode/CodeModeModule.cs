using DotCraft.Configuration;
using DotCraft.Contributions;
using DotCraft.Modules;
using DotCraft.Processes;
using DotCraft.Scripting;
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

    public override void ConfigureServices(IServiceCollection services, ModuleContext context) =>
        services.AddDotCraftCodeMode(context.Paths.WorkspacePath, context.Config);
}

public static class CodeModeServiceCollectionExtensions
{
    /// <summary>
    /// Adds Code Mode for the workspace. Its scripts run in a child process started as
    /// <c>&lt;workerHostName&gt; script-worker code-mode</c>, a command that host serves through
    /// <see cref="CodeModeWorkerRunner.RunAsync"/>; <c>Tools.CodeMode.Mode</c> decides whether a snapshot uses it.
    /// </summary>
    public static IServiceCollection AddDotCraftCodeMode(
        this IServiceCollection services,
        string workspacePath,
        AppConfig config,
        string workerHostName = ScriptWorkerProcess.DefaultHostName,
        CodeModeLimits? limits = null)
    {
        services.TryAddSingleton<IManagedChildProcessFactory, ManagedChildProcessFactory>();
        services.TryAddSingleton<CodeModeStore>();
        services.TryAddSingleton(sp => new CodeModeWorkerHost(
            sp.GetRequiredService<IManagedChildProcessFactory>(),
            workspacePath,
            limits ?? new CodeModeLimits(),
            sp.GetService<ILogger<CodeModeWorkerHost>>(),
            workerHostName));
        services.AddSingleton<IToolSnapshotFinalizer>(sp =>
        {
            var monitor = sp.GetService<IAppConfigMonitor>();
            return new CodeModeToolFinalizer(
                () => monitor?.Current ?? config,
                sp.GetRequiredService<CodeModeWorkerHost>(),
                sp.GetRequiredService<CodeModeStore>(),
                sp.GetRequiredService<IToolDispatcher>(),
                sp.GetService<ILogger<CodeModeToolFinalizer>>());
        });
        services.AddSingleton<IThreadLifecycleObserver>(sp => new CodeModeThreadObserver(
            sp.GetRequiredService<CodeModeWorkerHost>(),
            sp.GetRequiredService<CodeModeStore>()));
        return services;
    }
}

internal sealed class CodeModeThreadObserver(CodeModeWorkerHost host, CodeModeStore store)
    : IThreadLifecycleObserver, ITurnLifecycleContributor
{
    public Task OnThreadDeletingAsync(SessionThread thread, CancellationToken cancellationToken = default)
    {
        host.OpenCells.TerminateThread(thread.Id);
        store.Clear(thread.Id);
        return Task.CompletedTask;
    }

    public Task OnTurnEndedAsync(TurnLifecycleContext context, CancellationToken cancellationToken = default)
    {
        host.OpenCells.TerminateTurn(context.ThreadId, context.TurnId);
        return Task.CompletedTask;
    }
}
