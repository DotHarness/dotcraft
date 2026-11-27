using System.Text.Json.Nodes;
using DotCraft.Agents;
using DotCraft.Configuration;
using DotCraft.Hooks;
using DotCraft.Memory;
using DotCraft.Security;
using DotCraft.Sessions;
using DotCraft.Skills;
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
    public void HooksLoader_ValidatesMcpToolHooksAndMarksBundledPluginHooksManaged()
    {
        var dataDir = Path.Combine(_tempDir, ".craft");
        var userDataDir = Path.Combine(_tempDir, "user-data");
        var pluginDir = Path.Combine(userDataDir, "plugins", "browser");
        Directory.CreateDirectory(Path.Combine(pluginDir, ".craft-plugin"));
        Directory.CreateDirectory(Path.Combine(pluginDir, "hooks"));
        Directory.CreateDirectory(dataDir);

        File.WriteAllText(Path.Combine(pluginDir, DotCraft.Plugins.BuiltInPluginDeployer.MarkerFile), """
            { "marketplaceName": "dotcraft-bundled" }
            """);
        File.WriteAllText(Path.Combine(pluginDir, ".craft-plugin", "plugin.json"), """
            {
              "schemaVersion": 1,
              "id": "browser",
              "displayName": "Browser",
              "description": "Bundled browser plugin",
              "version": "1.0.0",
              "hooks": "./hooks/hooks.json"
            }
            """);
        File.WriteAllText(Path.Combine(pluginDir, "hooks", "hooks.json"), """
            {
              "hooks": {
                "Stop": [
                  {
                    "hooks": [
                      { "type": "mcp_tool", "server": "node_repl", "tool": "turn_ended", "skipIfDormant": true }
                    ]
                  }
                ]
              }
            }
            """);

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
                ]
              }
            }
            """);

        var paths = new DotCraft.Workspaces.DotCraftPaths(_tempDir, dataDir, userDataDir);
        var loader = new HooksLoader(paths);
        var discovery = loader.Discover(new AppConfig(), _tempDir);

        var bundledHook = Assert.Single(discovery.Hooks, h => h.Source == HookSources.Plugin && h.PluginId == "browser");
        Assert.Equal("mcp_tool", bundledHook.HandlerType);
        Assert.Equal("node_repl", bundledHook.McpServer);
        Assert.Equal("turn_ended", bundledHook.McpTool);
        Assert.True(bundledHook.SkipIfDormant);
        Assert.True(bundledHook.IsManaged);
        Assert.Equal(HookTrustStatuses.Managed, bundledHook.TrustStatus);
        Assert.Null(bundledHook.Command);

        var workspaceHook = Assert.Single(discovery.Hooks, h => h.Source == HookSources.Workspace);
        Assert.Equal("mcp_tool", workspaceHook.HandlerType);
        Assert.False(workspaceHook.IsManaged);
        Assert.Equal(HookTrustStatuses.Untrusted, workspaceHook.TrustStatus);

        Assert.Contains(discovery.Warnings, w => w.Contains("non-empty 'command'", StringComparison.OrdinalIgnoreCase));
        Assert.True(discovery.RuntimeConfig.Hooks.TryGetValue(nameof(HookEvent.Stop), out var runnable));
        Assert.Single(runnable!);
        Assert.Equal("node_repl", runnable![0].Hooks[0].Server);
    }

    [Fact]
    public async Task HookRunner_DeduplicatesIdenticalMcpToolHooksAndFailsOpenOnException()
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
                            new HookEntry { Type = "mcp_tool", Server = "node_repl", Tool = "turn_ended", SkipIfDormant = true },
                            new HookEntry { Type = "mcp_tool", Server = "node_repl", Tool = "turn_ended", SkipIfDormant = true },
                            new HookEntry { Type = "mcp_tool", Server = "broken", Tool = "boom" }
                        ]
                    }
                ]
            }
        };

        var calls = new List<(string Server, string Tool)>();
        var runner = new HookRunner(config, _tempDir)
        {
            McpExecutor = new DelegateMcpExecutor((req, _) =>
            {
                calls.Add((req.Server, req.Tool));
                if (req.Server == "broken")
                    throw new InvalidOperationException("boom");
                return Task.FromResult(new HookMcpExecutionResult(Succeeded: true, Output: "cleaned"));
            })
        };

        var result = await runner.RunAsync(HookEvent.Stop, new HookInput { SessionId = "thread-1", TurnId = "turn-1" }, CancellationToken.None);

        Assert.Equal([("node_repl", "turn_ended"), ("broken", "boom")], calls);
        Assert.False(result.Blocked);
        Assert.Equal("cleaned", result.Output);
    }

    [Fact]
    public async Task SessionService_FiresSessionEndOnArchiveAndDeleteAndInterruptOnCancel()
    {
        var events = new List<(HookEvent Event, string? ThreadId, bool IsSubagent)>();
        var hookConfig = new HooksFileConfig
        {
            Hooks =
            {
                [nameof(HookEvent.Interrupt)] = [new HookMatcherGroup { Hooks = [new HookEntry { Type = "mcp_tool", Server = "node_repl", Tool = "turn_ended" }] }],
                [nameof(HookEvent.SessionEnd)] = [new HookMatcherGroup { Hooks = [new HookEntry { Type = "mcp_tool", Server = "node_repl", Tool = "session_ended" }] }]
            }
        };

        var runner = new HookRunner(hookConfig, _tempDir)
        {
            McpExecutor = new DelegateMcpExecutor((req, _) =>
            {
                events.Add((req.Event, req.HookInput.SessionId, req.HookInput.IsSubagent));
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

        Assert.Contains(events, e => e.Event == HookEvent.Interrupt && e.ThreadId == thread.Id && !e.IsSubagent);

        await service.ArchiveThreadAsync(thread.Id);
        Assert.Contains(events, e => e.Event == HookEvent.SessionEnd && e.ThreadId == thread.Id && !e.IsSubagent);

        events.Clear();
        await service.DeleteThreadPermanentlyAsync(thread.Id);
        Assert.Contains(events, e => e.Event == HookEvent.SessionEnd && e.ThreadId == thread.Id && !e.IsSubagent);
    }

    private sealed class DelegateMcpExecutor(
        Func<HookMcpExecutionRequest, CancellationToken, Task<HookMcpExecutionResult>> handler) : IHookMcpExecutor
    {
        public Task<HookMcpExecutionResult> ExecuteAsync(HookMcpExecutionRequest request, CancellationToken ct) =>
            handler(request, ct);
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
