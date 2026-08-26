using System.Text.Json;
using DotCraft.AppServerTestClient;
using DotCraft.Configuration;
using Xunit;

namespace DotCraft.Tests.AppServerTestClient;

public sealed class DotNetPluginSmokeTests
{
    [Fact]
    public void CliOptions_UseTemporaryRunRootAndAllowProviderOverrides()
    {
        var bundles = Path.Combine(Path.GetTempPath(), "plugin-smoke-bundles");

        var ok = DotNetPluginSmokeCliOptions.TryParse(
            ["--bundles", bundles, "--provider-id", "local", "--model", "model-test"],
            out var options,
            out var error);

        Assert.True(ok, error);
        Assert.Equal(Path.GetFullPath(bundles), options.BundlesPath);
        Assert.Equal("local", options.ProviderId);
        Assert.Equal("model-test", options.Model);
        Assert.StartsWith(
            Path.Combine(Path.GetTempPath(), "dotcraft-plugin-smoke") + Path.DirectorySeparatorChar,
            options.WorkRoot,
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
        Assert.Equal(Path.Combine(options.WorkRoot, "report.json"), options.ReportPath);
        Assert.Equal(TimeSpan.FromMinutes(5), options.TurnTimeout);
        Assert.False(options.KeepWorkspace);
    }

    [Fact]
    public void CliOptions_RejectMissingBundles()
    {
        var ok = DotNetPluginSmokeCliOptions.TryParse([], out _, out var error);

        Assert.False(ok);
        Assert.Equal("missing_bundles", error);
    }

    [Fact]
    public void BuildConfigJson_SelectsProviderWithoutCopyingCredentialsOrExternalIntegrations()
    {
        var json = DotNetPluginSmokeWorkspace.BuildConfigJson("local", "model-test");
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.Equal("local", root.GetProperty("ProviderId").GetString());
        Assert.Equal(
            "model-test",
            root.GetProperty("ProviderPreferences").GetProperty("local").GetProperty("model").GetString());
        Assert.False(root.TryGetProperty("Providers", out _));
        Assert.False(json.Contains("ApiKey", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(root.GetProperty("McpServers").EnumerateObject());
        Assert.Empty(root.GetProperty("LspServers").EnumerateObject());
        Assert.Empty(root.GetProperty("ExternalChannels").EnumerateArray());
        Assert.False(root.GetProperty("Tracing").GetProperty("Enabled").GetBoolean());
        Assert.False(root.GetProperty("Compaction").GetProperty("AutoCompactEnabled").GetBoolean());
    }

    [Fact]
    public async Task DeleteWorkspaceAsync_RemovesAnIsolatedWorkspace()
    {
        var root = Path.Combine(Path.GetTempPath(), "dotnet-plugin-smoke-delete-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, ".craft"));
        await File.WriteAllTextAsync(Path.Combine(root, ".craft", "state.db"), "synthetic");

        await DotNetPluginSmokeWorkspace.DeleteWorkspaceAsync(root);

        Assert.False(Directory.Exists(root));
    }

    [Fact]
    public void ResolveModel_UsesOAuthRuntimeDefaultOnlyWhenNoPreferenceExists()
    {
        var config = new AppConfig();

        Assert.Equal(
            ModelProviderDefaults.DefaultChatGptCodexModel,
            DotNetPluginSmokeProvider.ResolveModel(
                config,
                "local",
                explicitModel: null,
                ModelProviderAuthMethods.ChatGptOAuth));
        Assert.Null(DotNetPluginSmokeProvider.ResolveModel(
            config,
            "local",
            explicitModel: null,
            ModelProviderAuthMethods.ApiKey));
    }

    [Fact]
    public void ReportJson_OmitsPathsAndExceptionMessages()
    {
        var report = new DotNetPluginSmokeReport
        {
            Status = DotNetPluginSmokeStatuses.Failed,
            Protocol = "openai-responses",
            ProviderId = "local",
            Model = "model-test",
            Phase = "model-turn",
            ErrorCode = "tool_result_missing",
            CleanupIncomplete = false,
            WorkspaceRetained = false
        };

        var json = JsonSerializer.Serialize(report, DotNetPluginSmokeJson.Options);

        Assert.Contains("tool_result_missing", json, StringComparison.Ordinal);
        Assert.DoesNotContain("workspacePath", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("workRoot", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("prompt", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("toolResult", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("errorMessage", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReportExitCode_DistinguishesPassFailureAndNoProvider()
    {
        Assert.Equal(0, new DotNetPluginSmokeReport { Status = DotNetPluginSmokeStatuses.Passed }.ExitCode);
        Assert.Equal(1, new DotNetPluginSmokeReport { Status = DotNetPluginSmokeStatuses.Failed }.ExitCode);
        Assert.Equal(2, new DotNetPluginSmokeReport { Status = DotNetPluginSmokeStatuses.Skipped }.ExitCode);
    }

    [Fact]
    public void SnapshotNotification_RequiresRootAndAffectedPluginIds()
    {
        using var complete = JsonDocument.Parse("""
            { "snapshotRevision": 8, "pluginIds": ["acme.review-core", "acme.review-consumer"] }
            """);
        DotNetPluginSmokeProtocol.ValidateSnapshotNotification(
            complete.RootElement,
            8,
            "acme.review-core",
            ["acme.review-consumer"],
            "test");

        using var incomplete = JsonDocument.Parse("""
            { "snapshotRevision": 8, "pluginIds": ["acme.review-core"] }
            """);
        var exception = Assert.Throws<DotNetPluginSmokeException>(() =>
            DotNetPluginSmokeProtocol.ValidateSnapshotNotification(
                incomplete.RootElement,
                8,
                "acme.review-core",
                ["acme.review-consumer"],
                "test"));
        Assert.Equal("plugin_snapshot_ids_incomplete", exception.ErrorCode);
    }

    [Fact]
    public void TurnCapture_ValidatesPluginProvenanceAndPairsResult()
    {
        var capture = new AppServerToolTurnCapture();
        using var call = JsonDocument.Parse("""
            {
              "params": {
                "turnId": "turn-1",
                "item": {
                  "type": "toolCall",
                  "payload": {
                    "namespace": "review",
                    "toolName": "normalize",
                    "providerFlatName": "review__normalize",
                    "source": { "pluginId": "acme.review-consumer" },
                    "callId": "call-1",
                    "arguments": { "text": "  synthetic  " }
                  }
                }
              }
            }
            """);
        using var result = JsonDocument.Parse("""
            {
              "params": {
                "turnId": "turn-1",
                "item": {
                  "type": "toolResult",
                  "payload": {
                    "callId": "call-1",
                    "success": true,
                    "result": "synthetic\n[reviewed by acme.review-core]"
                  }
                }
              }
            }
            """);

        AppServerToolTurnCaptureReader.ReadCompletedItem(call.RootElement, "turn-1", capture);
        AppServerToolTurnCaptureReader.ReadCompletedItem(result.RootElement, "turn-1", capture);

        var captured = Assert.Single(capture.Calls);
        Assert.Equal("call-1", captured.CallId);
        Assert.Equal("  synthetic  ", captured.Arguments.GetProperty("text").GetString());
        Assert.Equal(
            "synthetic\n[reviewed by acme.review-core]",
            capture.Results["call-1"].Result);
    }

    [Fact]
    public void TerminalNotificationMatchesTurn_ReadsNestedTurnId()
    {
        using var notification = JsonDocument.Parse("""
            {
              "jsonrpc": "2.0",
              "method": "turn/completed",
              "params": { "turn": { "id": "turn-1", "status": "completed" } }
            }
            """);

        Assert.True(DotNetPluginSmokeRunner.TerminalNotificationMatchesTurn(
            notification.RootElement,
            "turn-1"));
        Assert.False(DotNetPluginSmokeRunner.TerminalNotificationMatchesTurn(
            notification.RootElement,
            "turn-2"));
    }

    [Fact]
    public void ReadCompletedTurn_UsesAuthoritativeItemSnapshot()
    {
        using var notification = JsonDocument.Parse("""
            {
              "params": {
                "turn": {
                  "items": [
                    {
                      "type": "toolCall",
                      "payload": {
                        "namespace": "review",
                        "toolName": "normalize",
                        "providerFlatName": "review__normalize",
                        "source": { "pluginId": "acme.review-consumer" },
                        "callId": "call-1",
                        "arguments": { "text": "  synthetic  " }
                      }
                    },
                    {
                      "type": "toolResult",
                      "payload": {
                        "callId": "call-1",
                        "success": true,
                        "result": "synthetic\n[reviewed by acme.review-core]"
                      }
                    }
                  ]
                }
              }
            }
            """);
        var capture = new AppServerToolTurnCapture();
        capture.Calls.Add(new AppServerToolCall(
            "stale",
            null,
            "stale",
            "stale",
            null,
            JsonSerializer.SerializeToElement(new { })));
        capture.Results["stale"] = new AppServerToolResult(true, "stale");

        AppServerToolTurnCaptureReader.ReadCompletedTurn(notification.RootElement, capture);

        var captured = Assert.Single(capture.Calls);
        Assert.Equal("call-1", captured.CallId);
        Assert.Equal("  synthetic  ", captured.Arguments.GetProperty("text").GetString());
        Assert.Equal(
            "synthetic\n[reviewed by acme.review-core]",
            capture.Results["call-1"].Result);
        Assert.False(capture.Results.ContainsKey("stale"));
    }
}
