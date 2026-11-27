using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.AppServer;
using DotCraft.Sessions;
using DotHarness.Acp.Rpc;
using DotHarness.Acp.V1;
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

        if (method == Contract.AppServerMethodNames.ApprovalRequest)
            return await HandleApprovalRequestAsync(sessionId, wireParams, ct).ConfigureAwait(false);

        if (method == Contract.AppServerMethodNames.DynamicToolCall)
            return await HandleRuntimeToolCallAsync(wireParams, ct).ConfigureAwait(false);

        if (method.StartsWith("ext/acp/", StringComparison.Ordinal))
            return await HandleExtAcpForwardAsync(sessionId, method["ext/acp/".Length..], wireParams, ct).ConfigureAwait(false);

        return new { decision = "decline" };
    }

    private async Task<object> HandleApprovalRequestAsync(string sessionId, JsonElement wireParams, CancellationToken ct)
    {
        var approvalType = wireParams.TryGetProperty("approvalType", out var at) ? at.GetString() : null;
        var operation = wireParams.TryGetProperty("operation", out var op) ? op.GetString() ?? "" : "";
        var target = wireParams.TryGetProperty("target", out var tg) ? tg.GetString() ?? "" : "";

        var toolKind = approvalType == "shell"
            ? ToolKind.Execute
            : operation.ToLowerInvariant() switch
            {
                "write" or "edit" => ToolKind.Edit,
                "delete" => ToolKind.Delete,
                _ => ToolKind.Read
            };
        var reqId = wireParams.TryGetProperty("requestId", out var rid) ? rid.GetString() ?? "" : "";
        var shellReasons = wireParams.TryGetProperty("shell", out var shell)
            && shell.ValueKind == JsonValueKind.Object
            && shell.TryGetProperty("reasons", out var reasons)
            && reasons.ValueKind == JsonValueKind.Array
                ? string.Join(" ", reasons.EnumerateArray().Select(reason => reason.GetString()).Where(reason => !string.IsNullOrWhiteSpace(reason)))
                : string.Empty;

        var request = new RequestPermissionRequest
        {
            SessionId = new(sessionId),
            ToolCall = new ToolCallUpdate
            {
                ToolCallId = new(reqId),
                Title = approvalType == "shell"
                    ? $"Shell: {(operation.Length > 80 ? operation[..80] + "..." : operation)}"
                      + (shellReasons.Length > 0 ? $" ({shellReasons})" : string.Empty)
                    : $"File {operation}: {target}",
                Kind = toolKind,
                Status = ToolCallStatus.Pending
            },
            Options =
            [
                new PermissionOption { OptionId = new("allow-once"), Name = "Allow once", Kind = PermissionOptionKind.AllowOnce },
                new PermissionOption { OptionId = new("allow-always"), Name = "Allow always", Kind = PermissionOptionKind.AllowAlways },
                new PermissionOption { OptionId = new("reject-once"), Name = "Reject", Kind = PermissionOptionKind.RejectOnce }
            ]
        };

        try
        {
            var agent = await GetAgentAsync(ct).ConfigureAwait(false);
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(_permissionRequestTimeout);
            var response = await agent.Client.SessionRequestPermissionAsync(request, timeoutCts.Token).ConfigureAwait(false);
            var decision = response.Outcome switch
            {
                RequestPermissionOutcome.Case2 selected => selected.Value.OptionId.Value switch
                {
                    "allow-always" => "acceptAlways",
                    "allow-once" => "accept",
                    _ => "decline"
                },
                RequestPermissionOutcome.Case1 => "cancel",
                _ => "decline"
            };
            return new { decision };
        }
        catch
        {
            return new { decision = "decline" };
        }
    }

    private async Task<RuntimeDynamicToolCallResult> HandleRuntimeToolCallAsync(JsonElement wireParams, CancellationToken ct)
    {
        var toolName = TryGetStringProperty(wireParams, "tool");
        if (string.IsNullOrWhiteSpace(toolName))
            return AcpRuntimeTools.Failed("InvalidRequest", "Dynamic tool request did not include tool name.");

        var @namespace = TryGetStringProperty(wireParams, "namespace");
        if (!_runtimeToolDescriptors.TryGetValue(AcpRuntimeTools.RuntimeToolKey(@namespace, toolName), out var descriptor))
        {
            return AcpRuntimeTools.Failed(
                "UnsupportedTool",
                $"Dynamic tool '{toolName}' is not registered for this ACP session.");
        }

        var argumentsElement = wireParams.TryGetProperty("arguments", out var args) && args.ValueKind == JsonValueKind.Object
            ? args.Clone()
            : EmptyObjectElement();

        try
        {
            var agent = await GetAgentAsync(ct).ConfigureAwait(false);
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(_extForwardTimeout);
            var result = await agent.Connection.RequestAsync(
                descriptor.AcpMethod,
                argumentsElement,
                timeoutCts.Token).ConfigureAwait(false);

            RuntimeDynamicToolCallResult? toolResult;
            try
            {
                toolResult = result.Deserialize<RuntimeDynamicToolCallResult>(JsonOptions);
            }
            catch (JsonException ex)
            {
                return AcpRuntimeTools.Failed("DynamicToolResultInvalid", ex.Message);
            }

            if (toolResult == null)
                return AcpRuntimeTools.Failed("DynamicToolResultInvalid", "ACP runtime tool returned an empty result.");

            if (!AcpRuntimeTools.TryValidateResult(toolResult, out var resultError))
                return AcpRuntimeTools.Failed("DynamicToolResultInvalid", resultError);

            return toolResult;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return AcpRuntimeTools.Failed("DynamicToolTimeout", $"Dynamic tool '{toolName}' timed out.");
        }
        catch (OperationCanceledException)
        {
            return AcpRuntimeTools.Failed("DynamicToolCancelled", $"Dynamic tool '{toolName}' was cancelled.");
        }
        catch (AcpRpcException ex)
        {
            var code = ex.Code switch
            {
                -32601 => "DynamicToolUnavailable",
                -32602 => "InvalidArguments",
                -32800 => "DynamicToolCancelled",
                _ => "DynamicToolProtocolError"
            };
            var structured = new JsonObject
            {
                ["code"] = ex.Code,
                ["message"] = ex.Message
            };
            if (ex.ErrorData is { } data)
                structured["data"] = JsonNode.Parse(data.GetRawText());

            return AcpRuntimeTools.Failed(code, ex.Message, structured);
        }
        catch (Exception ex)
        {
            return AcpRuntimeTools.Failed("DynamicToolFailed", ex.Message);
        }
    }

    private async Task<object> HandleExtAcpForwardAsync(
        string sessionId,
        string acpMethod,
        JsonElement wireParams,
        CancellationToken ct)
    {
        if (string.IsNullOrEmpty(acpMethod))
            return new { error = "bad ext method" };

        try
        {
            var agent = await GetAgentAsync(ct).ConfigureAwait(false);
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(_extForwardTimeout);
            var callCt = timeoutCts.Token;

            switch (acpMethod)
            {
                case "fs/read_text_file":
                {
                    var path = TryGetStringProperty(wireParams, "path") ?? "";
                    var line = TryGetUInt32Property(wireParams, "line");
                    var limit = TryGetUInt32Property(wireParams, "limit");
                    var response = await agent.Client.FsReadTextFileAsync(new ReadTextFileRequest
                    {
                        SessionId = new(sessionId),
                        Path = path,
                        Line = line.HasValue ? new DotHarness.Acp.Protocol.Optional<uint?>(line.Value) : default,
                        Limit = limit.HasValue ? new DotHarness.Acp.Protocol.Optional<uint?>(limit.Value) : default
                    }, callCt).ConfigureAwait(false);
                    return new Contract.AcpFsReadTextFileResult { Content = response.Content };
                }
                case "fs/write_text_file":
                {
                    var path = TryGetStringProperty(wireParams, "path") ?? "";
                    var content = wireParams.TryGetProperty("content", out var c) && c.ValueKind == JsonValueKind.String
                        ? c.GetString() ?? ""
                        : "";
                    await agent.Client.FsWriteTextFileAsync(new WriteTextFileRequest
                    {
                        SessionId = new(sessionId),
                        Path = path,
                        Content = content
                    }, callCt).ConfigureAwait(false);
                    return new { };
                }
                case "terminal/create":
                {
                    var command = TryGetStringProperty(wireParams, "command") ?? "";
                    var args = TryGetStringArrayProperty(wireParams, "args");
                    var cwd = TryGetStringProperty(wireParams, "cwd");
                    var env = TryGetEnvVariablesProperty(wireParams, "env");
                    var outputByteLimit = TryGetUInt64Property(wireParams, "outputByteLimit");
                    var response = await agent.Client.TerminalCreateAsync(new CreateTerminalRequest
                    {
                        SessionId = new(sessionId),
                        Command = command,
                        Args = args != null ? new DotHarness.Acp.Protocol.Optional<string[]>(args) : default,
                        Env = env != null ? new DotHarness.Acp.Protocol.Optional<EnvVariable[]>(env) : default,
                        Cwd = cwd != null ? new DotHarness.Acp.Protocol.Optional<string?>(cwd) : default,
                        OutputByteLimit = outputByteLimit.HasValue
                            ? new DotHarness.Acp.Protocol.Optional<ulong?>(outputByteLimit.Value)
                            : default
                    }, callCt).ConfigureAwait(false);
                    return new Contract.AcpTerminalCreateResult { TerminalId = response.TerminalId.Value };
                }
                case "terminal/output":
                {
                    var terminalId = TryGetStringProperty(wireParams, "terminalId") ?? "";
                    var response = await agent.Client.TerminalOutputAsync(new TerminalOutputRequest
                    {
                        SessionId = new(sessionId),
                        TerminalId = new(terminalId)
                    }, callCt).ConfigureAwait(false);
                    var exitStatus = response.ExitStatus is { HasValue: true, Value: { } status }
                        ? new Contract.AcpTerminalExitStatus
                        {
                            ExitCode = status.ExitCode is { HasValue: true, Value: { } code } ? checked((int)code) : null,
                            Signal = status.Signal is { HasValue: true } ? status.Signal.Value : null
                        }
                        : null;
                    return new Contract.AcpTerminalOutputResult
                    {
                        Output = response.Output,
                        Truncated = response.Truncated,
                        ExitStatus = exitStatus
                    };
                }
                case "terminal/wait_for_exit":
                {
                    var terminalId = TryGetStringProperty(wireParams, "terminalId") ?? "";
                    var response = await agent.Client.TerminalWaitForExitAsync(new WaitForTerminalExitRequest
                    {
                        SessionId = new(sessionId),
                        TerminalId = new(terminalId)
                    }, callCt).ConfigureAwait(false);
                    return new Contract.AcpTerminalWaitForExitResult
                    {
                        ExitCode = response.ExitCode is { HasValue: true, Value: { } code } ? checked((int)code) : null,
                        Signal = response.Signal is { HasValue: true } ? response.Signal.Value : null
                    };
                }
                case "terminal/kill":
                {
                    var terminalId = TryGetStringProperty(wireParams, "terminalId") ?? "";
                    await agent.Client.TerminalKillAsync(new KillTerminalRequest
                    {
                        SessionId = new(sessionId),
                        TerminalId = new(terminalId)
                    }, callCt).ConfigureAwait(false);
                    return new { };
                }
                case "terminal/release":
                {
                    var terminalId = TryGetStringProperty(wireParams, "terminalId") ?? "";
                    await agent.Client.TerminalReleaseAsync(new ReleaseTerminalRequest
                    {
                        SessionId = new(sessionId),
                        TerminalId = new(terminalId)
                    }, callCt).ConfigureAwait(false);
                    return new { };
                }
                default:
                {
                    var forwarded = ParamsForIdeExtForward(wireParams);
                    var paramElement = forwarded is null
                        ? EmptyObjectElement()
                        : JsonSerializer.SerializeToElement(forwarded, JsonOptions);
                    var el = await agent.Connection.RequestAsync(acpMethod, paramElement, callCt).ConfigureAwait(false);
                    return JsonSerializer.Deserialize<object>(el.GetRawText(), JsonOptions) ?? new { };
                }
            }
        }
        catch
        {
            return new { error = "ext forward failed" };
        }
    }

    internal static object? ParamsForIdeExtForward(JsonElement wireParams)
    {
        if (wireParams.ValueKind != JsonValueKind.Object)
            return JsonSerializer.Deserialize<object>(wireParams.GetRawText(), JsonOptions);

        if (!wireParams.TryGetProperty("threadId", out _))
            return JsonSerializer.Deserialize<object>(wireParams.GetRawText(), JsonOptions);

        if (JsonNode.Parse(wireParams.GetRawText()) is not JsonObject obj)
            return JsonSerializer.Deserialize<object>(wireParams.GetRawText(), JsonOptions);

        obj.Remove("threadId");
        return obj;
    }

    private static uint? TryGetUInt32Property(JsonElement obj, string name) =>
        obj.ValueKind == JsonValueKind.Object
        && obj.TryGetProperty(name, out var prop)
        && prop.ValueKind == JsonValueKind.Number
        && prop.TryGetUInt32(out var val)
            ? val
            : null;

    private static ulong? TryGetUInt64Property(JsonElement obj, string name) =>
        obj.ValueKind == JsonValueKind.Object
        && obj.TryGetProperty(name, out var prop)
        && prop.ValueKind == JsonValueKind.Number
        && prop.TryGetUInt64(out var val)
            ? val
            : null;

    private static string[]? TryGetStringArrayProperty(JsonElement obj, string name)
    {
        if (obj.ValueKind != JsonValueKind.Object
            || !obj.TryGetProperty(name, out var prop)
            || prop.ValueKind != JsonValueKind.Array)
            return null;

        return prop.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString()!)
            .ToArray();
    }

    private static EnvVariable[]? TryGetEnvVariablesProperty(JsonElement obj, string name)
    {
        if (obj.ValueKind != JsonValueKind.Object
            || !obj.TryGetProperty(name, out var prop)
            || prop.ValueKind != JsonValueKind.Array)
            return null;

        var list = new List<EnvVariable>();
        foreach (var item in prop.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
                continue;
            var key = item.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() : null;
            var val = item.TryGetProperty("value", out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
            if (!string.IsNullOrEmpty(key))
                list.Add(new EnvVariable { Name = key, Value = val });
        }

        return list.Count > 0 ? [.. list] : null;
    }

    private static JsonElement EmptyObjectElement()
    {
        using var doc = JsonDocument.Parse("{}");
        return doc.RootElement.Clone();
    }
}
