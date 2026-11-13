using System.Diagnostics;
using System.Text.Json;
using DotCraft.AppServer;

namespace DotCraft.AppServerTestClient;

internal sealed class DotNetExtensionSmokeRunner(string dotcraftBin, DotNetExtensionSmokeCliOptions options)
{
    private const string ConsumerId = "acme.review-consumer";
    private const string ProviderId = "acme.review-core";
    private const string DependencyBlocker = "PluginDependencyUnsatisfied";
    private const string ReviewStamp = "[reviewed by acme.review-core]";

    private readonly DotNetExtensionSmokeReport report = new();
    private string? workspacePath;
    private bool consumerGrantAdded;
    private bool providerGrantAdded;
    private bool consumerDataExisted;
    private bool providerDataExisted;

    public async Task<DotNetExtensionSmokeReport> RunAsync()
    {
        var stopwatch = Stopwatch.StartNew();
        Directory.CreateDirectory(options.WorkRoot);
        try
        {
            ValidateBundles();
            if (!DotNetExtensionSmokeProvider.TryResolve(options, out var provider, out var skipCode))
            {
                report.Status = DotNetExtensionSmokeStatuses.Skipped;
                report.Phase = "provider-selection";
                report.ErrorCode = skipCode;
                return report;
            }

            report.Protocol = provider.Protocol;
            report.ProviderId = provider.ProviderId;
            report.Model = provider.Model;
            workspacePath = DotNetExtensionSmokeWorkspace.Create(options.WorkRoot, provider);
            CapturePluginDataBaseline();

            await RunLifecycleAsync(provider);
            report.Status = DotNetExtensionSmokeStatuses.Passed;
            report.Phase = "complete";
        }
        catch (DotNetExtensionSmokeException exception)
        {
            report.Status = DotNetExtensionSmokeStatuses.Failed;
            report.Phase = exception.Phase;
            report.ErrorCode = exception.ErrorCode;
        }
        catch (Exception exception)
        {
            report.Status = DotNetExtensionSmokeStatuses.Failed;
            report.Phase = "unexpected";
            report.ErrorCode = StableExceptionCode(exception);
        }
        finally
        {
            if (workspacePath is not null)
            {
                var cleanupSucceeded = await CleanupAsync();
                report.CleanupIncomplete = !cleanupSucceeded;
                if (!cleanupSucceeded)
                {
                    report.Status = DotNetExtensionSmokeStatuses.Failed;
                    report.Phase = "cleanup";
                    report.ErrorCode ??= "cleanup_incomplete";
                }

                report.WorkspaceRetained = options.KeepWorkspace || !cleanupSucceeded;
                if (!report.WorkspaceRetained)
                {
                    try
                    {
                        await DotNetExtensionSmokeWorkspace.DeleteWorkspaceAsync(workspacePath);
                    }
                    catch
                    {
                        report.CleanupIncomplete = true;
                        report.WorkspaceRetained = true;
                        report.Status = DotNetExtensionSmokeStatuses.Failed;
                        report.Phase = "cleanup";
                        report.ErrorCode = "workspace_cleanup_failed";
                    }
                }
                if (cleanupSucceeded)
                {
                    try
                    {
                        DeleteNewPluginDataRoots();
                    }
                    catch
                    {
                        report.CleanupIncomplete = true;
                        report.Status = DotNetExtensionSmokeStatuses.Failed;
                        report.Phase = "cleanup";
                        report.ErrorCode = "plugin_data_cleanup_failed";
                    }
                }
            }

            stopwatch.Stop();
            report.FinishedAt = DateTimeOffset.UtcNow;
            report.DurationMs = stopwatch.ElapsedMilliseconds;
        }

        return report;
    }

