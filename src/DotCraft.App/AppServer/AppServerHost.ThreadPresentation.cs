using System.Text.Json;
using DotCraft.Memory;
using DotCraft.Sessions;
using DotCraft.Sessions.Wire;
using Contract = DotCraft.Protocol.AppServer;

namespace DotCraft.AppServer;

public sealed partial class AppServerHost
{
    private void BroadcastThreadRenamed(SessionThread thread)
    {
        if (string.IsNullOrEmpty(thread.DisplayName)) return;
        _hiddenThreads.Observe(thread);
        var parameters = new Contract.ThreadRenamedNotification { ThreadId = thread.Id, DisplayName = thread.DisplayName };
        foreach (var (transport, connection) in _activeTransports)
        {
            if (!connection.ShouldSendNotification(Contract.AppServerRpc.ThreadRenamed.Name)
                || !_hiddenThreads.Accepts(thread.Id, connection)) continue;
            _ = SendThreadPresentationAsync(transport, connection, thread.Id,
                _ => transport.NotifyAsync(Contract.AppServerRpc.ThreadRenamed, parameters, CancellationToken.None));
        }
    }

    private void BroadcastThreadUpdated(SessionThread thread)
    {
        _hiddenThreads.Observe(thread);
        BroadcastThreadSettingsUpdated(thread);
        SessionWireThread wire;
        lock (thread.Metadata)
            wire = thread.ToWire(includeTurns: false) with
            {
                Configuration = thread.Configuration is { } configuration
                    ? JsonSerializer.SerializeToElement(configuration, SessionWireJsonOptions.Default)
                        .Deserialize<ThreadConfiguration>(SessionWireJsonOptions.Default)
                    : null
            };
        var skipTransport = string.Equals(AppServerRequestContext.CurrentMethod,
            Contract.AppServerRpc.ThreadWorktreeHandoff.Name, StringComparison.Ordinal)
            ? AppServerRequestContext.CurrentTransport : null;
        foreach (var (transport, connection) in _activeTransports)
        {
            if (!connection.ShouldSendNotification(Contract.AppServerRpc.ThreadUpdated.Name)
                || !_hiddenThreads.Accepts(thread.Id, connection)
                || ReferenceEquals(transport, skipTransport)) continue;
            _ = SendThreadPresentationAsync(transport, connection, thread.Id, snapshot =>
            {
                var current = snapshot is null ? wire : wire with
                {
                    Configuration = snapshot.Configuration,
                    Runtime = snapshot.Runtime.ToWireRuntimeState(),
                    ContextUsage = snapshot.ContextUsage ?? wire.ContextUsage,
                    QueuedInputs = snapshot.QueuedInputs.ToList()
                };
                return transport.NotifyAsync(Contract.AppServerRpc.ThreadUpdated,
                    new Contract.ThreadNotification { Thread = AppServerContractMapper.ToContract(current) }, CancellationToken.None);
            });
        }
    }

    private void BroadcastThreadSettingsUpdated(SessionThread thread)
    {
        var mode = thread.Configuration?.Mode ?? "agent";
        foreach (var (transport, connection) in _activeTransports)
        {
            if (!connection.ShouldSendNotification(Contract.AppServerRpc.ThreadSettingsUpdated.Name)
                || !_hiddenThreads.Accepts(thread.Id, connection)) continue;
            _ = SendThreadPresentationAsync(transport, connection, thread.Id, snapshot =>
                transport.NotifyAsync(Contract.AppServerRpc.ThreadSettingsUpdated,
                    new Contract.ThreadSettingsUpdatedNotification
                    {
                        ThreadId = thread.Id,
                        Mode = snapshot?.Configuration?.Mode ?? mode
                    }, CancellationToken.None));
        }
    }

    private void BroadcastPlanUpdated(string threadId, StructuredPlan plan)
    {
        var parameters = BuildPlanUpdatedParameters(threadId, plan);
        foreach (var (transport, connection) in _activeTransports)
        {
            if (!connection.ShouldSendNotification(Contract.AppServerRpc.PlanUpdated.Name)) continue;
            _ = SendThreadPresentationAsync(transport, connection, threadId, snapshot =>
                transport.NotifyAsync(Contract.AppServerRpc.PlanUpdated,
                    snapshot?.Plan is { } current ? BuildPlanUpdatedParameters(threadId, current) : parameters,
                    CancellationToken.None));
        }
    }

    private async Task SendThreadPresentationAsync(
        IAppServerTransport transport,
        AppServerConnection connection,
        string threadId,
        Func<ThreadObservationSnapshot?, Task> send)
    {
        try
        {
            await connection.RunThreadNotificationAsync(threadId, snapshot =>
            {
                snapshot?.EnsureCurrent();
                return send(snapshot);
            });
        }
        catch (OperationCanceledException) { }
        catch { _activeTransports.TryRemove(transport, out _); }
    }

    internal static object BuildPlanUpdatedNotification(string threadId, StructuredPlan plan) => new
    {
        jsonrpc = "2.0", method = Contract.AppServerRpc.PlanUpdated.Name,
        @params = BuildPlanUpdatedParameters(threadId, plan)
    };

    private static Contract.PlanUpdatedNotification BuildPlanUpdatedParameters(string threadId, StructuredPlan plan) => new()
    {
        ThreadId = threadId, Title = plan.Title, Overview = plan.Overview, Content = plan.Content,
        Todos = plan.Todos.Select(todo => new Contract.PlanTodo
        {
            Id = todo.Id, Content = todo.Content, Priority = todo.Priority, Status = todo.Status
        }).ToArray()
    };
}
