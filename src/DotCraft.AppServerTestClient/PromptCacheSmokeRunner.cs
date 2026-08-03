using System.Diagnostics;
using System.Text;
using System.Text.Json;
using DotCraft.Auth.OpenAI;
using DotCraft.Configuration;
using DotCraft.Protocol.AppServer;

namespace DotCraft.AppServerTestClient;

internal sealed class PromptCacheSmokeRunner(string dotcraftBin, PromptCacheSmokeCliOptions options)
{
    private static readonly TimeSpan CacheWarmupDelay = TimeSpan.FromSeconds(5);

    public async Task<PromptCacheSmokeReport> RunAsync(PromptCacheSmokeMatrix matrix)
    {
        Directory.CreateDirectory(options.WorkRoot);
        var report = new PromptCacheSmokeReport
        {
            StartedAt = DateTimeOffset.UtcNow,
            WorkRoot = options.WorkRoot
        };

        foreach (var skip in BuildUnsupportedProtocolSkips(matrix))
            report.Cases.Add(skip);

        foreach (var protocol in PromptCacheSmokeJson.SupportedProtocols)
        {
            var providerCase = FindProviderCase(matrix, protocol);
            if (providerCase is null)
            {
                report.Cases.Add(PromptCacheSmokeCaseReport.Skipped(
                    protocol,
                    string.Empty,
                    string.Empty,
                    "missing_protocol_mapping"));
                continue;
            }

            var selection = new PromptCacheSmokeProviderSelection(
                protocol,
                providerCase.ProviderId.Trim(),
                providerCase.Model.Trim(),
                0);
            var skipReason = ValidateSelection(providerCase, selection, out var validatedSelection);
            if (skipReason is not null)
            {
                report.Cases.Add(PromptCacheSmokeCaseReport.Skipped(
                    selection.Protocol,
                    selection.ProviderId,
                    selection.Model,
                    skipReason));
                continue;
            }

            var caseReport = await RunProviderAsync(validatedSelection);
            report.Cases.Add(caseReport);
            Console.Error.WriteLine(
                $"[prompt-cache-smoke] {validatedSelection.Protocol}/{PromptCacheSmokeScenarios.PromptCacheBaseline}: {caseReport.Status} {caseReport.Message ?? caseReport.ErrorMessage}");
        }

        report.FinalizeSummary(DateTimeOffset.UtcNow);
        return report;
    }

    private static IEnumerable<PromptCacheSmokeCaseReport> BuildUnsupportedProtocolSkips(
        PromptCacheSmokeMatrix matrix)
    {
        foreach (var provider in matrix.Providers)
        {
            if (TryNormalizeProtocol(provider.Protocol, out _))
                continue;

            yield return PromptCacheSmokeCaseReport.Skipped(
                provider.Protocol,
                provider.ProviderId,
                provider.Model,
                "unsupported_protocol");
        }
    }

