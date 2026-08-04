using DotCraft.Agents;
using DotCraft.Configuration;
using DotCraft.Hooks;
using DotCraft.Memory;
using DotCraft.Security;
using DotCraft.Sessions;
using DotCraft.Skills;
using DotCraft.Tools;
using Microsoft.Extensions.AI;
using SessionIdentity = DotCraft.Sessions.SessionIdentity;
using SessionThread = DotCraft.Sessions.SessionThread;
using Xunit;

namespace DotCraft.Tests.Sessions.Protocol;

public sealed class SessionServiceHookTests : IDisposable
{
    private readonly string _tempDir;

    public SessionServiceHookTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "SSHooks_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); }
        catch { }
    }

    [Fact]
    public async Task SubmitInputAsync_InjectsSessionStartHookOutputIntoPrompt()
    {
        var marker = "SUPERPOWERS_HOOK_CONTEXT";
        var chatClient = new CapturingChatClient("ok");
        await using var agentFactory = CreateAgentFactory(chatClient);
        var service = CreateService(agentFactory, chatClient, CreateSessionStartRunner(marker));
        var thread = await CreateThreadAsync(service);

        await DrainAsync(service.SubmitInputAsync(thread.Id, [new TextContent("hello")]));

        var request = Assert.Single(chatClient.CapturedRequests);
        var userMessage = request.Last(message => message.Role == ChatRole.User);
        var text = MessageText(userMessage);
        Assert.Contains("## SessionStart Hook Context", text);
        Assert.Contains(marker, text);
    }

    [Fact]
    public async Task SubmitInputAsync_RunsSessionStartHookOnlyOncePerThread()
    {
        var marker = "SESSION_START_ONCE_MARKER";
        var chatClient = new CapturingChatClient("ok");
        await using var agentFactory = CreateAgentFactory(chatClient);
        var service = CreateService(agentFactory, chatClient, CreateSessionStartRunner(marker));
        var thread = await CreateThreadAsync(service);

        await DrainAsync(service.SubmitInputAsync(thread.Id, [new TextContent("first")]));
        await DrainAsync(service.SubmitInputAsync(thread.Id, [new TextContent("second")]));

        Assert.Equal(2, chatClient.CapturedRequests.Count);
        var firstUserText = MessageText(chatClient.CapturedRequests[0].Last(message => message.Role == ChatRole.User));
        var secondUserText = MessageText(chatClient.CapturedRequests[1].Last(message => message.Role == ChatRole.User));
        Assert.Contains(marker, firstUserText);
        Assert.DoesNotContain("## SessionStart Hook Context", secondUserText);
        Assert.DoesNotContain(marker, secondUserText);
    }

    [Fact]
    public async Task SubmitInputAsync_DoesNotStopSessionStartHooksOnExitTwo()
    {
        var chatClient = new CapturingChatClient("ok");
        await using var agentFactory = CreateAgentFactory(chatClient);
        var service = CreateService(
            agentFactory,
            chatClient,
            CreateSessionStartRunnerFromCommands(
                EchoAndExitCommand("FIRST_SESSION_START_OUTPUT", 2),
                EchoCommand("SECOND_SESSION_START_OUTPUT")));
        var thread = await CreateThreadAsync(service);

        await DrainAsync(service.SubmitInputAsync(thread.Id, [new TextContent("hello")]));

        var request = Assert.Single(chatClient.CapturedRequests);
        var userMessage = request.Last(message => message.Role == ChatRole.User);
        var text = MessageText(userMessage);
        Assert.Contains("FIRST_SESSION_START_OUTPUT", text);
        Assert.Contains("SECOND_SESSION_START_OUTPUT", text);
    }

    [Fact]
    public async Task SubmitInputAsync_InjectsUserPromptSubmitAdditionalContextIntoPrompt()
    {
        var marker = "USER_PROMPT_SUBMIT_CONTEXT";
        var chatClient = new CapturingChatClient("ok");
        await using var agentFactory = CreateAgentFactory(chatClient);
        var service = CreateService(
            agentFactory,
            chatClient,
            CreateRunner(HookEvent.UserPromptSubmit, JsonAdditionalContextCommand("UserPromptSubmit", marker)));
        var thread = await CreateThreadAsync(service);

        await DrainAsync(service.SubmitInputAsync(thread.Id, [new TextContent("hello")]));

        var request = Assert.Single(chatClient.CapturedRequests);
        var userMessage = request.Last(message => message.Role == ChatRole.User);
        var text = MessageText(userMessage);
        Assert.Contains(marker, text);
        Assert.DoesNotContain("hookSpecificOutput", text);
    }

    [Fact]
    public async Task SubmitInputAsync_UserPromptSubmitExitTwoBlocksBeforeAgent()
    {
        var chatClient = new CapturingChatClient("ok");
        await using var agentFactory = CreateAgentFactory(chatClient);
        var service = CreateService(
            agentFactory,
            chatClient,
            CreateRunner(HookEvent.UserPromptSubmit, StderrAndExitCommand("prompt denied", 2)));
        var thread = await CreateThreadAsync(service);

        await DrainAsync(service.SubmitInputAsync(thread.Id, [new TextContent("hello")]));

        Assert.Empty(chatClient.CapturedRequests);
    }

    private SessionService CreateService(AgentFactory agentFactory, IChatClient chatClient, HookRunner hookRunner)
    {
        var defaultAgent = chatClient.AsAIAgent();
        return new SessionService(
            agentFactory,
            defaultAgent,
            new SessionPersistenceService(new ThreadStore(_tempDir)),
            new SessionGate(),
            hookRunner: hookRunner);
    }

    private AgentFactory CreateAgentFactory(IChatClient chatClient)
    {
        var config = AppConfigTestFactory.CreateOpenAI();
        return new AgentFactory(
            dotcraftPath: _tempDir,
            workspacePath: _tempDir,
            config: config,
            memoryStore: new MemoryStore(_tempDir),
            skillsLoader: new SkillsLoader(_tempDir),
            approvalService: new AutoApproveApprovalService(),
            blacklist: null,
            chatClient: chatClient,
            toolSources: Array.Empty<IToolSource>());
    }

    private HookRunner CreateSessionStartRunner(string output) =>
        CreateSessionStartRunnerFromCommands(EchoCommand(output));

    private HookRunner CreateSessionStartRunnerFromCommands(params string[] commands)
        => CreateRunner(HookEvent.SessionStart, commands);

    private HookRunner CreateRunner(HookEvent evt, params string[] commands)
    {
        var config = new HooksFileConfig
        {
            Hooks =
            {
                [evt.ToString()] =
                [
                    new HookMatcherGroup
                    {
                        Hooks =
                        [
                            ..commands.Select(command => new HookEntry
                            {
                                Type = "command",
                                Command = command,
                                Timeout = 10
                            })
                        ]
                    }
                ]
            }
        };

        return new HookRunner(config, _tempDir);
    }

    private static string EchoCommand(string output) =>
        OperatingSystem.IsWindows()
            ? $"Write-Output '{output}'"
            : $"printf '%s\\n' '{output}'";

    private static string EchoAndExitCommand(string output, int exitCode) =>
        OperatingSystem.IsWindows()
            ? $"Write-Output '{output}'; exit {exitCode}"
            : $"printf '%s\\n' '{output}'; exit {exitCode}";

    private static string StderrAndExitCommand(string output, int exitCode) =>
        OperatingSystem.IsWindows()
            ? $"[Console]::Error.WriteLine('{output}'); exit {exitCode}"
            : $"printf '%s\\n' '{output}' >&2; exit {exitCode}";

    private static string JsonAdditionalContextCommand(string eventName, string output)
    {
        var json = "{\"hookSpecificOutput\":{\"hookEventName\":\"" + eventName + "\",\"additionalContext\":\"" + output + "\"}}";
        return OperatingSystem.IsWindows()
            ? $"Write-Output '{json}'"
            : $"printf '%s\\n' '{json}'";
    }

    private async Task<SessionThread> CreateThreadAsync(SessionService service) =>
        await service.CreateThreadAsync(new SessionIdentity
        {
            ChannelName = "test",
            UserId = "user1",
            WorkspacePath = _tempDir
        });

    private static string MessageText(ChatMessage message) =>
        string.Concat(message.Contents.OfType<TextContent>().Select(content => content.Text));

    private static async Task DrainAsync(IAsyncEnumerable<SessionEvent> events)
    {
        await foreach (var _ in events)
        {
        }
    }

    private sealed class CapturingChatClient(string responseText) : IChatClient
    {
        public List<List<ChatMessage>> CapturedRequests { get; } = [];

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ChatResponse([new ChatMessage(ChatRole.Assistant, [new TextContent(responseText)])]));

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            CapturedRequests.Add(chatMessages.ToList());
            yield return new ChatResponseUpdate(ChatRole.Assistant, [new TextContent(responseText)]);
            await Task.CompletedTask;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
