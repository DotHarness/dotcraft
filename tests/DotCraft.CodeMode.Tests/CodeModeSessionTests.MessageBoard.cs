using System.Text.Json;
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

public sealed partial class CodeModeSessionTests
{
    [Theory]
    [InlineData(AppConfig.CodeModeSetting.On)]
    [InlineData(AppConfig.CodeModeSetting.Only)]
    public async Task MessageBoard_NestedCallsHaveDistinctPostIdentities(AppConfig.CodeModeSetting mode)
    {
        _ = typeof(CommandLineArgs).Assembly;
        var config = AppConfigTestFactory.CreateOpenAI();
        config.Tools.CodeMode.Mode = mode;
        var client = new ExecChatClient("""
            await tools.BoardCreateChannel({channelName: 'work'});
            const posts = await Promise.all([
                tools.BoardPost({channelName: 'work', text: 'same text'}),
                tools.BoardPost({channelName: 'work', text: 'same text'})
            ]);
            const ids = posts.map(x => JSON.parse(x).messageId);
            const page = JSON.parse(await tools.BoardSearchPosts({query: 'same text'}));
            text({count: page.nReturned, distinct: ids[0] !== ids[1]});
            """);
        var recorder = new ToolInvocationRecorderRouter();
        var dispatcher = new ToolDispatcher(recorder: recorder);
        await using var host = new CodeModeWorkerHost(new ManagedChildProcessFactory(), _tempDir, new CodeModeLimits());
        await using var factory = new AgentFactory(
            dotcraftPath: _tempDir, workspacePath: _tempDir, config: config,
            memoryStore: new MemoryStore(_tempDir), skillsLoader: new SkillsLoader(_tempDir),
            approvalService: new SessionScopedApprovalService(new AutoApproveApprovalService()), blacklist: null,
            chatClientRegistry: TestModelProviderRegistry.Create(), chatClient: client,
            toolDispatcher: dispatcher, toolSources: [new BoardFunctions()]);
        var service = new SessionService(factory, new StreamingFunctionInvokingChatClient(client).AsAIAgent(),
            new SessionPersistenceService(new ThreadStore(_tempDir)), new SessionGate(),
            toolSnapshotFinalizers: [new CodeModeToolFinalizer(() => config, host, new CodeModeStore(), dispatcher)]);
        recorder.Bind(service);
        var thread = await service.CreateThreadAsync(new SessionIdentity { ChannelName = "test", UserId = "u", WorkspacePath = _tempDir });
        await service.RefreshThreadAgentAsync(thread.Id);
        await foreach (var _ in service.SubmitInputAsync(thread.Id, [new TextContent("publish twice")])) { }
        var turn = Assert.Single((await service.GetThreadAsync(thread.Id)).Turns);
        Assert.True(turn.Status == TurnStatus.Completed, turn.Error);
        Assert.Contains("\"count\":2", client.ExecResult?.Result?.ToString()?.Replace(" ", ""));
        Assert.Contains("\"distinct\":true", client.ExecResult?.Result?.ToString()?.Replace(" ", ""));
        var posts = turn.Items.Where(i => i.Payload is ToolResultPayload { ToolName: "BoardPost" }).ToArray();
        Assert.Equal(2, posts.Length);
        Assert.Equal(2, posts.Select(i => i.AsToolResult!.CallId).Distinct().Count());
        foreach (var item in posts)
        {
            Assert.Equal(ToolInvocationOrigin.CodeModeKind, item.InvocationOrigin);
            using var parsed = JsonDocument.Parse(item.AsToolResult!.Result!);
            Assert.Equal("/root", parsed.RootElement.GetProperty("author").GetString());
        }
    }

    private sealed class BoardFunctions : AIFunctionToolSource
    {
        public override string SourceId => "core-native";
        protected override IEnumerable<AIFunction> CreateFunctions(ToolPlanningContext context)
        {
            var tools = new AgentMessageBoardTools();
            foreach (var name in new[] { "BoardCreateChannel", "BoardPost", "BoardSearchPosts" })
                yield return AIFunctionFactory.Create(typeof(AgentMessageBoardTools).GetMethod(name)!, tools);
        }
    }
}
