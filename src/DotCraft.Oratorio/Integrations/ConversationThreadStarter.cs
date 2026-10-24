using Microsoft.Extensions.Options;
using DotCraft.Oratorio.Data;

namespace DotCraft.Oratorio.Integrations;

public sealed class ConversationThreadStarter(IWorktreeManager worktreeManager, IOptionsMonitor<DotCraftOptions> options)
{
    private const string AgentInstructions = "You are connected through Oratorio. Follow the prompt exactly and use Oratorio dynamic tools when instructed.";

    public bool UsesManagedWorktrees => options.CurrentValue.ManagedWorktreesEnabled;

    public static WorktreePrepareRequest CreateWorktreeRequest(
        string ownerId,
        OratorioItem item,
        string? headSha,
        string baseWorkspacePath,
        string? stackOntoBranch = null,
        string? stackOntoSha = null) =>
        new(
            ownerId,
            item.ItemId,
            item.Source,
            item.ExternalId,
            item.Repository,
            item.Branch,
            headSha,
            baseWorkspacePath,
            stackOntoBranch,
            stackOntoSha);

    public Task<WorktreePrepareResult> PrepareWorktreeAsync(WorktreePrepareRequest request, CancellationToken ct) =>
        worktreeManager.PrepareAsync(request, ct);

    public Task<string> StartThreadAsync(
        IDotCraftAppServerClient client,
        string displayName,
        string baseWorkspacePath,
        string executionWorkspacePath,
        OratorioDynamicToolSet tools,
        IReadOnlyDictionary<string, AppServerRuntimeAdditionalContextEntry> runtimeAdditionalContext,
        CancellationToken ct) =>
        client.StartThreadAsync(new AppServerThreadStartRequest(
            DisplayName: displayName,
            BaseWorkspacePath: baseWorkspacePath,
            ExecutionWorkspacePath: executionWorkspacePath,
            ApprovalPolicy: DotCraftApprovalPolicy.Normalize(options.CurrentValue.ApprovalPolicy),
            AgentInstructions: AgentInstructions,
            DynamicTools: tools.Declarations,
            RuntimeAdditionalContext: runtimeAdditionalContext), ct);
}
