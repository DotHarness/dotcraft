using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using DotCraft.Auth.OpenAI;
using DotCraft.Configuration;
using DotCraft.Protocol.AppServer;

namespace DotCraft.AppServerTestClient;

internal sealed class DeferredLoadingSmokeRunner(string dotcraftBin, DeferredLoadingSmokeCliOptions options)
{
    private const string HiddenMcpServerCommand = "deferred-loading-smoke-mcp-server";

    public async Task<DeferredLoadingSmokeReport> RunAsync(DeferredLoadingSmokeMatrix matrix)
    {
        Directory.CreateDirectory(options.WorkRoot);
        var report = new DeferredLoadingSmokeReport
        {
            StartedAt = DateTimeOffset.UtcNow,
            WorkRoot = options.WorkRoot
        };

        foreach (var skip in BuildUnsupportedProtocolSkips(matrix))
            report.Cases.Add(skip);

        foreach (var protocol in DeferredLoadingSmokeJson.SupportedProtocols)
        {
            var providerCase = FindProviderCase(matrix, protocol);
            if (providerCase is null)
            {
                report.Cases.Add(DeferredLoadingSmokeCaseReport.Skipped(
                    protocol,
                    string.Empty,
                    string.Empty,
                    "missing_protocol_mapping"));
                continue;
            }

            var selection = new DeferredLoadingSmokeProviderSelection(
                protocol,
                providerCase.ProviderId.Trim(),
                providerCase.Model.Trim());
            var skipReason = ValidateSelection(providerCase, selection, out var validatedSelection);
            if (skipReason is not null)
            {
                report.Cases.Add(DeferredLoadingSmokeCaseReport.Skipped(
                    selection.Protocol,
                    selection.ProviderId,
                    selection.Model,
                    skipReason));
                continue;
            }

            var caseReport = await RunProviderAsync(validatedSelection);
            report.Cases.Add(caseReport);
            Console.Error.WriteLine(
                $"[deferred-loading-smoke] {validatedSelection.Protocol}/{DeferredLoadingSmokeScenarios.NativeDeferredToolSearch}: {caseReport.Status} {caseReport.Message ?? caseReport.ErrorMessage}");
        }

        report.FinalizeSummary(DateTimeOffset.UtcNow);
        return report;
    }