    private async Task RunLifecycleAsync(DotNetExtensionSmokeProviderSelection provider)
    {
        report.Phase = "install";
        await using (var client = await AppServerClient.SpawnAsync(dotcraftBin, workspacePath))
        {
            var protocol = new DotNetExtensionSmokeProtocol(client);
            await protocol.InitializeAsync();
            var initial = await protocol.ListAsync();
            RequireNotInstalled(initial, ConsumerId, "initial-list");
            RequireNotInstalled(initial, ProviderId, "initial-list");

            var consumerInstall = await protocol.InstallLocalAsync(BundlePath(ConsumerId), ConsumerId);
            var consumer = consumerInstall.Plugin
                           ?? throw DotNetExtensionSmokeProtocol.Failure("install-consumer", "plugin_result_missing");
            DotNetExtensionSmokeProtocol.RequireBlockedBy(consumer, DependencyBlocker, "install-consumer");
            var consumerTrusted = consumer.TrustStatus == "trusted";

            var providerInstall = await protocol.InstallLocalAsync(BundlePath(ProviderId), ProviderId);
            var installedProvider = providerInstall.Plugin
                                    ?? throw DotNetExtensionSmokeProtocol.Failure("install-provider", "plugin_result_missing");
            var providerTrusted = installedProvider.TrustStatus == "trusted";
            var baseline = new DotNetExtensionTrustBaseline(consumerTrusted, providerTrusted);

            report.Phase = "trust";
            if (!baseline.ConsumerTrusted)
            {
                consumerGrantAdded = true;
                await protocol.SetTrustedAsync(ConsumerId, trusted: true);
                var blockedConsumer = DotNetExtensionSmokeProtocol.Require(
                    await protocol.ListAsync(), ConsumerId, "trust-consumer");
                DotNetExtensionSmokeProtocol.RequireBlockedBy(
                    blockedConsumer, DependencyBlocker, "trust-consumer");
            }

            if (!baseline.ProviderTrusted)
            {
                providerGrantAdded = true;
                await protocol.SetTrustedAsync(ProviderId, trusted: true);
            }

            RequireBothActive(await protocol.ListAsync(), "activation");
            await client.StopAsync();
        }

        report.Phase = "restart";
        await using (var client = await AppServerClient.SpawnAsync(dotcraftBin, workspacePath))
        {
            var protocol = new DotNetExtensionSmokeProtocol(client);
            await protocol.InitializeAsync();
            RequireBothActive(await protocol.ListAsync(), "restart");

            report.Phase = "model-turn";
            await RunModelTurnAsync(client, provider);

            report.Phase = "disable-enable";
            await protocol.SetEnabledAsync(ProviderId, enabled: false);
            var disabled = await protocol.ListAsync();
            var disabledProvider = DotNetExtensionSmokeProtocol.Require(disabled, ProviderId, "disable-provider");
            if (disabledProvider.Enabled)
                throw DotNetExtensionSmokeProtocol.Failure("disable-provider", "provider_still_enabled");
            var blockedConsumer = DotNetExtensionSmokeProtocol.Require(disabled, ConsumerId, "disable-provider");
            DotNetExtensionSmokeProtocol.RequireBlockedBy(blockedConsumer, DependencyBlocker, "disable-provider");

            await protocol.SetEnabledAsync(ProviderId, enabled: true);
            RequireBothActive(await protocol.ListAsync(), "reenable-provider");

            report.Phase = "restore-trust";
            if (providerGrantAdded)
            {
                await protocol.SetTrustedAsync(ProviderId, trusted: false);
                providerGrantAdded = false;
                var revoked = await protocol.ListAsync();
                DotNetExtensionSmokeProtocol.RequireBlockedBy(
                    DotNetExtensionSmokeProtocol.Require(revoked, ConsumerId, "revoke-provider"),
                    DependencyBlocker,
                    "revoke-provider");
            }
            if (consumerGrantAdded)
            {
                await protocol.SetTrustedAsync(ConsumerId, trusted: false);
                consumerGrantAdded = false;
            }

            report.Phase = "remove";
            await protocol.RemoveAsync(ConsumerId);
            await protocol.RemoveAsync(ProviderId);
            await client.StopAsync();
        }

        report.Phase = "post-remove-restart";
        await using (var client = await AppServerClient.SpawnAsync(dotcraftBin, workspacePath))
        {
            var protocol = new DotNetExtensionSmokeProtocol(client);
            await protocol.InitializeAsync();
            var final = await protocol.ListAsync();
            RequireNotInstalled(final, ConsumerId, "post-remove-restart");
            RequireNotInstalled(final, ProviderId, "post-remove-restart");
            await client.StopAsync();
        }
    }

