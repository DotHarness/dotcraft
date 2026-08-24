using System.Text.Json;
using DotCraft.AppServerTestClient;
using DotCraft.Configuration;
using Xunit;

namespace DotCraft.Tests.AppServerTestClient;

public sealed class DotnetPluginSmokeTests
{
    [Fact]
    public void CliOptions_UseTemporaryRunRootAndAllowProviderOverrides()
    {
        var bundles = Path.Combine(Path.GetTempPath(), "plugin-smoke-bundles");

        var ok = DotnetPluginSmokeCliOptions.TryParse(
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
        var ok = DotnetPluginSmokeCliOptions.TryParse([], out _, out var error);

        Assert.False(ok);
        Assert.Equal("missing_bundles", error);
    }

    [Fact]
    public void BuildConfigJson_SelectsProviderWithoutCopyingCredentialsOrExternalIntegrations()
    {
        var json = DotnetPluginSmokeWorkspace.BuildConfigJson("local", "model-test");
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

        await DotnetPluginSmokeWorkspace.DeleteWorkspaceAsync(root);

        Assert.False(Directory.Exists(root));
    }

    [Fact]
    public void ResolveModel_UsesOAuthRuntimeDefaultOnlyWhenNoPreferenceExists()
    {
        var config = new AppConfig();

        Assert.Equal(
            ModelProviderDefaults.DefaultChatGptCodexModel,
            DotnetPluginSmokeProvider.ResolveModel(
                config,
                "local",
                explicitModel: null,
                ModelProviderAuthMethods.ChatGptOAuth));
        Assert.Null(DotnetPluginSmokeProvider.ResolveModel(
            config,
            "local",
            explicitModel: null,
            ModelProviderAuthMethods.ApiKey));
    }

    [Fact]
    public void ReportJson_OmitsPathsAndExceptionMessages()
    {
        var report = new DotnetPluginSmokeReport
        {
            Status = DotnetPluginSmokeStatuses.Failed,
            Protocol = "openai-responses",
            ProviderId = "local",
            Model = "model-test",
            Phase = "model-turn",
            ErrorCode = "tool_result_missing",
            CleanupIncomplete = false,
            WorkspaceRetained = false
        };

        var json = JsonSerializer.Serialize(report, DotnetPluginSmokeJson.Options);

        Assert.Contains("tool_result_missing", json, StringComparison.Ordinal);
        Assert.DoesNotContain("workspacePath", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("workRoot", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("errorMessage", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReportExitCode_DistinguishesPassFailureAndNoProvider()
    {
        Assert.Equal(0, new DotnetPluginSmokeReport { Status = DotnetPluginSmokeStatuses.Passed }.ExitCode);
        Assert.Equal(1, new DotnetPluginSmokeReport { Status = DotnetPluginSmokeStatuses.Failed }.ExitCode);
        Assert.Equal(2, new DotnetPluginSmokeReport { Status = DotnetPluginSmokeStatuses.Skipped }.ExitCode);
    }

    [Fact]
    public void SnapshotNotification_RequiresRootAndAffectedPluginIds()
    {
        using var complete = JsonDocument.Parse("""
            { "snapshotRevision": 8, "pluginIds": ["acme.review-core", "acme.review-consumer"] }
            """);
        DotnetPluginSmokeProtocol.ValidateSnapshotNotification(
            complete.RootElement,
            8,
            "acme.review-core",
            ["acme.review-consumer"],
            "test");

        using var incomplete = JsonDocument.Parse("""
            { "snapshotRevision": 8, "pluginIds": ["acme.review-core"] }
            """);
        var exception = Assert.Throws<DotnetPluginSmokeException>(() =>
            DotnetPluginSmokeProtocol.ValidateSnapshotNotification(
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
        var calls = new List<(string CallId, string Text)>();
        var results = new Dictionary<string, string>();
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

        DotnetPluginSmokeRunner.ReadCompletedItem(call.RootElement, "turn-1", calls, results);
        DotnetPluginSmokeRunner.ReadCompletedItem(result.RootElement, "turn-1", calls, results);

        var captured = Assert.Single(calls);
        Assert.Equal("call-1", captured.CallId);
        Assert.Equal("  synthetic  ", captured.Text);
        Assert.Equal("synthetic\n[reviewed by acme.review-core]", results["call-1"]);
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

        Assert.True(DotnetPluginSmokeRunner.TerminalNotificationMatchesTurn(
            notification.RootElement,
            "turn-1"));
        Assert.False(DotnetPluginSmokeRunner.TerminalNotificationMatchesTurn(
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
        var calls = new List<(string CallId, string Text)> { ("stale", "stale") };
        var results = new Dictionary<string, string> { ["stale"] = "stale" };

        DotnetPluginSmokeRunner.ReadCompletedTurn(notification.RootElement, calls, results);

        var captured = Assert.Single(calls);
        Assert.Equal("call-1", captured.CallId);
        Assert.Equal("  synthetic  ", captured.Text);
        Assert.Equal("synthetic\n[reviewed by acme.review-core]", results["call-1"]);
        Assert.False(results.ContainsKey("stale"));
    }
}
