using System.ComponentModel;
using System.Text.Json;
using DotCraft.GeneratedTools.Core;
using DotCraft.Sessions;
using Microsoft.Extensions.AI;

namespace DotCraft.Tools;

internal sealed class RemoteToolHostControlSource(IRemoteToolHostClient client)
    : AIFunctionToolSource, IThreadScopedToolSource, IThreadForkToolBindingSource
{
    public override string SourceId => "remote-tool-host-control";

    public override int Priority => 9;

    protected override string? GetNamespace(AIFunction function, ToolPlanningContext context) =>
        "RemoteToolHost";

    protected override string? GetNamespaceDescription(AIFunction function, ToolPlanningContext context) =>
        "Inspect and switch this thread's registered Remote Tool Host execution route.";

    protected override IEnumerable<AIFunction> CreateFunctions(ToolPlanningContext context)
    {
        if (!client.HasRegistrations)
            return [];

        var tools = new RemoteToolHostTools(client);
        return
        [
            GeneratedToolFunctions.RemoteToolHostTools_List(tools),
            GeneratedToolFunctions.RemoteToolHostTools_Connect(tools),
            GeneratedToolFunctions.RemoteToolHostTools_Disconnect(tools)
        ];
    }

    protected override string GetDescription(AIFunction function, ToolPlanningContext context)
    {
        var description = base.GetDescription(function, context);
        if (!string.Equals(function.Name, "Connect", StringComparison.Ordinal))
            return description;
        var summary = client.GetPlanningSummary();
        return string.IsNullOrWhiteSpace(summary) ? description : $"{description}\n\n{summary}";
    }

    public async ValueTask ReleaseThreadAsync(string threadId, CancellationToken cancellationToken = default) =>
        _ = await client.DisconnectAsync(threadId, cancellationToken).ConfigureAwait(false);

    public bool TryForkThreadBinding(string parentThreadId, string childThreadId) =>
        client.TryForkRoute(parentThreadId, childThreadId);
}

internal sealed class RemoteToolHostTools(IRemoteToolHostClient client)
{
    private static readonly JsonSerializerOptions JsonOptions = JsonSerializerOptions.Web;

    [GeneratedTool(Name = "List")]
    [Description("List registered Remote Tool Hosts, their online workspaces, and this thread's current route. Credentials and endpoints are never returned.")]
    public async Task<string> List(CancellationToken cancellationToken = default)
    {
        var threadId = CurrentThreadId();
        var result = await client.ListAsync(threadId, cancellationToken).ConfigureAwait(false);
        return JsonSerializer.Serialize(result, JsonOptions);
    }

    [GeneratedTool(Name = "Connect")]
    [Description("Connect this thread to a registered Remote Tool Host workspace. Subsequent RPC-eligible tools execute there until explicitly disconnected.")]
    public async Task<string> Connect(
        [Description("Registered opaque Remote Tool Host id.")] string hostId,
        [Description("Host-local workspace id returned by RemoteToolHost.List.")] string workspaceId,
        CancellationToken cancellationToken = default)
    {
        var result = await client.ConnectAsync(CurrentThreadId(), hostId, workspaceId, cancellationToken)
            .ConfigureAwait(false);
        return JsonSerializer.Serialize(result, JsonOptions);
    }

    [GeneratedTool(Name = "Disconnect")]
    [Description("Disconnect this thread from its Remote Tool Host workspace and return RPC-eligible tools to local execution.")]
    public async Task<string> Disconnect(CancellationToken cancellationToken = default)
    {
        var result = await client.DisconnectAsync(CurrentThreadId(), cancellationToken).ConfigureAwait(false);
        return JsonSerializer.Serialize(result, JsonOptions);
    }

    private static string CurrentThreadId() =>
        ToolHostExecutionScope.Current?.ThreadId
        ?? SubAgentSessionScope.Current?.ParentThread.Id
        ?? throw new InvalidOperationException("Remote Tool Host control is available only inside a Session Core turn.");
}