    private async Task RunModelTurnAsync(
        AppServerClient client,
        DotNetExtensionSmokeProviderSelection provider)
    {
        var unexpectedServerRequest = false;
        client.ServerRequestHandler = request =>
        {
            _ = request;
            unexpectedServerRequest = true;
            return Task.FromResult<object?>(new { decision = "reject" });
        };

        using var threadResponse = await client.SendRequestAsync(
            DotCraft.Protocol.AppServer.AppServerMethodNames.ThreadStart,
            new
            {
                identity = new
                {
                    channelName = "appserver-dotnet-extension-smoke",
                    userId = "dotnet-extension-smoke",
                    workspacePath
                },
                config = new
                {
                    mode = "agent",
                    providerId = provider.ProviderId,
                    model = provider.Model,
                    toolAllowList = new[] { "review__normalize" },
                    pluginPolicy = new { allow = new[] { ConsumerId, ProviderId } },
                    mcpServers = Array.Empty<object>(),
                    approvalPolicy = "deny"
                },
                displayName = "managed plugin smoke"
            });
        EnsureSuccess(threadResponse, "thread_start_failed");
        var threadId = threadResponse.RootElement.GetProperty("result")
            .GetProperty("thread").GetProperty("id").GetString()!;

        var nonce = "PLUGIN-SMOKE-" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
        var input = $"  {nonce}  ";
        var prompt = $"""
            Call the `review.normalize` tool exactly once with `text` equal to `{input}`.
            Do not call any other tool. After the tool result, reply with the normalized token only.
            """;
        using var turnResponse = await client.SendRequestAsync(
            DotCraft.Protocol.AppServer.AppServerMethodNames.TurnStart,
            new { threadId, input = new[] { new { type = "text", text = prompt } } });
        EnsureSuccess(turnResponse, "turn_start_failed");
        var turnId = turnResponse.RootElement.GetProperty("result")
            .GetProperty("turn").GetProperty("id").GetString()!;

        var capture = new AppServerToolTurnCapture();
        var deadline = DateTimeOffset.UtcNow + options.TurnTimeout;
        var completed = false;
        while (DateTimeOffset.UtcNow < deadline)
        {
            using var notification = await client.WaitForNotificationAsync(
                timeout: deadline - DateTimeOffset.UtcNow);
            if (notification is null)
                break;
            if (!notification.RootElement.TryGetProperty("method", out var methodElement))
                continue;
            var method = methodElement.GetString();
            if (method == DotCraft.Protocol.AppServer.AppServerMethodNames.ItemCompleted)
                AppServerToolTurnCaptureReader.ReadCompletedItem(
                    notification.RootElement,
                    turnId,
                    capture);
            else if (method == DotCraft.Protocol.AppServer.AppServerMethodNames.TurnCompleted
                     && NotificationMatchesTurn(notification.RootElement, turnId))
            {
                AppServerToolTurnCaptureReader.ReadCompletedTurn(notification.RootElement, capture);
                completed = true;
                break;
            }
            else if (method is DotCraft.Protocol.AppServer.AppServerMethodNames.TurnFailed
                         or DotCraft.Protocol.AppServer.AppServerMethodNames.TurnCancelled
                     && NotificationMatchesTurn(notification.RootElement, turnId))
            {
                throw DotNetExtensionSmokeProtocol.Failure("model-turn", "turn_terminal_failure");
            }
        }

        if (!completed)
            throw DotNetExtensionSmokeProtocol.Failure("model-turn", "turn_timeout");
        if (unexpectedServerRequest)
            throw DotNetExtensionSmokeProtocol.Failure("model-turn", "unexpected_server_request");
        if (capture.Calls.Count != 1)
            throw DotNetExtensionSmokeProtocol.Failure("model-turn", "unexpected_tool_call_count");
        var call = capture.Calls[0];
        if (call.Namespace != "review"
            || call.ToolName != "normalize"
            || call.ProviderFlatName != "review__normalize"
            || call.PluginId != ConsumerId)
        {
            throw DotNetExtensionSmokeProtocol.Failure("model-turn", "unexpected_tool_provenance");
        }
        if (call.Arguments.GetProperty("text").GetString() != input)
            throw DotNetExtensionSmokeProtocol.Failure("model-turn", "tool_arguments_mismatch");
        if (!capture.Results.TryGetValue(call.CallId, out var result))
            throw DotNetExtensionSmokeProtocol.Failure("model-turn", "tool_result_missing");
        if (!result.Success)
            throw DotNetExtensionSmokeProtocol.Failure("model-turn", "tool_result_failed");
        if (!result.Result.Contains(nonce, StringComparison.Ordinal)
            || !result.Result.Contains(ReviewStamp, StringComparison.Ordinal))
        {
            throw DotNetExtensionSmokeProtocol.Failure("model-turn", "tool_result_mismatch");
        }
    }

