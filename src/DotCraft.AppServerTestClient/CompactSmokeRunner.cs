using System.Diagnostics;
using System.Text.Json;
using DotCraft.Auth.OpenAI;
using DotCraft.Configuration;
using DotCraft.Context.Compaction;
using DotCraft.Protocol.AppServer;
using Microsoft.Extensions.AI;

namespace DotCraft.AppServerTestClient;

internal sealed class CompactSmokeRunner(string dotcraftBin, CompactSmokeCliOptions options)
{
    public async Task<CompactSmokeReport> RunAsync(CompactSmokeMatrix matrix)
    {
        Directory.CreateDirectory(options.WorkRoot);
        var report = new CompactSmokeReport
        {
            StartedAt = DateTimeOffset.UtcNow,
            WorkRoot = options.WorkRoot
        };

        var scenarios = options.ScenarioFilter.Count == 0
            ? CompactSmokeScenarios.All
            : options.ScenarioFilter;

        foreach (var skip in BuildUnsupportedProtocolSkips(matrix, scenarios))
            report.Cases.Add(skip);

        foreach (var protocol in CompactSmokeJson.SupportedProtocols)
        {
            var providerCase = FindProviderCase(matrix, protocol);
            if (providerCase is null)
            {
                AddSkippedScenarios(report, protocol, string.Empty, string.Empty, "missing_protocol_mapping", scenarios);
                continue;
            }

            var selection = new CompactSmokeProviderSelection(
                protocol,
                providerCase.ProviderId.Trim(),
                providerCase.Model.Trim());
            var skipReason = ValidateSelection(selection);
            if (skipReason is not null)
            {
                AddSkippedScenarios(report, selection.Protocol, selection.ProviderId, selection.Model, skipReason, scenarios);
                continue;
            }

            foreach (var scenario in scenarios)
            {
                var caseReport = await RunScenarioAsync(selection, scenario);
                report.Cases.Add(caseReport);
                Console.Error.WriteLine(
                    $"[compact-smoke] {selection.Protocol}/{scenario}: {caseReport.Status} {caseReport.Message ?? caseReport.ErrorMessage}");
            }
        }

        report.FinalizeSummary(DateTimeOffset.UtcNow);
        return report;
    }

    private static IEnumerable<CompactSmokeCaseReport> BuildUnsupportedProtocolSkips(
        CompactSmokeMatrix matrix,
        IReadOnlyList<string> scenarios)
    {
        foreach (var provider in matrix.Providers)
        {
            if (TryNormalizeProtocol(provider.Protocol, out _))
                continue;

            foreach (var scenario in scenarios)
            {
                yield return CompactSmokeCaseReport.Skipped(
                    provider.Protocol,
                    provider.ProviderId,
                    provider.Model,
                    scenario,
                    "unsupported_protocol");
            }
        }
    }

    private static CompactSmokeProviderCase? FindProviderCase(CompactSmokeMatrix matrix, string protocol)
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

    private static void AddSkippedScenarios(
        CompactSmokeReport report,
        string protocol,
        string providerId,
        string model,
        string reason,
        IReadOnlyList<string> scenarios)
    {
        foreach (var scenario in scenarios)
        {
            report.Cases.Add(CompactSmokeCaseReport.Skipped(
                protocol,
                providerId,
                model,
                scenario,
                reason));
        }
    }

