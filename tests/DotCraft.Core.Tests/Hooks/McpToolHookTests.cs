using System.Text.Json.Nodes;
using DotCraft.Agents;
using DotCraft.Configuration;
using DotCraft.Hooks;
using DotCraft.Memory;
using DotCraft.Security;
using DotCraft.Sessions;
using DotCraft.Skills;
using DotCraft.Tests.Sessions.Protocol;
using DotCraft.Tools;
using Microsoft.Extensions.AI;
using Xunit;

namespace DotCraft.Core.Tests.Hooks;

public sealed class McpToolHookTests : IDisposable
{
    private readonly string _tempDir;

    public McpToolHookTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "McpHooks_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); }
        catch { }
    }

    [Fact]
    public void HooksLoader_VerifiesBundledProvenanceAndEnforcesSessionEndRules()
    {
        var dataDir = Path.Combine(_tempDir, ".craft");
        var userDataDir = Path.Combine(_tempDir, "user-data");
        var bundledRoot = Path.Combine(_tempDir, "bundled");
        var bundledBrowserDir = Path.Combine(bundledRoot, "browser");
        var installedBrowserDir = Path.Combine(userDataDir, "plugins", "browser");
        var workspaceSpoofDir = Path.Combine(dataDir, "plugins", "spoof");
        Directory.CreateDirectory(dataDir);

        WriteBrowserPlugin(bundledBrowserDir, "turn_ended");
        WriteBrowserPlugin(installedBrowserDir, "turn_ended");
        WriteBrowserPlugin(workspaceSpoofDir, "turn_ended", pluginId: "spoof");
        File.WriteAllText(Path.Combine(workspaceSpoofDir, DotCraft.Plugins.BuiltInPluginDeployer.MarkerFile), "fake");

        File.WriteAllText(Path.Combine(dataDir, "hooks.json"), """
            {
              "hooks": {
                "Stop": [
                  {
                    "hooks": [
                      { "type": "mcp_tool", "server": "custom", "tool": "cleanup" },
                      { "type": "mcp_tool", "server": "invalid", "tool": "bad", "command": "echo nope" }
                    ]
                  }
                ],
                "SessionEnd": [
                  {
                    "hooks": [
                      { "type": "mcp_tool", "server": "node_repl", "tool": "session_ended" },
                      { "type": "command", "command": "echo bye", "async": true }
                    ]
                  }
                ]
              }
            }
            """);

        var paths = new DotCraft.Workspaces.DotCraftPaths(_tempDir, dataDir, userDataDir);
        var loader = new HooksLoader(paths);
        var discovery = loader.Discover(new AppConfig(), _tempDir, [bundledRoot]);

        var bundledHook = Assert.Single(discovery.Hooks, h => h.Source == HookSources.Plugin && h.PluginId == "browser");
        Assert.Equal("mcp_tool", bundledHook.HandlerType);
        Assert.Equal("node_repl", bundledHook.McpServer);
        Assert.Equal("turn_ended", bundledHook.McpTool);
        Assert.True(bundledHook.SkipIfDormant);
        Assert.True(bundledHook.IsManaged);
        Assert.Equal(HookTrustStatuses.Managed, bundledHook.TrustStatus);
        Assert.Null(bundledHook.Command);

        var spoofHook = Assert.Single(discovery.Hooks, h => h.Source == HookSources.Plugin && h.PluginId == "spoof");
        Assert.False(spoofHook.IsManaged);
        Assert.Equal(HookTrustStatuses.Untrusted, spoofHook.TrustStatus);

        var workspaceStopHook = Assert.Single(discovery.Hooks, h => h.Source == HookSources.Workspace && h.EventName == nameof(HookEvent.Stop));
        Assert.Equal("mcp_tool", workspaceStopHook.HandlerType);
        Assert.False(workspaceStopHook.IsManaged);
        Assert.Equal(HookTrustStatuses.Untrusted, workspaceStopHook.TrustStatus);

        var sessionEndCommand = Assert.Single(discovery.Hooks, h => h.EventName == nameof(HookEvent.SessionEnd));
        Assert.Equal("command", sessionEndCommand.HandlerType);
        Assert.Equal("sync", sessionEndCommand.ExecutionMode);

        Assert.Contains(discovery.Warnings, w => w.Contains("SessionEnd mcp_tool", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(discovery.Warnings, w => w.Contains("async SessionEnd", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(discovery.Warnings, w => w.Contains("non-empty 'command'", StringComparison.OrdinalIgnoreCase));
        Assert.True(discovery.RuntimeConfig.Hooks.TryGetValue(nameof(HookEvent.Stop), out var runnable));
        Assert.Single(runnable!);
        Assert.Equal("node_repl", runnable![0].Hooks[0].Server);
    }

    [Fact]
    public async Task HookRunner_DeduplicatesIdenticalMcpToolHooksWithoutFoldingArgumentCase()
    {
        var config = new HooksFileConfig
        {
            Hooks =
            {
                [nameof(HookEvent.Stop)] =
                [
                    new HookMatcherGroup
                    {
                        Hooks =
                        [
                            new HookEntry { Type = "mcp_tool", Server = "node_repl", Tool = "turn_ended", Input = new JsonObject { ["key"] = "Foo" } },
                            new HookEntry { Type = "mcp_tool", Server = "Node_Repl", Tool = "turn_ended", Input = new JsonObject { ["key"] = "Foo" } },
                            new HookEntry { Type = "mcp_tool", Server = "node_repl", Tool = "turn_ended", Input = new JsonObject { ["key"] = "foo" } },
                            new HookEntry { Type = "mcp_tool", Server = "broken", Tool = "boom" }
                        ]
                    }
                ]
            }
        };

        var calls = new List<(string Server, string Tool, string? Key)>();
        var runner = new HookRunner(config, _tempDir)
        {
            McpExecutor = new DelegateMcpExecutor((req, _) =>
            {
                calls.Add((req.Server, req.Tool, req.Input?["key"]?.GetValue<string>()));
                if (req.Server == "broken")
                    throw new InvalidOperationException("boom");
                return Task.FromResult(new HookMcpExecutionResult(Succeeded: true, Output: "cleaned"));
            })
        };

        var result = await runner.RunAsync(HookEvent.Stop, new HookInput { SessionId = "thread-1", TurnId = "turn-1" }, CancellationToken.None);

        Assert.Equal(
            [("node_repl", "turn_ended", "Foo"), ("node_repl", "turn_ended", "foo"), ("broken", "boom", null)],
            calls);
        Assert.False(result.Blocked);
        Assert.Equal("cleaned\ncleaned", result.Output);
    }

    [Fact]
    public async Task ToolDispatchAdapter_PropagatesTurnAndSubagentContextToHooks()
    {
        HookInput? preInput = null;
        HookInput? postInput = null;
        var config = new HooksFileConfig
        {
            Hooks =
            {
                [nameof(HookEvent.PreToolUse)] = [new HookMatcherGroup { Hooks = [new HookEntry { Type = "mcp_tool", Server = "audit", Tool = "pre" }] }],
                [nameof(HookEvent.PostToolUse)] = [new HookMatcherGroup { Hooks = [new HookEntry { Type = "mcp_tool", Server = "audit", Tool = "post" }] }]
            }
        };
        var runner = new HookRunner(config, _tempDir)
        {
            McpExecutor = new DelegateMcpExecutor((req, _) =>
            {
                if (req.Event == HookEvent.PreToolUse) preInput = req.HookInput;
                if (req.Event == HookEvent.PostToolUse) postInput = req.HookInput;
                return Task.FromResult(new HookMcpExecutionResult(Succeeded: true));
            })
        };

        var adapter = new HookRunnerToolDispatchAdapter(runner);
        var toolName = new ToolName("echo", "test");
        var defId = new ToolDefinitionId(ToolSourceKind.Mcp, "test", new SourceToolId("echo"));
        var definition = new ToolDefinition(
            defId,
            toolName,
            "Echo",
            System.Text.Json.JsonSerializer.SerializeToElement(new { type = "object" }));
        var registration = new ToolRegistration(
            definition,
            new ToolRuntimeBinding(new RuntimeBindingId("test:echo"), defId, new NoOpRuntime(), AlwaysAvailableLease.Instance, "test", 1),
            ToolProjectionShape.McpLifecycle,
            ToolExposure.Direct,
            ToolInvocationAudience.Model);
        var context = new ToolInvocationContext(
            "child-1", "turn-7", "call-9", ToolInvocationAudience.Model, toolName, defId,
            registration.Binding.Id, 1, DateTimeOffset.UtcNow, IsSubagent: true, ParentThreadId: "parent-0");

        await adapter.RunPreToolUseAsync(context, registration, new JsonObject());
        await adapter.RunTerminalAsync(context, registration, ToolExecutionResult.Succeeded("ok"));

        Assert.NotNull(preInput);
        Assert.Equal("turn-7", preInput!.TurnId);
        Assert.True(preInput.IsSubagent);
        Assert.Equal("parent-0", preInput.ParentThreadId);
        Assert.NotNull(postInput);
        Assert.Equal("turn-7", postInput!.TurnId);
        Assert.True(postInput.IsSubagent);
        Assert.Equal("parent-0", postInput.ParentThreadId);
    }

    [Fact]
    public async Task SessionService_FiresInterruptOnCancelAndSessionEndOnArchiveAndDelete()
    {
        var interruptEvents = new List<(string? ThreadId, bool IsSubagent)>();
        var sessionEndMarker = Path.Combine(_tempDir, "session-end.txt");
        var hookConfig = new HooksFileConfig
        {
            Hooks =
            {
                [nameof(HookEvent.Interrupt)] = [new HookMatcherGroup { Hooks = [new HookEntry { Type = "mcp_tool", Server = "node_repl", Tool = "turn_ended" }] }],
                [nameof(HookEvent.SessionEnd)] =
                [
                    new HookMatcherGroup
                    {
                        Hooks =
                        [
                            new HookEntry
                            {
                                Type = "command",
                                Command = TestHookCommands.AppendLine("session-end.txt", "ended"),
                                Shell = TestHookCommands.Shell,
                                Timeout = 10
                            }
                        ]
                    }
                ]
            }
        };

        var runner = new HookRunner(hookConfig, _tempDir)
        {
            McpExecutor = new DelegateMcpExecutor((req, _) =>
            {
                if (req.Event == HookEvent.Interrupt)
                    interruptEvents.Add((req.HookInput.SessionId, req.HookInput.IsSubagent));
                return Task.FromResult(new HookMcpExecutionResult(Succeeded: true));
            })
        };

        var chatClient = new CancellingChatClient();
        var dataDir = Path.Combine(_tempDir, ".craft");
        Directory.CreateDirectory(dataDir);
        var appConfig = AppConfigTestFactory.CreateOpenAI();
        await using var factory = new AgentFactory(
            dotcraftPath: dataDir,
            workspacePath: _tempDir,
            config: appConfig,
            memoryStore: new MemoryStore(dataDir),
            skillsLoader: new SkillsLoader(_tempDir),
            approvalService: new AutoApproveApprovalService(),
            blacklist: null,
            hookRunner: runner,
            chatClientRegistry: DotCraft.Tests.TestModelProviderRegistry.Create(),
            chatClient: chatClient,
            toolSources: Array.Empty<IToolSource>());
        var threadStore = new ThreadStore(dataDir);
        var persistence = new SessionPersistenceService(threadStore);
        var service = new SessionService(factory, chatClient.AsAIAgent(), persistence, new SessionGate(), hookRunner: runner);

        var thread = await service.CreateThreadAsync(new SessionIdentity
        {
            ChannelName = "test",
            UserId = "user1",
            WorkspacePath = _tempDir
        });

        string? activeTurnId = null;
        var submitTask = Task.Run(async () =>
        {
            await foreach (var evt in service.SubmitInputAsync(thread.Id, [new TextContent("run")]))
            {
                if (evt.TurnId is { Length: > 0 } turnId)
                    activeTurnId ??= turnId;
            }
        });
        await chatClient.Started.WaitAsync(TimeSpan.FromSeconds(5));
        var currentThread = await service.GetThreadAsync(thread.Id);
        var runningTurnId = activeTurnId ?? currentThread.Turns.Last().Id;
        await service.CancelTurnAsync(thread.Id, runningTurnId);
        await submitTask;

        Assert.Contains(interruptEvents, e => e.ThreadId == thread.Id && !e.IsSubagent);

        await service.ArchiveThreadAsync(thread.Id);
        Assert.Single(await File.ReadAllLinesAsync(sessionEndMarker));

        await service.DeleteThreadPermanentlyAsync(thread.Id);
        Assert.Equal(2, (await File.ReadAllLinesAsync(sessionEndMarker)).Length);
    }

    private static void WriteBrowserPlugin(string pluginDir, string toolName, string pluginId = "browser")
    {
        Directory.CreateDirectory(Path.Combine(pluginDir, ".craft-plugin"));
        Directory.CreateDirectory(Path.Combine(pluginDir, "hooks"));
        File.WriteAllText(Path.Combine(pluginDir, ".craft-plugin", "plugin.json"), $$"""
            {
              "schemaVersion": 1,
              "id": "{{pluginId}}",
              "displayName": "Browser",
              "description": "Bundled browser plugin",
              "version": "1.0.0",
              "hooks": "./hooks/hooks.json"
            }
            """);
        File.WriteAllText(Path.Combine(pluginDir, "hooks", "hooks.json"), $$"""
            {
              "hooks": {
                "Stop": [
                  {
                    "hooks": [
                      { "type": "mcp_tool", "server": "node_repl", "tool": "{{toolName}}", "skipIfDormant": true }
                    ]
                  }
                ]
              }
            }
            """);
    }

    private sealed class DelegateMcpExecutor(
        Func<HookMcpExecutionRequest, CancellationToken, Task<HookMcpExecutionResult>> handler) : IHookMcpExecutor
    {
        public Task<HookMcpExecutionResult> ExecuteAsync(HookMcpExecutionRequest request, CancellationToken ct) =>
            handler(request, ct);
    }

    private sealed class NoOpRuntime : IToolRuntime
    {
        public ValueTask<ToolExecutionResult> InvokeAsync(ToolInvocationContext context, JsonObject arguments, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(ToolExecutionResult.Succeeded("ok"));
    }

    private sealed class AlwaysAvailableLease : IToolBindingLease
    {
        public static readonly AlwaysAvailableLease Instance = new();
        public ValueTask<ToolBindingLeaseResult> CheckAsync(ToolInvocationContext context, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(ToolBindingLeaseResult.Available);
    }

    private sealed class CancellingChatClient : IChatClient
    {
        private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task Started => _started.Task;

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            _started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            yield break;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }
}
