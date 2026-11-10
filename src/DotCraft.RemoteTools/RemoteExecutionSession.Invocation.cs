using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Tools;
using ModelContextProtocol.Protocol;

namespace DotCraft.RemoteTools;

public sealed partial class RemoteExecutionSession
{
    public async ValueTask<ToolExecutionResult> InvokeAsync(
        RemoteToolRoute route,
        ToolDefinition definition,
        string contractHash,
        ToolInvocationContext context,
        JsonObject arguments,
        CancellationToken cancellationToken = default)
    {
        using var operation = _operations.TryEnter(cancellationToken);
        if (operation is null || !IsAvailable || route != Route)
            return Failure(RemoteToolErrorCodes.LeaseLost, "The remote execution session was lost.");
        cancellationToken = operation.Token;

        var invocationId = "rti_" + Guid.NewGuid().ToString("N");
        var releases = ThreadReleases(context.ThreadId);
        string? preparedBinding = null;
        long? snapshotRevision = null;
        if (definition.Id.Kind == ToolSourceKind.PluginNative)
        {
            ToolRegistration registration;
            lock (_stateGate)
            {
                if (_preparationFailures.TryGetValue(context.ThreadId, out var failure)
                    && failure.Revision == context.SnapshotRevision)
                    return Failure(failure.Code, failure.Message);
                if (!_preparedSnapshots.TryGetValue(context.ThreadId, out var prepared)
                    || prepared.Snapshot.Revision != context.SnapshotRevision
                    || !prepared.Snapshot.Registrations.Any(item => item.Binding.Id == context.RuntimeBindingId)
                    || !prepared.Bindings.TryGetValue(definition.Id.ToString(), out var binding)
                    || binding.ContractHash != contractHash)
                    return Failure(RemoteToolErrorCodes.RemoteToolUnavailable, "This plugin snapshot has not been prepared on the remote Host.");
                preparedBinding = binding.BindingId;
                snapshotRevision = prepared.Snapshot.Revision;
                registration = prepared.Snapshot.Registrations.Single(item => item.Binding.Id == context.RuntimeBindingId);
            }
            var authority = await registration.Binding.Lease.CheckAsync(context, cancellationToken).ConfigureAwait(false);
            if (!authority.IsAvailable)
                return Failure(RemoteToolErrorCodes.RemoteToolUnavailable, authority.Error?.Message ?? "The plugin source is no longer active.");
        }
        var meta = new RemoteInvocationMeta(
            route.LeaseId,
            route.WorkspaceId,
            invocationId,
            definition.Id.ToString(),
            contractHash,
            context.ThreadId,
            context.TurnId,
            ResolveRemoteResultLimit(definition),
            Math.Clamp(_spillPreviewLines, 1, 500), preparedBinding, snapshotRevision,
            context.Origin?.Kind == ToolInvocationOrigin.CodeModeKind ? ToolInvocationOrigin.CodeModeKind : null);
        return await InvokeRemoteAsync(route, definition.Name.ToString(), meta, context.CallId, arguments,
            releases, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<ToolExecutionResult> InvokeRemoteAsync(
        RemoteToolRoute route, string name, RemoteInvocationMeta meta, string callId,
        JsonObject arguments, int releases, CancellationToken cancellationToken)
    {
        var request = new CallToolRequestParams
        {
            Name = name,
            Arguments = arguments.ToDictionary(
                pair => pair.Key,
                pair => pair.Value is null
                    ? JsonSerializer.SerializeToElement<object?>(null)
                    : JsonSerializer.SerializeToElement(pair.Value, RemoteToolHostProtocol.JsonOptions),
                StringComparer.Ordinal),
            Meta = new JsonObject
            {
                ["dotcraft"] = JsonSerializer.SerializeToNode(meta, RemoteToolHostProtocol.JsonOptions)
            }
        };

        var started = DateTimeOffset.UtcNow;
        CallToolResult result;
        try
        {
            result = await CallToolAsync(
                _lease.Session.Client,
                request,
                meta.InvocationId,
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested && !_lease.Lost)
        {
            throw;
        }
        catch (Exception ex)
        {
            OnLeaseLost();
            return Failure(
                RemoteToolErrorCodes.RemoteOutcomeUnknown,
                $"The remote transport ended before the execution outcome was known. Invocation: {meta.InvocationId}. {ex.Message}",
                meta.InvocationId);
        }

        var dotcraft = result.Meta?["dotcraft"] as JsonObject;
        ReportStartedTerminals(dotcraft?["terminals"], meta.ThreadId, meta.TurnId, callId, releases);
        var code = dotcraft?["code"]?.GetValue<string>();
        RemoteToolArtifactMeta? artifact;
        try
        {
            artifact = ParseRemoteArtifact(dotcraft?["remoteArtifact"]);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            return Failure(
                RemoteToolErrorCodes.ProtocolMismatch,
                "Remote Tool Host returned invalid artifact metadata.",
                meta.InvocationId);
        }
        var content = NormalizeText(result);
        var remoteError = result.IsError == true
            ? Failure(code ?? ToolErrorCodes.ExecutionFailed,
                dotcraft?["diagnostic"]?.GetValue<string>() ?? content, meta.InvocationId).Error
            : null;

        var latency = (long)(DateTimeOffset.UtcNow - started).TotalMilliseconds;
        var provenance = new RemoteToolInvocationProvenance(
            "remote",
            route.HostId,
            route.WorkspaceId,
            route.HostInstanceId,
            meta.InvocationId,
            latency,
            artifact?.Path,
            artifact?.CharacterCount);
        return new ToolExecutionResult(
            result.IsError != true,
            content,
            result.StructuredContent,
            meta: provenance.ToJson(),
            rawSourceResult: JsonSerializer.SerializeToElement(result, RemoteToolHostProtocol.JsonOptions),
            error: remoteError,
            contentItems: NormalizeContentItems(result));
    }
}
