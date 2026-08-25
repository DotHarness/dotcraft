using System.Text.Json;
using DotCraft.AppServerTestClient;
using Xunit;

namespace DotCraft.Tests.AppServerTestClient;

public sealed class DotNetPluginAuthoringSmokeTests
{
    [Fact]
    public void CliOptions_UseIsolatedDefaultsAndProviderOverrides()
    {
        var ok = DotNetPluginAuthoringSmokeCliOptions.TryParse(
            ["--provider-id", "chatgpt", "--model", "model-test", "--timeout-minutes", "3"],
            out var options,
            out var error);

        Assert.True(ok, error);
        Assert.Equal("chatgpt", options.ProviderId);
        Assert.Equal("model-test", options.Model);
        Assert.Equal(TimeSpan.FromMinutes(3), options.TurnTimeout);
        Assert.StartsWith(
            Path.Combine(Path.GetTempPath(), "dotcraft-plugin-authoring-smoke")
            + Path.DirectorySeparatorChar,
            options.WorkRoot,
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
        Assert.Equal(Path.Combine(options.WorkRoot, "report.json"), options.ReportPath);
        Assert.False(options.KeepWorkspace);
    }

    [Fact]
    public void CliOptions_RejectInvalidTimeout()
    {
        Assert.False(DotNetPluginAuthoringSmokeCliOptions.TryParse(
            ["--timeout-minutes", "0"],
            out _,
            out var invalidTimeout));
        Assert.Equal("invalid_timeout_minutes", invalidTimeout);
    }

    [Fact]
    public void Capture_ValidatesPluginInvocation()
    {
        using var notification = JsonDocument.Parse("""
            {
              "params": {
                "turn": {
                  "items": [
                    {
                      "type": "toolCall",
                      "payload": {
                        "namespace": null,
                        "toolName": "smoke_agent_tool",
                        "providerFlatName": "smoke_agent_tool",
                        "source": { "pluginId": "smoke-agent-tool" },
                        "callId": "call-1",
                        "arguments": {}
                      }
                    },
                    {
                      "type": "toolResult",
                      "payload": {
                        "callId": "call-1",
                        "success": true,
                        "result": "AUTHORING_SMOKE_V1_SYNTHETIC"
                      }
                    }
                  ]
                }
              }
            }
            """);
        var capture = new AppServerToolTurnCapture();

        AppServerToolTurnCaptureReader.ReadCompletedTurn(notification.RootElement, capture);

        DotNetPluginAuthoringSmokeRunner.ValidatePluginInvocation(
            capture,
            "AUTHORING_SMOKE_V1_SYNTHETIC",
            "test");
    }

    [Fact]
    public void InvocationValidation_RejectsWrongPluginProvenance()
    {
        using var notification = JsonDocument.Parse("""
            {
              "params": {
                "turn": {
                  "items": [
                    {
                      "type": "toolCall",
                      "payload": {
                        "namespace": null,
                        "toolName": "smoke_agent_tool",
                        "providerFlatName": "smoke_agent_tool",
                        "source": { "pluginId": "other.plugin" },
                        "callId": "call-1",
                        "arguments": {}
                      }
                    }
                  ]
                }
              }
            }
            """);
        var capture = new AppServerToolTurnCapture();
        AppServerToolTurnCaptureReader.ReadCompletedTurn(notification.RootElement, capture);

        var exception = Assert.Throws<DotNetPluginSmokeException>(() =>
            DotNetPluginAuthoringSmokeRunner.ValidatePluginInvocation(
                capture,
                "unused",
                "invoke"));

        Assert.Equal("unexpected_tool_provenance", exception.ErrorCode);
    }

    [Fact]
    public void BuildCapture_ReadsActiveFingerprint()
    {
        using var notification = JsonDocument.Parse("""
            {
              "params": {
                "turn": {
                  "items": [
                    {
                      "type": "toolCall",
                      "payload": {
                        "namespace": "DotNetPlugin",
                        "toolName": "Build",
                        "providerFlatName": "DotNetPlugin__Build",
                        "source": { "kind": "native" },
                        "callId": "build-1",
                        "arguments": { "pluginId": "smoke-agent-tool" }
                      }
                    },
                    {
                      "type": "toolResult",
                      "payload": {
                        "callId": "build-1",
                        "success": true,
                        "result": "{\"outcome\":\"built\",\"fingerprint\":\"sha256:synthetic\",\"state\":\"active\",\"diagnostics\":[]}"
                      }
                    }
                  ]
                }
              }
            }
            """);
        var capture = new AppServerToolTurnCapture();
        AppServerToolTurnCaptureReader.ReadCompletedTurn(notification.RootElement, capture);

        var observation = DotNetPluginAuthoringSmokeRunner.ParseBuildResult(
            capture,
            Assert.Single(capture.Calls),
            "build");

        Assert.Equal("built", observation.Outcome);
        Assert.Equal("active", observation.State);
        Assert.Equal("sha256:synthetic", observation.Fingerprint);
    }
}
