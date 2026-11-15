using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using DotCraft.CodexGateway.Workspace;
using DotCraft.Sdk;
using DotCraft.Sdk.Wire;
using Xunit;

namespace DotCraft.CodexGateway.Tests;

public sealed class CodexWorkspaceConfigurationTests
{
    [Theory]
    [InlineData("deny", "default", "max", "max", "plan")]
    [InlineData("autoApprove", "default", "xhigh", "extraHigh", "default")]
    [InlineData("default", "autoApprove", "ultra", "ultra", "default")]
    public async Task OmittedOrNullApprovalSelectionPreservesSourcePolicyAndModelSettings(
        string threadPolicy, string workspacePolicy, string effort, string sourceEffort, string mode)
    {
        using var fixture = new ConfigurationFixture(threadPolicy, workspacePolicy);
        await using var session = await fixture.ConnectAsync();
        var catalog = Json(await session.HandleAsync("model/list", Json(new { }), default));
        Assert.Contains(catalog.GetProperty("data")[0].GetProperty("supportedReasoningEfforts").EnumerateArray(),
            entry => entry.GetProperty("reasoningEffort").GetString() == effort);
        var configuration = Json(await session.HandleAsync("config/read", Json(new { }), default)).GetProperty("config");
        Assert.Equal(workspacePolicy == "autoApprove" ? ":danger-full-access" : "dotcraft",
            configuration.GetProperty("default_permissions").GetString());
        Assert.Equal("danger-full-access", configuration.GetProperty("sandbox_mode").GetString());

        var created = Json(await session.HandleAsync("thread/start", Json(new
        {
            permissions = "dotcraft", summary = "none", serviceTier = (string?)null,
            collaborationMode = Collaboration(mode, effort)
        }), default));
        var threadId = created.GetProperty("thread").GetProperty("id").GetString();
        Assert.Equal(JsonValueKind.Null, created.GetProperty("turnsBackwardsCursor").ValueKind);
        Assert.Equal(JsonValueKind.Null, created.GetProperty("itemsBackwardsCursor").ValueKind);
        Assert.Equal("never", created.GetProperty("approvalPolicy").GetString());
        Assert.Equal("dangerFullAccess", created.GetProperty("sandbox").GetProperty("type").GetString());
        Assert.Equal(threadPolicy == "autoApprove" || threadPolicy == "default" && workspacePolicy == "autoApprove"
                ? ":danger-full-access" : "dotcraft",
            created.GetProperty("activePermissionProfile").GetProperty("id").GetString());
        Assert.Equal(effort, created.GetProperty("reasoningEffort").GetString());
        Assert.Equal(effort, created.GetProperty("collaborationMode").GetProperty("settings").GetProperty("reasoning_effort").GetString());
        var start = fixture.Transport.Requests.Single(r => r.Method == "thread/start").Parameters.GetProperty("config");
        Assert.False(start.TryGetProperty("approvalPolicy", out _));

        for (var attempt = 0; attempt < 2; attempt++)
        {
            var parameters = new Dictionary<string, object?>
            {
                ["threadId"] = threadId, ["approvalsReviewer"] = "user", ["summary"] = "none", ["serviceTier"] = null,
                ["collaborationMode"] = created.GetProperty("collaborationMode"),
                ["input"] = new[] { new { type = "text", text = "Continue " + attempt } }
            };
            if (attempt == 0) parameters["permissions"] = "dotcraft";
            else
            {
                parameters["sandboxPolicy"] = created.GetProperty("sandbox");
                parameters["approvalPolicy"] = null;
            }
            await session.HandleAsync("turn/start", Json(parameters), default);
        }

        var turns = fixture.Transport.Requests.Where(r => r.Method == "turn/start").ToArray();
        Assert.Equal(2, turns.Length);
        Assert.All(turns, request =>
        {
            Assert.False(request.Parameters.TryGetProperty("approvalPolicy", out _));
            Assert.Equal(sourceEffort, request.Parameters.GetProperty("reasoning").GetProperty("effort").GetString());
            Assert.Equal("standard", request.Parameters.GetProperty("speed").GetString());
            Assert.Equal(mode == "plan" ? "plan" : "agent", request.Parameters.GetProperty("mode").GetString());
        });
        Assert.Equal(threadPolicy, fixture.Transport.Configuration["approvalPolicy"]!.GetValue<string>());
        Assert.DoesNotContain(fixture.Transport.Requests, r => r.Method is "thread/config/update" or "config/batchWrite");
    }

