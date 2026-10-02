using System.ClientModel.Primitives;
using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Agents;
using DotCraft.Configuration;
using DotCraft.Memory;
using DotCraft.Security;
using DotCraft.Sessions;
using DotCraft.Skills;
using DotCraft.Tools;
using Microsoft.Extensions.AI;
using OpenAI.Chat;
using Xunit;
using ChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace DotCraft.Tests.Sessions.Protocol;

public sealed class FreeformCallProtocolSwitchTests : IDisposable
{
    private readonly string _tempDir = Directory.CreateTempSubdirectory("freeform-switch-").FullName;

    [Fact]
    public async Task FreeformCallDispatchedOnFunctionProtocol_ReplaysAsCustomOnResponsesOnly()
    {
        var config = AppConfigTestFactory.CreateOpenAI();
        var chatClient = new ScriptChatClient();
        var dispatcher = new ToolDispatcher(recorder: new ToolInvocationRecorderRouter());
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
            toolSources: [new ScriptToolSource()]);
        var service = new SessionService(
            agentFactory,
            new StreamingFunctionInvokingChatClient(chatClient).AsAIAgent(),
            new SessionPersistenceService(new ThreadStore(_tempDir)),
            new SessionGate());
        var thread = await service.CreateThreadAsync(new SessionIdentity
        {
            ChannelName = "test",
            UserId = "u",
            WorkspacePath = _tempDir
        });
        await service.RefreshThreadAgentAsync(thread.Id);

        await foreach (var _ in service.SubmitInputAsync(thread.Id, [new TextContent("run it")]))
        {
        }

        var history = Assert.IsAssignableFrom<IReadOnlyList<ChatMessage>>(chatClient.FollowUpMessages);
        var call = Assert.Single(history.SelectMany(static message => message.Contents).OfType<FunctionCallContent>());
        Assert.True(ProviderFunctionCallMetadata.IsCustomToolCall(call));

        var responses = ModelReaderWriter.Write(
            ResponsesToolSearchMapper.CreateResponseOptions("gpt-test", history, new ChatOptions())).ToString();
        using var responsesDocument = JsonDocument.Parse(responses);
        var input = responsesDocument.RootElement.GetProperty("input").EnumerateArray().ToArray();
        var customCall = Assert.Single(input, static item => item.GetProperty("type").GetString() == "custom_tool_call");
        Assert.Equal("return 1;", customCall.GetProperty("input").GetString());
        Assert.Single(input, static item => item.GetProperty("type").GetString() == "custom_tool_call_output");
        Assert.DoesNotContain(input, static item => item.GetProperty("type").GetString() == "function_call");

        var chatCall = Assert.Single(
            MicrosoftExtensionsAIChatExtensions.AsOpenAIChatMessages(history)
                .OfType<AssistantChatMessage>()
                .SelectMany(static message => message.ToolCalls));
        Assert.Equal(ChatToolCallKind.Function, chatCall.Kind);
        Assert.Equal("run_script", chatCall.FunctionName);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); }
        catch { }
    }

    private sealed class ScriptToolSource : IToolSource
    {
        public string SourceId => "script-test";

        public ValueTask<IReadOnlyList<ToolRegistration>> GetRegistrationsAsync(
            ToolPlanningContext context,
            CancellationToken cancellationToken = default)
        {
            var id = new ToolDefinitionId(ToolSourceKind.CoreNative, SourceId, new SourceToolId("run_script"));
            var definition = new ToolDefinition(
                id,
                new ToolName(null, "run_script"),
                "Run a script.",
                JsonSerializer.SerializeToElement(new
                {
                    type = "object",
                    properties = new { code = new { type = "string" } },
                    required = new[] { "code" }
                }),
                freeformInput: new ToolFreeformInput("code", "lark", "start: SOURCE\nSOURCE: /[\\s\\S]+/\n"));
            var binding = new ToolRuntimeBinding(
                new RuntimeBindingId("script-test:1"),
                id,
                new ScriptRuntime(),
                ToolBindingLeases.AlwaysAvailable,
                "test",
                context.Revision);
            return ValueTask.FromResult<IReadOnlyList<ToolRegistration>>([
                new ToolRegistration(definition, binding, ToolProjectionShape.StandardPair)
            ]);
        }
    }

    private sealed class ScriptRuntime : IToolRuntime
    {
        public ValueTask<ToolExecutionResult> InvokeAsync(
            ToolInvocationContext context,
            JsonObject arguments,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(ToolExecutionResult.Succeeded("ran"));
    }

    private sealed class ScriptChatClient : IChatClient
    {
        private int _requests;

        public IReadOnlyList<ChatMessage>? FollowUpMessages { get; private set; }

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
                yield return new ChatResponseUpdate(ChatRole.Assistant,
                    [new FunctionCallContent("call-script", "run_script", new Dictionary<string, object?> { ["code"] = "return 1;" })]);
            }
            else
            {
                FollowUpMessages = chatMessages.ToList();
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