    private static PromptCacheSmokeProviderCase? FindProviderCase(PromptCacheSmokeMatrix matrix, string protocol)
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
        PromptCacheSmokeProviderCase providerCase,
        PromptCacheSmokeProviderSelection selection,
        out PromptCacheSmokeProviderSelection validatedSelection)
    {
        validatedSelection = selection;
        if (string.IsNullOrWhiteSpace(selection.ProviderId))
            return "missing_provider_id";
        if (string.IsNullOrWhiteSpace(selection.Model))
            return "missing_model";
        if (providerCase.MinimumCacheHitRate is < 0 or > 1 || double.IsNaN(providerCase.MinimumCacheHitRate ?? 0))
            return "invalid_minimum_cache_hit_rate";

        var tempConfigPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), ".craft", "config.json");
        Directory.CreateDirectory(Path.GetDirectoryName(tempConfigPath)!);
        try
        {
            File.WriteAllText(tempConfigPath, PromptCacheSmokeWorkspace.BuildConfigJson(selection.ProviderId, selection.Model));
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
            var minimumCacheHitRate = providerCase.MinimumCacheHitRate
                                      ?? ResolveDefaultMinimumCacheHitRate(selection.Protocol, authMethod);
            validatedSelection = selection with { MinimumCacheHitRate = minimumCacheHitRate };
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

    private static double ResolveDefaultMinimumCacheHitRate(string protocol, string authMethod)
    {
        return protocol switch
        {
            ModelProviderProtocols.OpenAIChatCompletions => 0.50,
            ModelProviderProtocols.OpenAIResponses
                when string.Equals(authMethod, ModelProviderAuthMethods.ChatGptOAuth, StringComparison.Ordinal) => 0.35,
            ModelProviderProtocols.OpenAIResponses => 0.30,
            ModelProviderProtocols.Anthropic => 0.50,
            _ => 0.01
        };
    }

    private async Task<PromptCacheSmokeCaseReport> RunProviderAsync(
        PromptCacheSmokeProviderSelection provider)
    {
        var stopwatch = Stopwatch.StartNew();
        var workspacePath = PromptCacheSmokeWorkspace.Create(options.WorkRoot, provider);
        var traceDbPath = PromptCacheSmokeWorkspace.TraceDbPath(workspacePath);
        var caseReport = new PromptCacheSmokeCaseReport
        {
            Protocol = provider.Protocol,
            ProviderId = provider.ProviderId,
            Model = provider.Model,
            MinimumCacheHitRate = provider.MinimumCacheHitRate,
            WorkspacePath = workspacePath,
            TraceDbPath = traceDbPath
        };

        try
        {
            var threadId = await RunPromptCacheBaselineAsync(provider, workspacePath);
            caseReport.ThreadId = threadId;
            var events = PromptCacheSmokeTraceReader.ReadThreadEvents(traceDbPath, threadId);
            var validation = PromptCacheSmokeTraceValidator.Validate(events, provider.MinimumCacheHitRate);
            caseReport.Status = validation.Success ? PromptCacheSmokeStatuses.Passed : PromptCacheSmokeStatuses.Failed;
            caseReport.Message = validation.Message;
            caseReport.CacheHitRequired = validation.CacheHitRequired;
            caseReport.CacheHit = validation.CacheHit;
            caseReport.MinimumCacheHitRate = validation.MinimumCacheHitRate;
            caseReport.InputTokens = validation.InputTokens;
            caseReport.CachedInputTokens = validation.CachedInputTokens;
            caseReport.CacheWriteInputTokens = validation.CacheWriteInputTokens;
            caseReport.CacheHitRate = validation.CacheHitRate;
            caseReport.ContextCompactionCount = validation.ContextCompactionCount;
        }
        catch (Exception ex)
        {
            caseReport.Status = PromptCacheSmokeStatuses.Failed;
            caseReport.ErrorMessage = ex.Message;
        }
        finally
        {
            stopwatch.Stop();
            caseReport.DurationMs = stopwatch.ElapsedMilliseconds;
        }

        return caseReport;
    }

    private async Task<string> RunPromptCacheBaselineAsync(
        PromptCacheSmokeProviderSelection provider,
        string workspacePath)
    {
        var sourcePath = Path.Combine(workspacePath, "notes.txt");
        await File.WriteAllTextAsync(sourcePath, BuildPromptCacheBaselineNotes());

        await using var client = await AppServerClient.SpawnAsync(dotcraftBin, workspacePath);
        await client.InitializeAsync();
        var threadId = await StartThreadAsync(client, workspacePath, provider, "prompt cache baseline");

        await RunTurnAsync(client, threadId, "Use ReadFile on notes.txt and reply with exactly: read-one.");
        await Task.Delay(CacheWarmupDelay);
        await RunTurnAsync(client, threadId, "Use ReadFile on notes.txt again and reply with exactly: read-two.");
        await Task.Delay(CacheWarmupDelay);
        await RunTurnAsync(client, threadId, "Use ReadFile on notes.txt once more and reply with exactly: read-three.");

        await client.StopAsync();
        return threadId;
    }

    private static string BuildPromptCacheBaselineNotes()
    {
        const int targetChars = 30_000;
        var header =
            "DotCraft prompt cache baseline notes.\n" +
            "These lines exist purely to inflate the ReadFile tool result so the agent's\n" +
            "cumulative context crosses provider-specific prompt-cache thresholds.\n" +
            "Stable phrases below repeat verbatim across turns to keep the prefix bytes\n" +
            "byte-identical between requests, the canonical condition for cache hits.\n";
        var lines = new[]
        {
            "Line A: hot cake mango papaya quartz vivid amber breeze cinder dawn ember frost.\n",
            "Line B: glade harbor ivory jonquil kindle lumen marsh nectar opal prism quill rune.\n",
            "Line C: silver tundra umber vista willow xeric yam zither cobalt dune ember fjord.\n",
            "Line D: arctic boreal coastal delta estuary forest gulf highland island junction.\n",
            "Line E: knoll lagoon meadow oasis prairie reef savanna terrace upland valley wetland.\n"
        };
        var builder = new StringBuilder(targetChars + 256);
        builder.Append(header);
        var index = 0;
        while (builder.Length < targetChars)
        {
            builder.Append(lines[index % lines.Length]);
            index++;
        }
        builder.Append("End.\n");
        return builder.ToString();
    }

    private async Task<string> StartThreadAsync(
        AppServerClient client,
        string workspacePath,
        PromptCacheSmokeProviderSelection provider,
        string displayName)
    {
        var threadResponse = await client.SendRequestAsync(DotCraft.Protocol.AppServer.AppServerMethodNames.ThreadStart, new
        {
            identity = new
            {
                channelName = "appserver-prompt-cache-smoke",
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
        var turnResponse = await client.SendRequestAsync(DotCraft.Protocol.AppServer.AppServerMethodNames.TurnStart, new
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
            if (method == DotCraft.Protocol.AppServer.AppServerMethodNames.TurnCompleted)
                return;
            if (method is DotCraft.Protocol.AppServer.AppServerMethodNames.TurnFailed or DotCraft.Protocol.AppServer.AppServerMethodNames.TurnCancelled)
                throw new InvalidOperationException($"turn terminal failure: {ExtractNotificationMessage(notification)}");
        }

        throw new TimeoutException($"Timed out waiting for turn {turnId} to complete.");
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