    private async Task<bool> CleanupAsync()
    {
        if (workspacePath is null || !Directory.Exists(workspacePath))
            return true;
        try
        {
            await using var client = await AppServerClient.SpawnAsync(dotcraftBin, workspacePath);
            var protocol = new DotNetExtensionSmokeProtocol(client);
            await protocol.InitializeAsync();
            var snapshot = await protocol.ListAsync();

            if (providerGrantAdded && IsInstalled(snapshot, ProviderId))
                await protocol.SetTrustedAsync(ProviderId, trusted: false);
            providerGrantAdded = false;
            if (consumerGrantAdded && IsInstalled(await protocol.ListAsync(), ConsumerId))
                await protocol.SetTrustedAsync(ConsumerId, trusted: false);
            consumerGrantAdded = false;

            snapshot = await protocol.ListAsync();
            if (IsInstalled(snapshot, ConsumerId))
                await protocol.RemoveAsync(ConsumerId);
            snapshot = await protocol.ListAsync();
            if (IsInstalled(snapshot, ProviderId))
                await protocol.RemoveAsync(ProviderId);
            await client.StopAsync();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private void ValidateBundles()
    {
        foreach (var id in new[] { ConsumerId, ProviderId })
        {
            var manifest = Path.Combine(BundlePath(id), ".craft-plugin", "plugin.json");
            if (!File.Exists(manifest))
                throw DotNetExtensionSmokeProtocol.Failure("bundle-validation", "sample_bundle_missing");
        }
    }

    private string BundlePath(string pluginId) => Path.Combine(options.BundlesPath, pluginId);

    private static void RequireBothActive(PluginSnapshot snapshot, string phase)
    {
        DotNetExtensionSmokeProtocol.RequireState(
            DotNetExtensionSmokeProtocol.Require(snapshot, ProviderId, phase), "active", phase);
        DotNetExtensionSmokeProtocol.RequireState(
            DotNetExtensionSmokeProtocol.Require(snapshot, ConsumerId, phase), "active", phase);
    }

    private static void RequireNotInstalled(PluginSnapshot snapshot, string id, string phase)
    {
        if (snapshot.Plugins.TryGetValue(id, out var plugin) && plugin.Installed)
            throw DotNetExtensionSmokeProtocol.Failure(phase, "plugin_already_installed");
    }

    private static bool IsInstalled(PluginSnapshot snapshot, string id) =>
        snapshot.Plugins.TryGetValue(id, out var plugin) && plugin.Installed;

    private static bool NotificationMatchesTurn(JsonElement notification, string turnId) =>
        notification.TryGetProperty("params", out var parameters)
        && parameters.TryGetProperty("turn", out var turn)
        && turn.TryGetProperty("id", out var value)
        && value.GetString() == turnId;

    internal static bool TerminalNotificationMatchesTurn(JsonElement notification, string turnId) =>
        NotificationMatchesTurn(notification, turnId);

    private static void EnsureSuccess(JsonDocument response, string errorCode)
    {
        if (response.RootElement.TryGetProperty("error", out _))
            throw DotNetExtensionSmokeProtocol.Failure("model-turn", errorCode);
    }

    private static string StableExceptionCode(Exception exception) => exception switch
    {
        TimeoutException => "operation_timeout",
        IOException => "io_failure",
        UnauthorizedAccessException => "access_denied",
        _ => "unexpected_failure"
    };

    private void CapturePluginDataBaseline()
    {
        consumerDataExisted = Directory.Exists(GlobalPluginDataPath(ConsumerId));
        providerDataExisted = Directory.Exists(GlobalPluginDataPath(ProviderId));
    }

    private void DeleteNewPluginDataRoots()
    {
        DeleteIfCreated(GlobalPluginDataPath(ConsumerId), consumerDataExisted);
        DeleteIfCreated(GlobalPluginDataPath(ProviderId), providerDataExisted);
    }

    private static void DeleteIfCreated(string path, bool existed)
    {
        if (!existed && Directory.Exists(path))
            Directory.Delete(path, recursive: true);
    }

    private static string GlobalPluginDataPath(string pluginId) => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".craft",
        "plugins",
        pluginId);
}