    private static IEnumerable<DeferredLoadingSmokeCaseReport> BuildUnsupportedProtocolSkips(
        DeferredLoadingSmokeMatrix matrix)
    {
        foreach (var provider in matrix.Providers)
        {
            if (TryNormalizeProtocol(provider.Protocol, out var normalized)
                && DeferredLoadingSmokeJson.SupportedProtocols.Contains(normalized, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            yield return DeferredLoadingSmokeCaseReport.Skipped(
                provider.Protocol,
                provider.ProviderId,
                provider.Model,
                "unsupported_protocol");
        }
    }

    private static DeferredLoadingSmokeProviderCase? FindProviderCase(
        DeferredLoadingSmokeMatrix matrix,
        string protocol)
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
        DeferredLoadingSmokeProviderCase providerCase,
        DeferredLoadingSmokeProviderSelection selection,
        out DeferredLoadingSmokeProviderSelection validatedSelection)
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
            File.WriteAllText(
                tempConfigPath,
                DeferredLoadingSmokeWorkspace.BuildConfigJson(selection.ProviderId, selection.Model, "dotcraft-test-client"));
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

            if (!string.Equals(providerCase.Protocol.Trim(), selection.Protocol, StringComparison.OrdinalIgnoreCase))
                validatedSelection = selection with { Protocol = selection.Protocol };

            var authMethod = ModelProviderAuthMethods.Normalize(provider.AuthMethod);
            if (string.Equals(authMethod, ModelProviderAuthMethods.ChatGptOAuth, StringComparison.Ordinal))
            {
                if (new OpenAITokenStore().Load() is null)
                    return "chatgpt_oauth_not_logged_in";
                return null;
            }

            if (string.IsNullOrWhiteSpace(provider.ApiKey))
                return "provider_api_key_missing";

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

    private async Task<DeferredLoadingSmokeCaseReport> RunProviderAsync(
        DeferredLoadingSmokeProviderSelection provider)
    {
        var stopwatch = Stopwatch.StartNew();
        var (mcpCommand, mcpArguments) = ResolveMcpServerCommand();
        var workspacePath = DeferredLoadingSmokeWorkspace.Create(
            options.WorkRoot,
            provider,
            mcpCommand,
            mcpArguments);
        var traceDbPath = DeferredLoadingSmokeWorkspace.TraceDbPath(workspacePath);
        var caseReport = new DeferredLoadingSmokeCaseReport
        {
            Protocol = provider.Protocol,
            ProviderId = provider.ProviderId,
            Model = provider.Model,
            WorkspacePath = workspacePath,
            TraceDbPath = traceDbPath,
            TargetToolName = DeferredLoadingSmokeTools.Echo
        };

        try
        {
            var threadId = await RunNativeDeferredToolSearchAsync(provider, workspacePath);
            caseReport.ThreadId = threadId;
            var events = await ReadThreadEventsWithRetryAsync(traceDbPath, threadId);
            var validation = DeferredLoadingSmokeTraceValidator.Validate(
                events,
                provider.Protocol,
                DeferredLoadingSmokeTools.Echo);
            caseReport.Status = validation.Success
                ? DeferredLoadingSmokeStatuses.Passed
                : DeferredLoadingSmokeStatuses.Failed;
            caseReport.Message = validation.Message;
            caseReport.DeferredToolLoadingObserved = validation.DeferredToolLoadingObserved;
            caseReport.WireShape = validation.WireShape;
            caseReport.TargetToolName = validation.TargetToolName ?? DeferredLoadingSmokeTools.Echo;
        }
        catch (Exception ex)
        {
            caseReport.Status = DeferredLoadingSmokeStatuses.Failed;
            caseReport.ErrorMessage = ex.Message;
        }
        finally
        {
            stopwatch.Stop();
            caseReport.DurationMs = stopwatch.ElapsedMilliseconds;
        }

        return caseReport;
    }

    private async Task<string> RunNativeDeferredToolSearchAsync(
        DeferredLoadingSmokeProviderSelection provider,
        string workspacePath)
    {
        await using var client = await AppServerClient.SpawnAsync(dotcraftBin, workspacePath);
        await client.InitializeAsync();
        var threadId = await StartThreadAsync(client, workspacePath, provider, "native deferred loading smoke");

        await RunTurnAsync(client, threadId, BuildPrompt());

        await client.StopAsync();
        return threadId;
    }

    private static string BuildPrompt() =>
        $"""
        This is a provider-native deferred loading smoke test.
        You must use tools. Do not answer from memory.
        First call `tool_search` with query exactly `{DeferredLoadingSmokeTools.Echo}` and max_results 5.
        Then call the discovered `{DeferredLoadingSmokeTools.Echo}` tool with message exactly `DEFERRED_SMOKE_PING`.
        After the tool result, reply exactly `{DeferredLoadingSmokeTools.SuccessToken}` and no extra words.
        """;

    private async Task<string> StartThreadAsync(
        AppServerClient client,
        string workspacePath,
        DeferredLoadingSmokeProviderSelection provider,
        string displayName)
    {
        var threadResponse = await client.SendRequestAsync(DotCraft.Protocol.Contracts.AppServer.AppServerMethodNames.ThreadStart, new
        {
            identity = new
            {
                channelName = "appserver-deferred-loading-smoke",
                workspacePath
            },
            config = new
            {
                mode = "agent",
                providerId = provider.ProviderId,
                model = provider.Model
            },
            displayName
        });
        EnsureNoJsonRpcError(threadResponse, "thread/start");
        return threadResponse.RootElement
            .GetProperty("result")
            .GetProperty("thread")
            .GetProperty("id")
            .GetString()!;
    }

    private async Task RunTurnAsync(
        AppServerClient client,
        string threadId,
        string text)
    {
        var turnResponse = await client.SendRequestAsync(DotCraft.Protocol.Contracts.AppServer.AppServerMethodNames.TurnStart, new
        {
            threadId,
            input = new[] { new { type = "text", text } }
        });
        EnsureNoJsonRpcError(turnResponse, "turn/start");
        var turnId = turnResponse.RootElement
            .GetProperty("result")
            .GetProperty("turn")
            .GetProperty("id")
            .GetString()!;

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
            if (method == DotCraft.Protocol.Contracts.AppServer.AppServerMethodNames.TurnCompleted)
                return;
            if (method is DotCraft.Protocol.Contracts.AppServer.AppServerMethodNames.TurnFailed or DotCraft.Protocol.Contracts.AppServer.AppServerMethodNames.TurnCancelled)
                throw new InvalidOperationException($"turn terminal failure: {ExtractNotificationMessage(notification)}");
        }

        throw new TimeoutException($"Timed out waiting for turn {turnId} to complete.");
    }

    private static async Task<IReadOnlyList<DeferredLoadingSmokeTraceEvent>> ReadThreadEventsWithRetryAsync(
        string traceDbPath,
        string threadId)
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var events = DeferredLoadingSmokeTraceReader.ReadThreadEvents(traceDbPath, threadId);
            if (events.Count > 0)
                return events;

            await Task.Delay(TimeSpan.FromMilliseconds(250));
        }

        return DeferredLoadingSmokeTraceReader.ReadThreadEvents(traceDbPath, threadId);
    }

    private static (string Command, string[] Arguments) ResolveMcpServerCommand()
    {
        var processPath = Environment.ProcessPath;
        var assemblyPath = Assembly.GetEntryAssembly()?.Location;
        var processName = string.IsNullOrWhiteSpace(processPath)
            ? string.Empty
            : Path.GetFileNameWithoutExtension(processPath);

        if (!string.IsNullOrWhiteSpace(processPath)
            && processName.Equals("dotnet", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(assemblyPath)
            && string.Equals(Path.GetExtension(assemblyPath), ".dll", StringComparison.OrdinalIgnoreCase))
        {
            return (processPath, [assemblyPath, HiddenMcpServerCommand]);
        }

        if (!string.IsNullOrWhiteSpace(processPath))
            return (processPath, [HiddenMcpServerCommand]);

        if (!string.IsNullOrWhiteSpace(assemblyPath))
            return ("dotnet", [assemblyPath, HiddenMcpServerCommand]);

        return ("dotcraft-test-client", [HiddenMcpServerCommand]);
    }

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

        if (parameters.TryGetProperty("error", out var error))
            return error.GetRawText();

        if (parameters.TryGetProperty("message", out var message))
            return message.GetString() ?? notification.RootElement.GetRawText();

        return parameters.GetRawText();
    }
}