    [Theory]
    [InlineData(":danger-full-access", null, "autoApprove")]
    [InlineData(":danger-full-access", "on-request", "prompt")]
    public async Task NativeProfilesUseExplicitApprovalOrTheFullAccessDefault(
        string profile, string? approvalPolicy, string sourcePolicy)
    {
        using var fixture = new ConfigurationFixture("deny", "default");
        await using var session = await fixture.ConnectAsync();
        var created = Json(await session.HandleAsync("thread/start", Json(new
        {
            permissions = profile, approvalPolicy
        }), default));
        Assert.Equal(sourcePolicy, fixture.Transport.Configuration["approvalPolicy"]!.GetValue<string>());
        Assert.Equal(sourcePolicy == "autoApprove" ? ":danger-full-access" : "dotcraft",
            created.GetProperty("activePermissionProfile").GetProperty("id").GetString());
    }

    [Fact]
    public async Task IsolationOnlyConfigEditsDoNotWriteSource()
    {
        using var fixture = new ConfigurationFixture("default", "default");
        await using var session = await fixture.ConnectAsync();
        await session.HandleAsync("config/batchWrite", Json(new
        {
            expectedVersion = "workspace-v1",
            edits = new[]
            {
                new { keyPath = "default_permissions", value = "dotcraft", mergeStrategy = "replace" },
                new { keyPath = "sandbox_mode", value = "workspace-write", mergeStrategy = "replace" },
                new { keyPath = "model_reasoning_summary", value = "none", mergeStrategy = "replace" }
            }
        }), default);
        Assert.DoesNotContain(fixture.Transport.Requests, request => request.Method == "config/batchWrite");
    }

    [Fact]
    public async Task UnsupportedSelectionsFailBeforeSourceAdmission()
    {
        using var fixture = new ConfigurationFixture("default", "default");
        await using var session = await fixture.ConnectAsync();
        foreach (var unsupported in new object[]
        {
            new { permissions = "other" }, new { sandbox = "danger-full-access", permissions = "dotcraft" },
            new { summary = "auto" }, new { approvalPolicy = "on-failure" }
        })
            await Assert.ThrowsAsync<ArgumentException>(() => session.HandleAsync("thread/start", Json(unsupported), default));
        Assert.DoesNotContain(fixture.Transport.Requests, request => request.Method == "thread/start");
        var created = Json(await session.HandleAsync("thread/start", Json(new { sandbox = "read-only" }), default));
        var threadId = created.GetProperty("thread").GetProperty("id").GetString();
        await Assert.ThrowsAsync<ArgumentException>(() => session.HandleAsync("turn/start", Json(new
        {
            threadId, permissions = "dotcraft", sandboxPolicy = new { type = "dangerFullAccess" },
            input = new[] { new { type = "text", text = "do not admit" } }
        }), default));
        Assert.DoesNotContain(fixture.Transport.Requests, request => request.Method == "turn/start");
    }

    [Theory]
    [InlineData("sandbox", "read-only")]
    [InlineData("permissions", "dotcraft")]
    public async Task ExplicitPermissionSelectionOverridesConfigDefaultsBeforeAdmission(string selection, string value)
    {
        using var fixture = new ConfigurationFixture("deny", "default");
        await using var session = await fixture.ConnectAsync();
        var parameters = new Dictionary<string, object?>
        {
            [selection] = value,
            ["config"] = new { sandbox_mode = "workspace-write", default_permissions = "other" }
        };
        var created = Json(await session.HandleAsync("thread/start", Json(parameters), default));
        var threadId = created.GetProperty("thread").GetProperty("id").GetString();
        Assert.Equal("dangerFullAccess", created.GetProperty("sandbox").GetProperty("type").GetString());
        Assert.Equal("dotcraft", created.GetProperty("activePermissionProfile").GetProperty("id").GetString());
        parameters["threadId"] = threadId;
        await session.HandleAsync("thread/resume", Json(parameters), default);
        parameters.Remove(selection);
        parameters["sandboxPolicy"] = new { type = "workspaceWrite", networkAccess = true };
        parameters["input"] = new[] { new { type = "text", text = "continue" } };
        await session.HandleAsync("turn/start", Json(parameters), default);

        var admitted = Assert.Single(fixture.Transport.Requests, request => request.Method == "turn/start");
        Assert.False(admitted.Parameters.TryGetProperty("approvalPolicy", out _));
        Assert.Equal("deny", fixture.Transport.Configuration["approvalPolicy"]!.GetValue<string>());
        Assert.DoesNotContain(fixture.Transport.Requests, request => request.Method is "thread/config/update" or "config/batchWrite");
    }

