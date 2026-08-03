using System.Diagnostics;
using System.Text;
using System.Text.Json;
using DotCraft.Configuration;
using DotCraft.Protocol.AppServer;
using DotCraft.Sessions;

namespace DotCraft.AppServerTestClient;

internal sealed class StreamRetrySmokeRunner(string dotcraftBin, StreamRetrySmokeCliOptions options)
{
    public const string ExpectedMarker = "STREAM_RETRY_OK";
    private const string ExpectedReconnectMessage = "Reconnecting... 1/1";

    public async Task<StreamRetrySmokeReport> RunAsync(StreamRetrySmokeMatrix matrix)
    {
        Directory.CreateDirectory(options.WorkRoot);
        var report = new StreamRetrySmokeReport
        {
            StartedAt = DateTimeOffset.UtcNow,
            WorkRoot = options.WorkRoot
        };

        foreach (var skip in BuildUnsupportedProtocolSkips(matrix))
            report.Cases.Add(skip);

        foreach (var protocol in StreamRetrySmokeProtocols.Supported)
        {
            var providerCase = FindProviderCase(matrix, protocol);
            if (providerCase is null)
            {
                report.Cases.Add(StreamRetrySmokeCaseReport.Skipped(
                    protocol,
                    string.Empty,
                    string.Empty,
                    "missing_protocol_mapping"));
                continue;
            }

            var selection = new StreamRetrySmokeProviderSelection(
                protocol,
                providerCase.ProviderId.Trim(),
                providerCase.Model.Trim(),
                UpstreamEndPoint: string.Empty);
            var skipReason = ValidateSelection(selection, out var validatedSelection);
            if (skipReason is not null)
            {
                report.Cases.Add(StreamRetrySmokeCaseReport.Skipped(
                    selection.Protocol,
                    selection.ProviderId,
                    selection.Model,
                    skipReason));
                continue;
            }

            var caseReport = await RunCaseAsync(validatedSelection);
            report.Cases.Add(caseReport);
            Console.Error.WriteLine(
                $"[stream-retry-smoke] {validatedSelection.Protocol}: {caseReport.Status} {caseReport.Message ?? caseReport.ErrorMessage}");
        }

        report.FinalizeSummary(DateTimeOffset.UtcNow);
        return report;
    }

    private static IEnumerable<StreamRetrySmokeCaseReport> BuildUnsupportedProtocolSkips(StreamRetrySmokeMatrix matrix)
    {
        foreach (var provider in matrix.Providers)
        {
            if (TryNormalizeProtocol(provider.Protocol, out _))
                continue;

            yield return StreamRetrySmokeCaseReport.Skipped(
                provider.Protocol,
                provider.ProviderId,
                provider.Model,
                "unsupported_protocol");
        }
    }

    private static StreamRetrySmokeProviderCase? FindProviderCase(StreamRetrySmokeMatrix matrix, string protocol)
    {
        return matrix.Providers.FirstOrDefault(provider =>
            TryNormalizeProtocol(provider.Protocol, out var normalized) &&
            string.Equals(normalized, protocol, StringComparison.OrdinalIgnoreCase));
    }

    private static bool TryNormalizeProtocol(string? protocol, out string normalized)
    {
        try
        {
            normalized = ModelProviderProtocols.Normalize(protocol);
            return true;
        }
        catch (ArgumentException)
        {
            normalized = string.Empty;
            return false;
        }
    }

