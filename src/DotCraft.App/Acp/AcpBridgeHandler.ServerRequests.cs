using System.Text.Json;
using DotCraft.Sessions;
using Contract = DotCraft.Protocol.AppServer;

namespace DotCraft.Acp;

public sealed partial class AcpBridgeHandler
{
    private async Task<object?> OnWireServerRequestAsync(JsonDocument doc)
    {
        var root = doc.RootElement;
        if (!root.TryGetProperty("method", out var methodElement)
            || !root.TryGetProperty("params", out var parameters))
            throw new InvalidOperationException("Invalid AppServer request.");

        var method = methodElement.GetString() ?? "";
        var sessionId = TryGetStringProperty(parameters, "threadId");
        if (string.IsNullOrEmpty(sessionId) || !_knownSessions.TryGetValue(sessionId, out var session))
            throw new InvalidOperationException("AppServer request targeted an unknown ACP session.");

        if (method != Contract.AppServerMethodNames.ApprovalRequest
            && method != Contract.AppServerMethodNames.DynamicToolCall
            && !method.StartsWith("ext/acp/", StringComparison.Ordinal))
            throw new InvalidOperationException($"Unsupported AppServer request: {method}");

        return await HandleWireServerRequestAsync(doc, sessionId, session.Lifetime).ConfigureAwait(false);
    }

    private async Task<object?> HandleWireServerRequestAsync(JsonDocument doc, string sessionId, CancellationToken ct)
    {
        var root = doc.RootElement;
        if (!root.TryGetProperty("method", out var mEl))
            return new { decision = "decline" };
        var method = mEl.GetString() ?? "";
        if (!root.TryGetProperty("params", out var wireParams))
            return new { decision = "decline" };

        if (method == DotCraft.Protocol.AppServer.AppServerMethodNames.ApprovalRequest)
        {
            var approvalType = wireParams.TryGetProperty("approvalType", out var at) ? at.GetString() : null;
            var operation = wireParams.TryGetProperty("operation", out var op) ? op.GetString() ?? "" : "";
            var target = wireParams.TryGetProperty("target", out var tg) ? tg.GetString() ?? "" : "";

            var toolKind = approvalType == "shell"
                ? AcpToolKind.Execute
                : operation.ToLowerInvariant() switch
                {
                    "write" or "edit" => AcpToolKind.Edit,
                    "delete" => AcpToolKind.Delete,
                    _ => AcpToolKind.Read
                };
            var reqId = wireParams.TryGetProperty("requestId", out var rid) ? rid.GetString() ?? "" : "";
            var shellReasons = wireParams.TryGetProperty("shell", out var shell)
                && shell.ValueKind == JsonValueKind.Object
                && shell.TryGetProperty("reasons", out var reasons)
                && reasons.ValueKind == JsonValueKind.Array
                    ? string.Join(" ", reasons.EnumerateArray().Select(reason => reason.GetString()).Where(reason => !string.IsNullOrWhiteSpace(reason)))
                    : string.Empty;
            var toolCall = new AcpToolCallInfo
            {
                ToolCallId = reqId,
                Title = approvalType == "shell"
                    ? $"Shell: {(operation.Length > 80 ? operation[..80] + "..." : operation)}"
                      + (shellReasons.Length > 0 ? $" ({shellReasons})" : string.Empty)
                    : $"File {operation}: {target}",
                Kind = toolKind,
                Status = AcpToolStatus.Pending
            };
            var permParams = new RequestPermissionParams
            {
                SessionId = sessionId,
                ToolCall = toolCall,
                Options =
                [
                    new PermissionOption { OptionId = "allow-once", Name = "Allow once", Kind = AcpPermissionKind.AllowOnce },
                    new PermissionOption { OptionId = "allow-always", Name = "Allow always", Kind = AcpPermissionKind.AllowAlways },
                    new PermissionOption { OptionId = "reject-once", Name = "Reject", Kind = AcpPermissionKind.RejectOnce },
                ]
            };

            try
            {
                var resultElement = await acpTransport.SendClientRequestAsync(
                    AcpMethods.RequestPermission, permParams,
                    timeout: _permissionRequestTimeout, ct: ct);
                var result = resultElement.Deserialize<RequestPermissionResult>(JsonOptions);
                var decision = result?.Outcome?.OptionId switch
                {
                    "allow-always" => "acceptAlways",
                    "allow-once" => "accept",
                    _ when result?.Outcome?.Outcome == "cancelled" => "cancel",
                    _ => "decline"
                };
                return new { decision };
            }
            catch
            {
                return new { decision = "decline" };
            }
        }

        if (method == DotCraft.Protocol.AppServer.AppServerMethodNames.DynamicToolCall)
            return await HandleRuntimeToolCallAsync(wireParams, ct);

        if (method.StartsWith("ext/acp/", StringComparison.Ordinal))
        {
            var acpMethod = method["ext/acp/".Length..];
            if (string.IsNullOrEmpty(acpMethod))
                return new { error = "bad ext method" };
            var ideMethod = MapExtPathToAcpMethod(acpMethod);
            try
            {
                var paramObj = ParamsForIdeExtForward(wireParams);
                var el = await acpTransport.SendClientRequestAsync(ideMethod, paramObj, ct,
                    timeout: _extForwardTimeout).ConfigureAwait(false);
                return JsonSerializer.Deserialize<object>(el.GetRawText(), JsonOptions) ?? new { };
            }
            catch
            {
                return new { error = "ext forward failed" };
            }
        }

        return new { decision = "decline" };
    }

}
