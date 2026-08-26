using System.Diagnostics;
using System.Text.Json;
using DotCraft.Configuration;

namespace DotCraft.AppServerTestClient;

internal sealed record DotNetPluginAuthoringBuildObservation(
    string? Outcome,
    string? State,
    string? Fingerprint);

internal sealed class DotNetPluginAuthoringSmokeRunner(
    string dotcraftBin,
    DotNetPluginAuthoringSmokeCliOptions options)
{
    internal const string PluginId = "smoke-agent-tool";
    internal const string ToolName = "smoke_agent_tool";

    private static readonly string[] AllowedTools =
    [
        "SkillView",
        "ReadFile",
        "WriteFile",
        "EditFile",
        "SearchTools",
        "DotNetPlugin__Inspect",
        "DotNetPlugin__Build",
        ToolName
    ];

    private readonly DotNetPluginSmokeReport report = new();
    private string? workspacePath;
    private bool pluginDataExisted;

    public async Task<DotNetPluginSmokeReport> RunAsync()
    {
        var stopwatch = Stopwatch.StartNew();
        Directory.CreateDirectory(options.WorkRoot);
        try
        {
            if (!DotNetPluginSmokeProvider.TryResolve(
                    options.WorkRoot,
                    options.ProviderId,
                    options.Model,
                    out var provider,
                    out var skipCode))
            {
                report.Status = DotNetPluginSmokeStatuses.Skipped;
                report.Phase = "provider-selection";
                report.ErrorCode = skipCode;
                return report;
            }

            report.Protocol = provider.Protocol;
            report.ProviderId = provider.ProviderId;
            report.Model = provider.Model;
            workspacePath = DotNetPluginSmokeWorkspace.Create(options.WorkRoot, provider);
            pluginDataExisted = Directory.Exists(GlobalPluginDataPath());

            await RunLifecycleAsync(provider);
            report.Status = DotNetPluginSmokeStatuses.Passed;
            report.Phase = "complete";
        }
        catch (DotNetPluginSmokeException exception)
        {
            report.Status = DotNetPluginSmokeStatuses.Failed;
            report.Phase = exception.Phase;
            report.ErrorCode = exception.ErrorCode;
        }
        catch (Exception exception)
        {
            report.Status = DotNetPluginSmokeStatuses.Failed;
            report.Phase = "unexpected";
            report.ErrorCode = StableExceptionCode(exception);
        }
        finally
        {
            if (workspacePath is not null)
                await CleanupAsync();
            stopwatch.Stop();
            report.FinishedAt = DateTimeOffset.UtcNow;
            report.DurationMs = stopwatch.ElapsedMilliseconds;
        }

        return report;
    }

    private async Task RunLifecycleAsync(DotNetPluginSmokeProviderSelection provider)
    {
        var v1 = "AUTHORING_SMOKE_V1_" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
        var v2 = "AUTHORING_SMOKE_V2_" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();

        await using var client = await AppServerClient.SpawnAsync(dotcraftBin, workspacePath);
        var unexpectedServerRequest = false;
        client.ServerRequestHandler = _ =>
        {
            unexpectedServerRequest = true;
            return Task.FromResult<object?>(new { decision = "reject" });
        };
        using (var initialize = await client.InitializeAsync(approvalSupport: true, streamingSupport: true))
            EnsureSuccess(initialize, "initialize", "initialize_failed");

        var verifyOpenAIRebuild = string.Equals(
            provider.Protocol,
            ModelProviderProtocols.OpenAIResponses,
            StringComparison.Ordinal);
        var threadId = await StartAuthoringThreadAsync(client, provider, verifyOpenAIRebuild ? "plan" : "agent");

        if (verifyOpenAIRebuild)
        {
            report.Phase = "discover-in-plan";
            var discovery = await RunTurnAsync(
                client,
                threadId,
                DiscoveryPrompt(),
                report.Phase,
                () => unexpectedServerRequest);
            ValidateDiscoveryTurn(discovery, report.Phase);

            using var modeResponse = await client.SendRequestAsync(
                DotCraft.Protocol.AppServer.AppServerMethodNames.ThreadModeSet,
                new { threadId, mode = "agent" });
            EnsureSuccess(modeResponse, "mode-switch", "mode_switch_failed");
        }

        report.Phase = "create-build-v1";
        var firstBuild = await RunTurnAsync(
            client,
            threadId,
            FirstBuildPrompt(v1, verifyOpenAIRebuild),
            report.Phase,
            () => unexpectedServerRequest);
        if (verifyOpenAIRebuild && firstBuild.Calls.Any(IsSearchCall))
            throw Failure(report.Phase, "tool_search_repeated_after_mode_switch");
        var firstFingerprint = ValidateBuildTurn(firstBuild, v1, requireCreatorSkill: true, oldFingerprint: null);

        report.Phase = "invoke-v1";
        var firstInvocationThread = await StartInvocationThreadAsync(client, provider, report.Phase);
        var firstInvocation = await RunTurnAsync(
            client,
            firstInvocationThread,
            InvocationPrompt(),
            report.Phase,
            () => unexpectedServerRequest);
        ValidatePluginInvocation(firstInvocation, v1, report.Phase);

        report.Phase = "hot-reload-v2";
        var secondBuild = await RunTurnAsync(
            client,
            threadId,
            ReloadPrompt(v1, v2),
            report.Phase,
            () => unexpectedServerRequest);
        ValidateBuildTurn(secondBuild, v2, requireCreatorSkill: false, oldFingerprint: firstFingerprint);

        report.Phase = "invoke-v2";
        var secondInvocationThread = await StartInvocationThreadAsync(client, provider, report.Phase);
        var secondInvocation = await RunTurnAsync(
            client,
            secondInvocationThread,
            InvocationPrompt(),
            report.Phase,
            () => unexpectedServerRequest);
        ValidatePluginInvocation(secondInvocation, v2, report.Phase);

        await client.StopAsync();
    }

    private async Task<string> StartAuthoringThreadAsync(
        AppServerClient client,
        DotNetPluginSmokeProviderSelection provider,
        string mode)
    {
        using var response = await client.SendRequestAsync(
            DotCraft.Protocol.AppServer.AppServerMethodNames.ThreadStart,
            new
            {
                identity = new
                {
                    channelName = "appserver-dotnet-plugin-authoring-smoke",
                    userId = "dotnet-plugin-authoring-smoke",
                    workspacePath
                },
                config = new
                {
                    mode,
                    providerId = provider.ProviderId,
                    model = provider.Model,
                    toolAllowList = AllowedTools,
                    pluginPolicy = new { allow = new[] { PluginId } },
                    skillsPolicy = new { allow = new[] { "plugin-creator" }, allowManage = false },
                    mcpServers = Array.Empty<object>(),
                    approvalPolicy = "interrupt",
                    requireApprovalOutsideWorkspace = false
                },
                displayName = "managed plugin authoring smoke"
            });
        EnsureSuccess(response, "thread-start", "thread_start_failed");
        return response.RootElement.GetProperty("result")
            .GetProperty("thread").GetProperty("id").GetString()!;
    }

    private async Task<string> StartInvocationThreadAsync(
        AppServerClient client,
        DotNetPluginSmokeProviderSelection provider,
        string phase)
    {
        using var response = await client.SendRequestAsync(
            DotCraft.Protocol.AppServer.AppServerMethodNames.ThreadStart,
            new
            {
                identity = new
                {
                    channelName = "appserver-dotnet-plugin-authoring-smoke",
                    userId = phase,
                    workspacePath
                },
                config = new
                {
                    mode = "agent",
                    providerId = provider.ProviderId,
                    model = provider.Model,
                    toolAllowList = new[] { ToolName },
                    pluginPolicy = new { allow = new[] { PluginId } },
                    mcpServers = Array.Empty<object>(),
                    approvalPolicy = "interrupt",
                    requireApprovalOutsideWorkspace = false
                },
                displayName = $"managed plugin authoring smoke {phase}"
            });
        EnsureSuccess(response, phase, "invocation_thread_start_failed");
        return response.RootElement.GetProperty("result")
            .GetProperty("thread").GetProperty("id").GetString()!;
    }

    private async Task<AppServerToolTurnCapture> RunTurnAsync(
        AppServerClient client,
        string threadId,
        string prompt,
        string phase,
        Func<bool> unexpectedServerRequest)
    {
        using var response = await client.SendRequestAsync(
            DotCraft.Protocol.AppServer.AppServerMethodNames.TurnStart,
            new { threadId, input = new[] { new { type = "text", text = prompt } } });
        EnsureSuccess(response, phase, "turn_start_failed");
        var turnId = response.RootElement.GetProperty("result")
            .GetProperty("turn").GetProperty("id").GetString()!;

        var capture = new AppServerToolTurnCapture();
        var deadline = DateTimeOffset.UtcNow + options.TurnTimeout;
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
            {
                AppServerToolTurnCaptureReader.ReadCompletedItem(
                    notification.RootElement,
                    turnId,
                    capture);
            }
            else if (method == DotCraft.Protocol.AppServer.AppServerMethodNames.TurnCompleted
                     && TerminalNotificationMatchesTurn(notification.RootElement, turnId))
            {
                AppServerToolTurnCaptureReader.ReadCompletedTurn(notification.RootElement, capture);
                if (unexpectedServerRequest())
                    throw Failure(phase, "unexpected_server_request");
                return capture;
            }
            else if (method is DotCraft.Protocol.AppServer.AppServerMethodNames.TurnFailed
                         or DotCraft.Protocol.AppServer.AppServerMethodNames.TurnCancelled
                     && TerminalNotificationMatchesTurn(notification.RootElement, turnId))
            {
                throw Failure(phase, "turn_terminal_failure");
            }
        }

        throw Failure(phase, "turn_timeout");
    }

    private string ValidateBuildTurn(
        AppServerToolTurnCapture capture,
        string expectedToken,
        bool requireCreatorSkill,
        string? oldFingerprint)
    {
        if (requireCreatorSkill)
        {
            var skillCall = RequireSingleCall(capture, null, "SkillView", report.Phase!);
            if (ReadString(skillCall.Arguments, "name") != "plugin-creator")
                throw Failure(report.Phase!, "creator_skill_not_loaded");
        }
        else if (!capture.Calls.Any(call => call.ToolName is "EditFile" or "WriteFile"))
        {
            throw Failure(report.Phase!, "source_not_edited");
        }

        var buildCall = RequireSingleCall(capture, "DotNetPlugin", "Build", report.Phase!);
        if (ReadString(buildCall.Arguments, "pluginId") != PluginId)
            throw Failure(report.Phase!, "build_plugin_id_mismatch");
        if (capture.Calls.Any(IsPluginToolCall))
            throw Failure(report.Phase!, "plugin_invoked_in_build_turn");

        var build = ParseBuildResult(capture, buildCall, report.Phase!);
        if (!string.Equals(build.Outcome, "built", StringComparison.Ordinal)
            || !string.Equals(build.State, "active", StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(build.Fingerprint))
        {
            throw Failure(report.Phase!, "build_not_active");
        }
        if (oldFingerprint is not null
            && string.Equals(build.Fingerprint, oldFingerprint, StringComparison.Ordinal))
        {
            throw Failure(report.Phase!, "fingerprint_not_changed");
        }

        var projectRoot = ProjectRoot();
        var manifestPath = Path.Combine(projectRoot, "plugin", ".craft-plugin", "plugin.json");
        var sourcePath = Path.Combine(projectRoot, "src", "Plugin.cs");
        if (!File.Exists(manifestPath) || !File.Exists(sourcePath))
            throw Failure(report.Phase!, "project_scaffold_missing");
        var markerPath = Path.Combine(
            workspacePath!,
            ".craft",
            "skills",
            "plugin-creator",
            ".builtin");
        if (!File.Exists(markerPath))
            throw Failure(report.Phase!, "creator_version_marker_missing");
        var hostVersion = File.ReadAllText(markerPath).Trim();
        using (var manifest = JsonDocument.Parse(File.ReadAllText(manifestPath)))
        {
            if (ReadString(manifest.RootElement, "id") != PluginId)
                throw Failure(report.Phase!, "manifest_plugin_id_mismatch");
            if (!manifest.RootElement.TryGetProperty("dotnet", out var dotnet)
                || ReadString(dotnet, "minHostVersion") != hostVersion)
            {
                throw Failure(report.Phase!, "manifest_host_version_mismatch");
            }
        }
        var source = File.ReadAllText(sourcePath);
        if (!source.Contains(expectedToken, StringComparison.Ordinal))
            throw Failure(report.Phase!, "source_version_mismatch");
        var libRoot = Path.Combine(projectRoot, "plugin", "lib");
        foreach (var fileName in new[]
                 {
                     "SmokeAgentTool.Plugin.dll",
                     "SmokeAgentTool.Plugin.deps.json"
                 })
        {
            if (!File.Exists(Path.Combine(libRoot, fileName)))
                throw Failure(report.Phase!, "build_output_missing");
        }
        if (Directory.EnumerateFiles(projectRoot, "*.csproj", SearchOption.AllDirectories).Any())
            throw Failure(report.Phase!, "unexpected_project_file");

        return build.Fingerprint!;
    }

    internal static void ValidatePluginInvocation(
        AppServerToolTurnCapture capture,
        string expectedToken,
        string phase)
    {
        var call = capture.Calls.Count == 1
            ? capture.Calls[0]
            : throw Failure(phase, "unexpected_tool_call_count");
        if (!IsPluginToolCall(call))
            throw Failure(phase, "unexpected_tool_provenance");
        if (call.Arguments.ValueKind != JsonValueKind.Object
            || call.Arguments.EnumerateObject().Any())
        {
            throw Failure(phase, "tool_arguments_mismatch");
        }
        if (!capture.Results.TryGetValue(call.CallId, out var result))
            throw Failure(phase, "tool_result_missing");
        if (!result.Success)
            throw Failure(phase, "tool_result_failed");
        if (!string.Equals(result.Result, expectedToken, StringComparison.Ordinal))
            throw Failure(phase, "tool_result_mismatch");
    }

    internal static DotNetPluginAuthoringBuildObservation ParseBuildResult(
        AppServerToolTurnCapture capture,
        AppServerToolCall call,
        string phase)
    {
        if (!capture.Results.TryGetValue(call.CallId, out var result))
            throw Failure(phase, "build_result_missing");
        if (!result.Success)
            throw Failure(phase, "build_tool_failed");

        try
        {
            using var document = JsonDocument.Parse(result.Result);
            var root = document.RootElement;
            return new DotNetPluginAuthoringBuildObservation(
                ReadString(root, "outcome"),
                ReadString(root, "state"),
                ReadString(root, "fingerprint"));
        }
        catch (JsonException)
        {
            throw Failure(phase, "build_result_invalid");
        }
    }

    private static AppServerToolCall RequireSingleCall(
        AppServerToolTurnCapture capture,
        string? @namespace,
        string name,
        string phase)
    {
        var matches = capture.Calls.Where(call =>
            string.Equals(call.Namespace, @namespace, StringComparison.Ordinal)
            && string.Equals(call.ToolName, name, StringComparison.Ordinal)).ToArray();
        return matches.Length == 1
            ? matches[0]
            : throw Failure(phase, $"{name.ToLowerInvariant()}_call_count");
    }

    private static bool IsPluginToolCall(AppServerToolCall call) =>
        string.IsNullOrEmpty(call.Namespace)
        && call.ToolName == ToolName
        && call.ProviderFlatName == ToolName
        && call.PluginId == PluginId;

    private static bool IsSearchCall(AppServerToolCall call) =>
        string.IsNullOrEmpty(call.Namespace)
        && call.ToolName == "SearchTools";

    private static void ValidateDiscoveryTurn(AppServerToolTurnCapture capture, string phase)
    {
        _ = RequireSingleCall(capture, null, "SearchTools", phase);
        var inspect = RequireSingleCall(capture, "DotNetPlugin", "Inspect", phase);
        if (!capture.Results.TryGetValue(inspect.CallId, out var result) || !result.Success)
            throw Failure(phase, "inspect_failed");
        if (capture.Calls.Any(call => call.Namespace == "DotNetPlugin" && call.ToolName == "Build"))
            throw Failure(phase, "unexpected_build_call");
    }

    private static string DiscoveryPrompt() => """
        This is an isolated OpenAI deferred-tool rebuild smoke test. Use tools; do not answer from memory.
        Call `SearchTools` exactly once with query `DotNetPlugin` and max_results 5.
        Then call the discovered `DotNetPlugin.Inspect` exactly once with query `IDotCraftPlugin`.
        Do not call `DotNetPlugin.Build`, do not edit files, and stop after the Inspect result.
        """;

    private static string FirstBuildPrompt(string token, bool reuseDiscoveredTools)
    {
        var deferredInstruction = reuseDiscoveredTools
            ? "The DotNetPlugin tools were discovered in the previous Turn. Do not call SearchTools again."
            : "Search for the deferred DotNetPlugin tools in this Turn.";
        return $"""
        This is an isolated .NET plugin authoring smoke test. Use tools; do not answer from memory.
        First load the built-in `plugin-creator` skill with SkillView and follow its managed .NET workflow.
        Do not use a shell. Read the deployed creator reference or scaffold script when you need the current template.
        With workspace file tools, create exactly `.craft/plugin-projects/{PluginId}` with `src/Plugin.cs`
        and `plugin/.craft-plugin/plugin.json`. The manifest id must be `{PluginId}`, its entry assembly must be
        `./lib/SmokeAgentTool.Plugin.dll`, and its entry type must be `DotCraft.Plugin.SmokeAgentTool.Plugin`.
        Adapt the creator's current minimal managed Tool template so it contributes one top-level tool named
        `{ToolName}` with an empty object schema and returns exactly `{token}`.
        Use DotNetPlugin.Inspect if an API signature is unclear.
        {deferredInstruction}
        Call DotNetPlugin.Build exactly once with pluginId `{PluginId}`. Do not invoke `{ToolName}` in this Turn.
        Stop after the build result.
        """;
    }

    private static string ReloadPrompt(string oldToken, string newToken) => $"""
        Hot reload the existing managed plugin. Use EditFile on
        `.craft/plugin-projects/{PluginId}/src/Plugin.cs` to replace exactly `{oldToken}` with `{newToken}`.
        Do not change the manifest or Tool schema. Search for the deferred DotNetPlugin tools in this Turn,
        then call DotNetPlugin.Build exactly once with pluginId `{PluginId}`.
        Do not invoke `{ToolName}` in this Turn. Stop after the build result.
        """;

    private static string InvocationPrompt() => $"""
        Call `{ToolName}` exactly once with an empty object. Do not call any other tool and do not answer from memory.
        Reply with exactly the Tool result and no extra words.
        """;

    private async Task CleanupAsync()
    {
        var cleanupFailed = false;
        if (!pluginDataExisted)
        {
            try
            {
                var pluginDataPath = GlobalPluginDataPath();
                if (Directory.Exists(pluginDataPath))
                    Directory.Delete(pluginDataPath, recursive: true);
            }
            catch
            {
                cleanupFailed = true;
            }
        }

        report.WorkspaceRetained = options.KeepWorkspace || cleanupFailed;
        if (!report.WorkspaceRetained)
        {
            try
            {
                await DotNetPluginSmokeWorkspace.DeleteWorkspaceAsync(workspacePath!);
            }
            catch
            {
                cleanupFailed = true;
                report.WorkspaceRetained = true;
            }
        }

        report.CleanupIncomplete = cleanupFailed;
        if (cleanupFailed)
        {
            report.Status = DotNetPluginSmokeStatuses.Failed;
            report.Phase = "cleanup";
            report.ErrorCode = "cleanup_incomplete";
        }
    }

    private string ProjectRoot() => Path.Combine(
        workspacePath!,
        ".craft",
        "plugin-projects",
        PluginId);

    private static string GlobalPluginDataPath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".craft",
        "plugins",
        PluginId);

    private static string? ReadString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value)
            || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }
        return value.GetString();
    }

    private static bool TerminalNotificationMatchesTurn(JsonElement notification, string turnId) =>
        notification.TryGetProperty("params", out var parameters)
        && parameters.TryGetProperty("turn", out var turn)
        && turn.TryGetProperty("id", out var id)
        && id.GetString() == turnId;

    private static void EnsureSuccess(JsonDocument response, string phase, string errorCode)
    {
        if (response.RootElement.TryGetProperty("error", out _))
            throw Failure(phase, errorCode);
    }

    private static DotNetPluginSmokeException Failure(string phase, string errorCode) =>
        new(phase, errorCode);

    private static string StableExceptionCode(Exception exception) => exception switch
    {
        TimeoutException => "operation_timeout",
        IOException => "io_failure",
        UnauthorizedAccessException => "access_denied",
        JsonException => "invalid_json",
        _ => "unexpected_failure"
    };

}