    private static object Collaboration(string mode, string effort) => new
    {
        mode, settings = new { model = "dotcraft/provider/model", reasoning_effort = effort, developer_instructions = (string?)null }
    };

    private static JsonElement Json(object? value) => JsonSerializer.SerializeToElement(value);

    private sealed class ConfigurationFixture(string threadPolicy, string workspacePolicy) : IDisposable
    {
        private readonly string _state = Path.Combine(Path.GetTempPath(), "gateway-config-" + Guid.NewGuid());
        public ConfigurationTransport Transport { get; } = new(threadPolicy, workspacePolicy);

        public async Task<CodexWorkspaceSession> ConnectAsync()
        {
            var client = await DotCraftClient.ConnectAsync(Transport);
            return new(client, Path.GetTempPath(), _state, (_, _) => Task.CompletedTask);
        }

        public void Dispose()
        {
            if (Directory.Exists(_state)) Directory.Delete(_state, true);
        }
    }

    private sealed class ConfigurationTransport(string threadPolicy, string workspacePolicy) : IJsonRpcTransport
    {
        private readonly Channel<JsonDocument> _incoming = Channel.CreateUnbounded<JsonDocument>();
        public List<(string Method, JsonElement Parameters)> Requests { get; } = [];
        public JsonObject Configuration { get; } = new()
        {
            ["providerId"] = "provider", ["model"] = "model", ["approvalPolicy"] = threadPolicy,
            ["reasoning"] = new JsonObject { ["enabled"] = true, ["effort"] = "max" }, ["mode"] = "agent"
        };

        public Task WriteAsync(object message, CancellationToken cancellationToken = default)
        {
            var json = Json(message);
            if (!json.TryGetProperty("id", out var id)) return Task.CompletedTask;
            var method = json.GetProperty("method").GetString()!;
            var p = json.GetProperty("params").Clone();
            Requests.Add((method, p));
            object result = method switch
            {
                "initialize" => new { serverInfo = new { name = "configuration-fixture", version = "1", protocolVersion = "1" }, capabilities = new { threadManagement = true } },
                "config/read" => new
                {
                    config = new
                    {
                        ProviderId = "provider", ProviderPreferences = new { provider = new { model = "model", reasoning = new { effort = "max" } } },
                        Permissions = new { DefaultApprovalPolicy = workspacePolicy }
                    },
                    layers = new[] { new { name = new { type = "workspace", file = Path.Combine(Path.GetTempPath(), ".craft", "config.json") }, version = "workspace-v1", config = new { } } }
                },
                "provider/list" => new { providers = new[] { new { id = "provider" } } },
                "model/list" => new
                {
                    success = true, models = new[] { new
                    {
                        id = "model", inputModalities = new[] { "text" },
                        reasoning = new
                        {
                            defaultEnabled = true, defaultEffort = "max", supportsDisable = true,
                            supportedEfforts = new[] { new { effort = "max" }, new { effort = "extraHigh" }, new { effort = "ultra" } }
                        }
                    } }
                },
                "thread/start" => Create(p),
                "thread/read" => new { thread = Thread() },
                "thread/resume" => new { thread = Thread(), instructionSources = Array.Empty<string>(),
                    turnsBackwardsCursor = (string?)null, itemsBackwardsCursor = (string?)null },
                "turn/start" => new { turn = new { id = "turn-source", status = "running" } },
                _ => throw new InvalidOperationException("Unexpected configuration fixture request: " + method)
            };
            _incoming.Writer.TryWrite(JsonDocument.Parse(Json(new { id, result }).GetRawText()));
            return Task.CompletedTask;
        }

        private object Create(JsonElement p)
        {
            if (p.TryGetProperty("config", out var config) && config.ValueKind == JsonValueKind.Object)
                foreach (var field in config.EnumerateObject()) Configuration[field.Name] = JsonNode.Parse(field.Value.GetRawText());
            return new { thread = Thread(), instructionSources = Array.Empty<string>() };
        }

        private object Thread() => new
        {
            id = "thread-source", sessionId = "thread-source", cwd = Path.GetTempPath(), workspacePath = Path.GetTempPath(),
            status = "active", runtime = new { running = false }, configuration = Configuration
        };

        public async Task<JsonDocument?> ReadAsync(CancellationToken cancellationToken = default) =>
            await _incoming.Reader.ReadAsync(cancellationToken);

        public ValueTask DisposeAsync()
        {
            _incoming.Writer.TryComplete();
            return ValueTask.CompletedTask;
        }
    }
}