    private static string? ValidateSelection(CompactSmokeProviderSelection selection)
    {
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

            var authMethod = ModelProviderAuthMethods.Normalize(provider.AuthMethod);
            if (string.Equals(authMethod, ModelProviderAuthMethods.ChatGptOAuth, StringComparison.Ordinal))
            {
                // OAuth providers authenticate via ~/.craft/auth.json; the runtime auth pipeline
                // (OpenAIOAuthPipelinePolicy) refreshes the token on 401, so we only need to
                // confirm a token bundle was persisted by a prior login flow.
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

    private async Task<CompactSmokeCaseReport> RunScenarioAsync(
        CompactSmokeProviderSelection provider,
        string scenario)
    {
        var stopwatch = Stopwatch.StartNew();
        var workspacePath = CompactSmokeWorkspace.Create(options.WorkRoot, provider, scenario);
        var traceDbPath = CompactSmokeWorkspace.TraceDbPath(workspacePath);
        var caseReport = new CompactSmokeCaseReport
        {
            Protocol = provider.Protocol,
            ProviderId = provider.ProviderId,
            Model = provider.Model,
            Scenario = scenario,
            WorkspacePath = workspacePath,
            TraceDbPath = traceDbPath
        };

        try
        {
            var threadId = scenario switch
            {
                CompactSmokeScenarios.ManualSnapshotPartial => await RunManualSnapshotPartialAsync(provider, workspacePath),
                CompactSmokeScenarios.ManualLegacyPartial => await RunManualLegacyPartialAsync(provider, workspacePath),
                CompactSmokeScenarios.AutoSnapshotFork => await RunAutoSnapshotForkAsync(provider, workspacePath),
                _ => throw new InvalidOperationException($"Unknown compact-smoke scenario '{scenario}'.")
            };

            caseReport.ThreadId = threadId;
            var events = CompactSmokeTraceReader.ReadThreadEvents(traceDbPath, threadId);
            var validation = CompactSmokeTraceValidator.Validate(scenario, provider, events);
            caseReport.Status = validation.Success ? CompactSmokeStatuses.Passed : CompactSmokeStatuses.Failed;
            caseReport.Message = validation.Message;
            caseReport.FallbackReason = validation.FallbackReason;
            caseReport.SnapshotSource = validation.SnapshotSource;
            caseReport.SnapshotInvalidReason = validation.SnapshotInvalidReason;
            caseReport.CacheHitRequired = validation.CacheHitRequired;
            caseReport.CacheHit = validation.CacheHit;
            caseReport.CacheShapeApplied = validation.CacheShapeApplied;
            caseReport.CacheShapeKind = validation.CacheShapeKind;
            caseReport.PromptCacheKeyPresent = validation.PromptCacheKeyPresent;
            caseReport.CacheMarkerSource = validation.CacheMarkerSource;
            caseReport.InputTokens = validation.InputTokens;
            caseReport.CachedInputTokens = validation.CachedInputTokens;
            caseReport.CacheWriteInputTokens = validation.CacheWriteInputTokens;
            caseReport.CacheHitRate = validation.CacheHitRate;
        }
        catch (Exception ex)
        {
            caseReport.Status = CompactSmokeStatuses.Failed;
            caseReport.ErrorMessage = ex.Message;
        }
        finally
        {
            stopwatch.Stop();
            caseReport.DurationMs = stopwatch.ElapsedMilliseconds;
        }

        return caseReport;
    }

    private async Task<string> RunManualSnapshotPartialAsync(
        CompactSmokeProviderSelection provider,
        string workspacePath)
    {
        await using var client = await AppServerClient.SpawnAsync(dotcraftBin, workspacePath);
        await client.InitializeAsync();
        var threadId = await StartThreadAsync(client, workspacePath, provider, "compact smoke manual snapshot");
        await RunTurnAsync(client, threadId, BuildCacheWarmupPrompt());
        await Task.Delay(CacheWarmupDelay);
        await RunTurnAsync(client, threadId, "Compact smoke manual snapshot setup. Reply with exactly: manual-ready.");
        await Task.Delay(CacheWarmupDelay);
        var compactResponse = await client.SendRequestAsync(
            DotCraft.Protocol.Contracts.AppServer.AppServerMethodNames.ThreadCompactStart,
            new { threadId },
            timeout: options.TurnTimeout);
        EnsureNoJsonRpcError(compactResponse, "thread/compact/start");
        EnsureCompactSucceeded(compactResponse);
        await client.StopAsync();
        return threadId;
    }

    private async Task<string> RunManualLegacyPartialAsync(
        CompactSmokeProviderSelection provider,
        string workspacePath)
    {
        string threadId;
        await using (var setupClient = await AppServerClient.SpawnAsync(dotcraftBin, workspacePath))
        {
            await setupClient.InitializeAsync();
            threadId = await StartThreadAsync(setupClient, workspacePath, provider, "compact smoke manual legacy");
            await RunSetupTurnsAsync(setupClient, threadId);
            await setupClient.StopAsync();
        }

        await using var compactClient = await AppServerClient.SpawnAsync(dotcraftBin, workspacePath);
        await compactClient.InitializeAsync();
        var compactResponse = await compactClient.SendRequestAsync(
            DotCraft.Protocol.Contracts.AppServer.AppServerMethodNames.ThreadCompactStart,
            new { threadId },
            timeout: options.TurnTimeout);
        EnsureNoJsonRpcError(compactResponse, "thread/compact/start");
        EnsureCompactSucceeded(compactResponse);
        await compactClient.StopAsync();
        return threadId;
    }

    private async Task<string> RunAutoSnapshotForkAsync(
        CompactSmokeProviderSelection provider,
        string workspacePath)
    {
        await using var client = await AppServerClient.SpawnAsync(dotcraftBin, workspacePath);
        await client.InitializeAsync();
        var threadId = await StartThreadAsync(client, workspacePath, provider, "compact smoke auto snapshot");
        await RunTurnAsync(client, threadId, BuildCacheWarmupPrompt());
        await Task.Delay(CacheWarmupDelay);
        await RunTurnAsync(client, threadId, "Compact smoke auto cache primer. Reply with exactly: primer.");
        await Task.Delay(CacheWarmupDelay);

        var notifications = await RunTurnAsync(client, threadId, BuildLargeAutoCompactPrompt());
        if (!ContainsSystemEvent(notifications, "compacting"))
            throw new InvalidOperationException("auto compact did not emit system/event kind=compacting.");
        if (!ContainsSystemEvent(notifications, "compacted"))
            throw new InvalidOperationException("auto compact did not emit system/event kind=compacted.");

        await client.StopAsync();
        return threadId;
    }

    private async Task RunSetupTurnsAsync(AppServerClient client, string threadId)
    {
        await RunTurnAsync(client, threadId, "Compact smoke setup turn one. Reply with exactly: setup-one.");
        await RunTurnAsync(client, threadId, "Compact smoke setup turn two. Reply with exactly: setup-two.");
    }

    private async Task<string> StartThreadAsync(
        AppServerClient client,
        string workspacePath,
        CompactSmokeProviderSelection provider,
        string displayName)
    {
        var threadResponse = await client.SendRequestAsync(DotCraft.Protocol.Contracts.AppServer.AppServerMethodNames.ThreadStart, new
        {
            identity = new
            {
                channelName = "appserver-compact-smoke",
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

    private async Task<IReadOnlyList<JsonDocument>> RunTurnAsync(
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

        var notifications = new List<JsonDocument>();
        var deadline = DateTimeOffset.UtcNow + options.TurnTimeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            var remaining = deadline - DateTimeOffset.UtcNow;
            var notification = await client.WaitForNotificationAsync(timeout: remaining);
            if (notification is null)
                break;

            notifications.Add(notification);
            if (!notification.RootElement.TryGetProperty("method", out var methodElement))
                continue;

            var method = methodElement.GetString();
            if (method == DotCraft.Protocol.Contracts.AppServer.AppServerMethodNames.TurnCompleted)
                return notifications;
            if (method is DotCraft.Protocol.Contracts.AppServer.AppServerMethodNames.TurnFailed or DotCraft.Protocol.Contracts.AppServer.AppServerMethodNames.TurnCancelled)
                throw new InvalidOperationException($"turn terminal failure: {ExtractNotificationMessage(notification)}");
        }

        throw new TimeoutException($"Timed out waiting for turn {turnId} to complete.");
    }

    private static string BuildLargeAutoCompactPrompt()
    {
        const int targetTokens = 22000;
        var seed = "compact smoke context pressure token block ";
        var chunks = new List<string>
        {
            "Reply with exactly: auto-compact-ok.\n",
            "The following repeated text exists only to trigger DotCraft pre-sampling compaction.\n"
        };

        while (MessageTokenEstimator.Estimate([new ChatMessage(ChatRole.User, string.Concat(chunks))]) < targetTokens)
            chunks.Add(seed);

        return string.Concat(chunks);
    }

    private static readonly TimeSpan CacheWarmupDelay = TimeSpan.FromSeconds(5);

    private static string BuildCacheWarmupPrompt()
    {
        const int targetTokens = 4000;
        var seed = "compact smoke cache warmup stable prefix block ";
        var chunks = new List<string>
        {
            "Compact smoke setup. Reply with exactly: setup.\n",
            "The following repeated text exists only to warm provider prompt cache below the auto-compact threshold.\n"
        };

        while (MessageTokenEstimator.Estimate([new ChatMessage(ChatRole.User, string.Concat(chunks))]) < targetTokens)
            chunks.Add(seed);

        return string.Concat(chunks);
    }

    private static bool ContainsSystemEvent(IEnumerable<JsonDocument> notifications, string kind)
    {
        foreach (var notification in notifications)
        {
            var root = notification.RootElement;
            if (!root.TryGetProperty("method", out var method) ||
                method.GetString() != DotCraft.Protocol.Contracts.AppServer.AppServerMethodNames.SystemEvent ||
                !root.TryGetProperty("params", out var parameters) ||
                !parameters.TryGetProperty("kind", out var kindElement))
            {
                continue;
            }

            if (string.Equals(kindElement.GetString(), kind, StringComparison.Ordinal))
                return true;
        }

        return false;
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

    private static void EnsureCompactSucceeded(JsonDocument response)
    {
        var result = response.RootElement.GetProperty("result");
        var outcome = result.TryGetProperty("outcome", out var outcomeElement)
            ? outcomeElement.GetString()
            : null;
        if (outcome == "partial" || outcome == "micro")
            return;

        var message = result.TryGetProperty("message", out var messageElement)
            ? messageElement.GetString()
            : null;
        throw new InvalidOperationException($"compact outcome={outcome ?? "<missing>"} message={message}");
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
}