    private static string? ValidateSelection(
        StreamRetrySmokeProviderSelection selection,
        out StreamRetrySmokeProviderSelection validatedSelection)
    {
        validatedSelection = selection;

        if (string.IsNullOrWhiteSpace(selection.ProviderId))
            return "missing_provider_id";
        if (string.IsNullOrWhiteSpace(selection.Model))
            return "missing_model";

        var tempConfigPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), ".craft", "config.json");
        Directory.CreateDirectory(Path.GetDirectoryName(tempConfigPath)!);
        try
        {
            File.WriteAllText(tempConfigPath, CompactSmokeWorkspace.BuildConfigJson(selection.ProviderId, selection.Model));
            var config = AppConfig.LoadWithGlobalFallback(tempConfigPath);
            if (!config.Providers.TryGetValue(selection.ProviderId, out var provider))
                return "provider_not_configured";

            string actualProtocol;
            try
            {
                actualProtocol = ModelProviderProtocols.Normalize(provider.Protocol);
            }
            catch (ArgumentException)
            {
                return "provider_protocol_unsupported";
            }

            if (!string.Equals(actualProtocol, selection.Protocol, StringComparison.OrdinalIgnoreCase))
                return "provider_protocol_mismatch";

            if (string.IsNullOrWhiteSpace(provider.ApiKey))
                return "provider_api_key_missing";

            EffectiveModelRuntime runtime;
            try
            {
                runtime = ModelProviderResolver.ResolveMain(config, selection.ProviderId, selection.Model);
            }
            catch (Exception)
            {
                return "provider_runtime_invalid";
            }

            if (!Uri.TryCreate(runtime.EndPoint, UriKind.Absolute, out _))
                return "provider_endpoint_invalid";

            validatedSelection = selection with { UpstreamEndPoint = runtime.EndPoint };
            return null;
        }
        finally
        {
            var root = Directory.GetParent(Path.GetDirectoryName(tempConfigPath)!)?.FullName;
            if (!string.IsNullOrWhiteSpace(root) && Directory.Exists(root))
            {
                try { Directory.Delete(root, recursive: true); } catch { }
            }
        }
    }

    private async Task<StreamRetrySmokeCaseReport> RunCaseAsync(StreamRetrySmokeProviderSelection provider)
    {
        var stopwatch = Stopwatch.StartNew();
        StreamRetrySmokeFaultProxy? proxy = null;
        var caseReport = new StreamRetrySmokeCaseReport
        {
            Protocol = provider.Protocol,
            ProviderId = provider.ProviderId,
            Model = provider.Model,
            UpstreamEndPoint = RedactEndpoint(provider.UpstreamEndPoint)
        };

        try
        {
            proxy = await StreamRetrySmokeFaultProxy.StartAsync(new Uri(provider.UpstreamEndPoint));
            caseReport.ProxyEndPoint = proxy.Endpoint.ToString();
            var workspacePath = StreamRetrySmokeWorkspace.Create(options.WorkRoot, provider, proxy.Endpoint);
            caseReport.WorkspacePath = workspacePath;

            await using var client = await AppServerClient.SpawnAsync(dotcraftBin, workspacePath);
            await client.InitializeAsync();
            var threadId = await StartThreadAsync(client, workspacePath, provider);
            var turnResult = await RunTurnAsync(client, threadId);
            caseReport.ThreadId = threadId;
            caseReport.TurnId = turnResult.TurnId;
            caseReport.StreamErrorCount = turnResult.StreamErrorMessages.Count;

            var persisted = await ReadThreadAndValidateAsync(client, threadId, turnResult.TurnId);
            var finalAssistantText = string.IsNullOrWhiteSpace(turnResult.AssistantText)
                ? persisted.AssistantText
                : turnResult.AssistantText;
            caseReport.FinalAssistantText = finalAssistantText;

            ApplyProxySnapshot(caseReport, proxy.Snapshot());
            var validationMessage = ValidateCase(caseReport, turnResult, persisted);
            caseReport.Status = validationMessage is null
                ? StreamRetrySmokeStatuses.Passed
                : StreamRetrySmokeStatuses.Failed;
            caseReport.Message = validationMessage ?? "stream_retry_smoke_passed";
            await client.StopAsync();
        }
        catch (Exception ex)
        {
            caseReport.Status = StreamRetrySmokeStatuses.Failed;
            caseReport.ErrorMessage = ex.Message;
            if (proxy is not null)
                ApplyProxySnapshot(caseReport, proxy.Snapshot());
        }
        finally
        {
            if (proxy is not null)
                await proxy.DisposeAsync();
            stopwatch.Stop();
            caseReport.DurationMs = stopwatch.ElapsedMilliseconds;
        }

        return caseReport;
    }

    private async Task<string> StartThreadAsync(
        AppServerClient client,
        string workspacePath,
        StreamRetrySmokeProviderSelection provider)
    {
        var threadResponse = await client.SendRequestAsync(DotCraft.Protocol.AppServer.AppServerMethodNames.ThreadStart, new
        {
            identity = new
            {
                channelName = "appserver-stream-retry-smoke",
                workspacePath
            },
            config = new
            {
                mode = "agent",
                providerId = provider.ProviderId,
                model = provider.Model
            },
            displayName = $"stream retry smoke {provider.Protocol}"
        });
        EnsureNoJsonRpcError(threadResponse, "thread/start");
        return threadResponse.RootElement
            .GetProperty("result")
            .GetProperty("thread")
            .GetProperty("id")
            .GetString()!;
    }

    private async Task<StreamRetryTurnResult> RunTurnAsync(AppServerClient client, string threadId)
    {
        var turnResponse = await client.SendRequestAsync(DotCraft.Protocol.AppServer.AppServerMethodNames.TurnStart, new
        {
            threadId,
            input = new[] { new { type = "text", text = BuildPrompt() } }
        });
        EnsureNoJsonRpcError(turnResponse, "turn/start");
        var turnId = turnResponse.RootElement
            .GetProperty("result")
            .GetProperty("turn")
            .GetProperty("id")
            .GetString()!;

        var assistantText = new StringBuilder();
        var streamErrorMessages = new List<string>();
        var deadline = DateTimeOffset.UtcNow + options.TurnTimeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            var remaining = deadline - DateTimeOffset.UtcNow;
            var notification = await client.WaitForNotificationAsync(timeout: remaining);
            if (notification is null)
                break;

            if (!notification.RootElement.TryGetProperty("method", out var methodElement))
                continue;

            var method = methodElement.GetString();
            if (method == DotCraft.Protocol.AppServer.AppServerMethodNames.AgentMessageDelta)
                AppendDelta(notification, assistantText);
            else if (method == DotCraft.Protocol.AppServer.AppServerMethodNames.SystemEvent)
                CaptureStreamError(notification, streamErrorMessages);
            else if (method == DotCraft.Protocol.AppServer.AppServerMethodNames.TurnCompleted)
                return new StreamRetryTurnResult(turnId, assistantText.ToString(), streamErrorMessages);
            else if (method is DotCraft.Protocol.AppServer.AppServerMethodNames.TurnFailed or DotCraft.Protocol.AppServer.AppServerMethodNames.TurnCancelled)
                throw new InvalidOperationException($"turn terminal failure: {ExtractNotificationMessage(notification)}");
        }

        throw new TimeoutException($"Timed out waiting for turn {turnId} to complete.");
    }

    private async Task<PersistedTurnValidation> ReadThreadAndValidateAsync(
        AppServerClient client,
        string threadId,
        string turnId)
    {
        var threadResponse = await client.SendRequestAsync(DotCraft.Protocol.AppServer.AppServerMethodNames.ThreadRead, new
        {
            threadId,
            includeTurns = true
        });
        EnsureNoJsonRpcError(threadResponse, "thread/read");

        var thread = threadResponse.RootElement.GetProperty("result").GetProperty("thread");
        if (!thread.TryGetProperty("turns", out var turns) || turns.ValueKind != JsonValueKind.Array)
            return new PersistedTurnValidation(false, true, string.Empty);

        var foundCompletedTurn = false;
        var foundFailedTurn = false;
        var assistantText = new StringBuilder();
        foreach (var turn in turns.EnumerateArray())
        {
            var currentTurnId = turn.TryGetProperty("id", out var idElement)
                ? idElement.GetString()
                : null;
            var status = turn.TryGetProperty("status", out var statusElement)
                ? statusElement.GetString()
                : null;

            if (string.Equals(status, "failed", StringComparison.OrdinalIgnoreCase))
                foundFailedTurn = true;

            if (!string.Equals(currentTurnId, turnId, StringComparison.Ordinal))
                continue;

            foundCompletedTurn = string.Equals(status, "completed", StringComparison.OrdinalIgnoreCase);
            if (turn.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
                AppendPersistedAssistantText(items, assistantText);
        }

        return new PersistedTurnValidation(foundCompletedTurn, foundFailedTurn, assistantText.ToString());
    }

    private static string? ValidateCase(
        StreamRetrySmokeCaseReport caseReport,
        StreamRetryTurnResult turnResult,
        PersistedTurnValidation persisted)
    {
        if (turnResult.StreamErrorMessages.Count != 1)
            return $"expected_one_stream_error_but_saw_{turnResult.StreamErrorMessages.Count}";
        if (!string.Equals(turnResult.StreamErrorMessages[0], ExpectedReconnectMessage, StringComparison.Ordinal))
            return "stream_error_message_mismatch";
        if (caseReport.FaultedRequests != 1)
            return $"expected_one_faulted_request_but_saw_{caseReport.FaultedRequests ?? 0}";
        if (caseReport.ForwardedRequests is null or < 1)
            return $"expected_forwarded_request_but_saw_{caseReport.ForwardedRequests ?? 0}";
        if (caseReport.FinalAssistantText?.Contains(ExpectedMarker, StringComparison.Ordinal) != true)
            return "assistant_marker_missing";
        if (!persisted.Completed)
            return "persisted_turn_not_completed";
        if (persisted.HasFailedTurn)
            return "persisted_failed_turn_present";

        return null;
    }

    private static void ApplyProxySnapshot(
        StreamRetrySmokeCaseReport caseReport,
        StreamRetrySmokeProxySnapshot snapshot)
    {
        caseReport.FaultedRequests = snapshot.FaultedRequests;
        caseReport.ForwardedRequests = snapshot.ForwardedRequests;
        caseReport.ProxyRequests = snapshot.Requests;
    }

    private static void AppendDelta(JsonDocument notification, StringBuilder assistantText)
    {
        if (notification.RootElement.TryGetProperty("params", out var parameters) &&
            parameters.TryGetProperty("delta", out var delta) &&
            delta.ValueKind == JsonValueKind.String)
        {
            assistantText.Append(delta.GetString());
        }
    }

    private static void CaptureStreamError(JsonDocument notification, List<string> streamErrorMessages)
    {
        if (!notification.RootElement.TryGetProperty("params", out var parameters) ||
            !parameters.TryGetProperty("kind", out var kind) ||
            !string.Equals(kind.GetString(), "streamError", StringComparison.Ordinal))
        {
            return;
        }

        var message = parameters.TryGetProperty("message", out var messageElement)
            ? messageElement.GetString() ?? string.Empty
            : string.Empty;
        streamErrorMessages.Add(message);
    }

    private static void AppendPersistedAssistantText(JsonElement items, StringBuilder assistantText)
    {
        foreach (var item in items.EnumerateArray())
        {
            var payloadKind = item.TryGetProperty("payloadKind", out var payloadKindElement)
                ? payloadKindElement.GetString()
                : null;
            if (!string.Equals(payloadKind, "agentMessage", StringComparison.OrdinalIgnoreCase))
                continue;
            if (!item.TryGetProperty("payload", out var payload) ||
                !payload.TryGetProperty("text", out var textElement) ||
                textElement.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            assistantText.Append(textElement.GetString());
        }
    }

    private static string BuildPrompt() =>
        "Stream retry smoke test. Reply with exactly: STREAM_RETRY_OK";

    private static void EnsureNoJsonRpcError(JsonDocument response, string context)
    {
        if (!response.RootElement.TryGetProperty("error", out var error))
            return;

        var message = error.TryGetProperty("message", out var messageElement)
            ? messageElement.GetString()
            : response.RootElement.GetRawText();
        throw new InvalidOperationException($"{context}: {message}");
    }

    private static string ExtractNotificationMessage(JsonDocument notification)
    {
        if (!notification.RootElement.TryGetProperty("params", out var parameters))
            return notification.RootElement.GetRawText();

        if (parameters.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.String)
            return message.GetString() ?? string.Empty;
        if (parameters.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.String)
            return error.GetString() ?? string.Empty;

        return parameters.GetRawText();
    }

    private static string RedactEndpoint(string endpoint)
    {
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri))
            return endpoint;

        var builder = new UriBuilder(uri)
        {
            UserName = string.Empty,
            Password = string.Empty,
            Query = string.Empty
        };
        return builder.Uri.ToString();
    }

    private sealed record StreamRetryTurnResult(
        string TurnId,
        string AssistantText,
        IReadOnlyList<string> StreamErrorMessages);

    private sealed record PersistedTurnValidation(
        bool Completed,
        bool HasFailedTurn,
        string AssistantText);
}
