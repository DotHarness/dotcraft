using DotCraft.Agents;
using DotCraft.Context;
using Microsoft.Extensions.AI;

namespace DotCraft.Sessions;

public sealed partial class SessionService
{
    private IContextPageManager AgentInstructionContextPages =>
        AgentFactory.RuntimeContext.ContextPageManager;

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> GetInstructionSourcesAsync(
        string threadId,
        CancellationToken ct = default)
    {
        var thread = await GetOrLoadThreadAsync(threadId, ct);
        var snapshot = ResolveAgentInstructions(thread);
        RecordAgentInstructionsSnapshot(thread.Id, snapshot);
        return snapshot.Sources;
    }

    private ContextPageSnapshot ResolveAgentInstructions(SessionThread thread) =>
        ResolveAgentInstructions(thread, ThreadWorkspaceResolver.Resolve(thread).Cwd);

    private ContextPageSnapshot ResolveAgentInstructions(
        SessionThread thread,
        string effectiveCwd)
    {
        var config = _appConfigMonitor?.Current ?? AgentFactory.RuntimeContext.Config;
        var globalConfigPath = string.IsNullOrWhiteSpace(config.GlobalConfigPath)
            ? string.Empty
            : Path.GetFullPath(config.GlobalConfigPath!);
        var variant = string.Join(
            "\0",
            Path.GetFullPath(effectiveCwd),
            globalConfigPath,
            config.ProjectDocMaxBytes.ToString(System.Globalization.CultureInfo.InvariantCulture));
        var shouldLoad = thread.Source.SubAgent is not { } subAgent
                         || string.Equals(
                subAgent.RuntimeType ?? string.Empty,
                             NativeSubAgentRuntime.RuntimeTypeName,
                             StringComparison.OrdinalIgnoreCase);

        return AgentInstructionContextPages.GetOrAdd(
            thread.Id,
            ContextPageKeys.AgentInstructions(variant),
            ContextPageLifecycle.StableUntilCompaction,
            () => shouldLoad
                ? new AgentInstructionsLoader(Logger).Load(effectiveCwd, config).ToContextPageDocument()
                : ContextPageDocument.FromContent(string.Empty));
    }

    private ContextPageSnapshot ReloadAgentInstructionsAfterCompaction(
        SessionThread thread,
        IList<ChatMessage> history) =>
        ReloadAgentInstructionsAfterCompaction(
            thread,
            history,
            ThreadWorkspaceResolver.Resolve(thread));

    private ContextPageSnapshot ReloadAgentInstructionsAfterCompaction(
        SessionThread thread,
        IList<ChatMessage> history,
        ThreadWorkspaceContext context)
    {
        AgentInstructionContextPages.ReleaseStablePages(thread.Id);
        var snapshot = ResolveAgentInstructions(thread, context.Cwd);
        AgentInstructionsHistory.InstallBaseline(history, snapshot.Content);
        RecordAgentInstructionsSnapshot(thread.Id, snapshot);
        return snapshot;
    }

    private void RecordAgentInstructionsSnapshot(string threadId, ContextPageSnapshot snapshot) =>
        TraceCollector?.RecordAgentInstructions(
            threadId,
            snapshot.Content,
            snapshot.Sources,
            snapshot.Fingerprint);
}
