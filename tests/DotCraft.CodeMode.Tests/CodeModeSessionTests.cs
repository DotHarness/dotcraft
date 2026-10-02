using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Agents;
using DotCraft.CLI;
using DotCraft.Configuration;
using DotCraft.Memory;
using DotCraft.Processes;
using DotCraft.Security;
using DotCraft.Sessions;
using DotCraft.Skills;
using DotCraft.Tests;
using DotCraft.Tools;
using Microsoft.Extensions.AI;

namespace DotCraft.CodeMode.Tests;

public sealed class CodeModeSessionTests : IDisposable
{
    private readonly string _tempDir = Directory.CreateTempSubdirectory("codemode-session-").FullName;

    [Theory]
    [InlineData(AppConfig.CodeModeSetting.On)]
    [InlineData(AppConfig.CodeModeSetting.Only)]
    public async Task ExecTurn_RecordsNestedCallsInTheTurnAndReturnsScriptOutput(AppConfig.CodeModeSetting mode)
    {
        _ = typeof(CommandLineArgs).Assembly;
        var config = AppConfigTestFactory.CreateOpenAI();
        config.Tools.CodeMode.Mode = mode;
        var chatClient = new ExecChatClient("text(await tools.Echo({ value: 'hi' }));");
        var recorder = new ToolInvocationRecorderRouter();
        var dispatcher = new ToolDispatcher(recorder: recorder);
        await using var host = new CodeModeWorkerHost(new ManagedChildProcessFactory(), _tempDir, new CodeModeLimits());
        await using var agentFactory = new AgentFactory(
            dotcraftPath: _tempDir,
            workspacePath: _tempDir,
            config: config,
            memoryStore: new MemoryStore(_tempDir),
            skillsLoader: new SkillsLoader(_tempDir),
            approvalService: new SessionScopedApprovalService(new AutoApproveApprovalService()),
            blacklist: null,
            chatClientRegistry: TestModelProviderRegistry.Create(),
            chatClient: chatClient,
            toolDispatcher: dispatcher,
            toolSources: [new EchoToolSource()]);
        var service = new SessionService(
            agentFactory,
            new StreamingFunctionInvokingChatClient(chatClient).AsAIAgent(),
            new SessionPersistenceService(new ThreadStore(_tempDir)),
            new SessionGate(),
            toolSnapshotFinalizers: [new CodeModeToolFinalizer(() => config, host, new CodeModeStore(), dispatcher)]);
        recorder.Bind(service);
        var thread = await service.CreateThreadAsync(new SessionIdentity
        {
            ChannelName = "test",
            UserId = "u",
            WorkspacePath = _tempDir
        });
        await service.RefreshThreadAgentAsync(thread.Id);

        await foreach (var _ in service.SubmitInputAsync(thread.Id, [new TextContent("echo it")]))
        {
        }

        Assert.Contains("exec", chatClient.ToolNames);
        if (mode == AppConfig.CodeModeSetting.Only)
        {
            Assert.DoesNotContain("Echo", chatClient.ToolNames);
            Assert.Contains("declare const tools: {\n  Echo(args: {\n    value?: string;\n  }): Promise<string>;\n};",
                chatClient.Descriptions["exec"]);
        }
        else
        {
            Assert.EndsWith("\n\nCode mode: `tools.Echo(args)` resolves to a string.", chatClient.Descriptions["Echo"]);
        }
        var execResult = Assert.IsType<FunctionResultContent>(chatClient.ExecResult);
        Assert.StartsWith("Script completed", execResult.Result?.ToString());
        Assert.Contains("echo:hi", execResult.Result?.ToString());
        var items = (await service.GetThreadAsync(thread.Id)).Turns.Single().Items;
        var execCall = Assert.Single(items, static item => item.Payload is ToolCallPayload { ToolName: "exec" });
        Assert.Equal("call-exec", execCall.AsToolCall!.CallId);
        Assert.Null(execCall.InvocationOrigin);
        Assert.True(execCall.FreeformCall);
        var nested = items.Where(static item => item.Payload is ToolCallPayload { ToolName: "Echo" } or ToolResultPayload { ToolName: "Echo" })
            .ToArray();
        Assert.Equal(2, nested.Length);
        Assert.All(nested, static item =>
        {
            Assert.Equal(ToolInvocationOrigin.CodeModeKind, item.InvocationOrigin);
            Assert.False(item.FreeformCall);
            Assert.StartsWith("exec-", item.Payload is ToolCallPayload call ? call.CallId : ((ToolResultPayload)item.Payload!).CallId,
                StringComparison.Ordinal);
        });
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); }
        catch { }
    }

    private sealed class EchoToolSource : IToolSource
    {
        public string SourceId => "echo-test";

        public ValueTask<IReadOnlyList<ToolRegistration>> GetRegistrationsAsync(
            ToolPlanningContext context,
            CancellationToken cancellationToken = default)
        {
            var id = new ToolDefinitionId(ToolSourceKind.CoreNative, SourceId, new SourceToolId("Echo"));
            var definition = new ToolDefinition(
                id,
                new ToolName(null, "Echo"),
                "Echo a value.",
                JsonSerializer.SerializeToElement(new { type = "object", properties = new { value = new { type = "string" } } }));
            var binding = new ToolRuntimeBinding(
                new RuntimeBindingId("echo-test:1"),
                id,
                new EchoRuntime(),
                ToolBindingLeases.AlwaysAvailable,
                "test",
                context.Revision);
            return ValueTask.FromResult<IReadOnlyList<ToolRegistration>>([
                new ToolRegistration(definition, binding, ToolProjectionShape.StandardPair)
            ]);
        }
    }

    private sealed class EchoRuntime : IToolRuntime
    {
        public ValueTask<ToolExecutionResult> InvokeAsync(
            ToolInvocationContext context,
            JsonObject arguments,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(ToolExecutionResult.Succeeded($"echo:{arguments["value"]}"));
    }

    private sealed class ExecChatClient(string code) : IChatClient
    {
        private int _requests;

        public IReadOnlyList<string> ToolNames { get; private set; } = [];

        public IReadOnlyDictionary<string, string> Descriptions { get; private set; } = new Dictionary<string, string>();

        public FunctionResultContent? ExecResult { get; private set; }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ChatResponse([new ChatMessage(ChatRole.Assistant, [new TextContent("done")])]));

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _requests) == 1)
            {
                ToolNames = options?.Tools?.Select(static tool => tool.Name).ToArray() ?? [];
                Descriptions = options?.Tools?.ToDictionary(static tool => tool.Name, static tool => tool.Description)
                               ?? new Dictionary<string, string>();
                yield return new ChatResponseUpdate(ChatRole.Assistant,
                    [new FunctionCallContent("call-exec", "exec", new Dictionary<string, object?> { ["code"] = code })]);
            }
            else
            {
                ExecResult = chatMessages
                    .SelectMany(static message => message.Contents)
                    .OfType<FunctionResultContent>()
                    .SingleOrDefault(static result => result.CallId == "call-exec");
                yield return new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("done")]);
            }

            await Task.CompletedTask;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
