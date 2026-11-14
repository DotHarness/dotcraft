using System.Text.Json;
using DotCraft.Agents;
using DotCraft.AppServer;
using DotCraft.CLI;
using DotCraft.Configuration;
using DotCraft.Memory;
using DotCraft.Processes;
using DotCraft.Security;
using DotCraft.Sessions;
using DotCraft.Sessions.Wire;
using DotCraft.Skills;
using DotCraft.Tests;
using DotCraft.Tools;
using Microsoft.Extensions.AI;
using Contract = DotCraft.Protocol.AppServer;

namespace DotCraft.CodeMode.Tests;

public sealed partial class CodeModeSessionTests
{
    [Fact]
    public async Task NestedFileRead_ProjectsActualParentCallProvenanceToLiveAndHistoricalWire()
    {
        _ = typeof(CommandLineArgs).Assembly;
        var path = Path.Combine(_tempDir, "fixture.txt");
        await File.WriteAllTextAsync(path, "fixture contents");
        var config = AppConfigTestFactory.CreateOpenAI();
        config.Tools.CodeMode.Mode = AppConfig.CodeModeSetting.On;
        using var model = new ExecChatClient("text(await tools.ReadFixture({}));");
        var recorder = new ToolInvocationRecorderRouter();
        var dispatcher = new ToolDispatcher(recorder: recorder);
        await using var host = new CodeModeWorkerHost(new ManagedChildProcessFactory(), _tempDir, new CodeModeLimits());
        await using var factory = new AgentFactory(
            dotcraftPath: _tempDir,
            workspacePath: _tempDir,
            config: config,
            memoryStore: new MemoryStore(_tempDir),
            skillsLoader: new SkillsLoader(_tempDir),
            approvalService: new SessionScopedApprovalService(new AutoApproveApprovalService()),
            blacklist: null,
            chatClientRegistry: TestModelProviderRegistry.Create(),
            chatClient: model,
            toolDispatcher: dispatcher,
            toolSources: [new FixtureReadSource(path)]);
        using var service = new SessionService(
            factory,
            new StreamingFunctionInvokingChatClient(model).AsAIAgent(),
            new SessionPersistenceService(new ThreadStore(_tempDir)),
            new SessionGate(),
            toolSnapshotFinalizers: [new CodeModeToolFinalizer(() => config, host, new CodeModeStore(), dispatcher)]);
        recorder.Bind(service);
        var thread = await service.CreateThreadAsync(new SessionIdentity
        {
            ChannelName = "test",
            UserId = "reader",
            WorkspacePath = _tempDir
        });
        await service.RefreshThreadAgentAsync(thread.Id);
        var events = new List<SessionEvent>();

        await foreach (var evt in service.SubmitInputAsync(thread.Id, [new TextContent("read fixture")]))
            events.Add(evt);

        var items = Assert.Single((await service.GetThreadAsync(thread.Id)).Turns).Items;
        var parent = Assert.Single(items, item => item.AsToolCall?.ToolName == "CodeMode");
        var parentCallId = parent.AsToolCall!.CallId;
        var read = Assert.Single(items, item => item.AsToolCall?.ToolName == "ReadFixture");
        var result = Assert.Single(items, item => item.AsToolResult?.ToolName == "ReadFixture");
        Assert.NotEqual(parent.Id, parentCallId);
        Assert.NotEqual(parentCallId, read.AsToolCall!.CallId);
        Assert.Equal(read.AsToolCall.CallId, result.AsToolResult!.CallId);
        Assert.True(result.AsToolResult.Success);
        Assert.Contains("fixture contents", result.AsToolResult.Result);
        AssertProvenance(read, parentCallId);
        AssertProvenance(result, parentCallId);
        var live = events.Where(evt => evt.EventType == SessionEventType.ItemCompleted
            && (evt.ItemId == read.Id || evt.ItemId == result.Id)).ToArray();
        Assert.Equal(2, live.Length);
        Assert.All(live, evt => AssertProvenance(evt.ItemPayload!, parentCallId));
        Assert.Null(AppServerContractMapper.ToContract(parent.ToWire()).InvocationOrigin);
        Assert.Null(AppServerContractMapper.ToContract(parent.ToWire()).InvocationSourceId);

        await using var reloadedStore = new ThreadStore(_tempDir);
        var reloaded = await reloadedStore.LoadThreadAsync(thread.Id);
        Assert.All(Assert.Single(reloaded!.Turns).Items.Where(item => item.Id == read.Id || item.Id == result.Id),
            item => AssertProvenance(item, parentCallId));
    }

    private static void AssertProvenance(SessionItem item, string parentCallId)
    {
        var wire = item.ToWire();
        Assert.Equal(ToolInvocationOrigin.CodeModeKind, wire.InvocationOrigin);
        Assert.Equal(parentCallId, wire.InvocationSourceId);
        var contract = AppServerContractMapper.ToContract(wire);
        var json = JsonSerializer.SerializeToElement(contract, SessionWireJsonOptions.Default);
        Assert.Equal(ToolInvocationOrigin.CodeModeKind, json.GetProperty("invocationOrigin").GetString());
        Assert.Equal(parentCallId, json.GetProperty("invocationSourceId").GetString());
        var parsed = json.Deserialize<Contract.SessionItem>(SessionWireJsonOptions.Default)!;
        Assert.Equal(parentCallId, parsed.InvocationSourceId);
    }

    private sealed class FixtureReadSource(string path) : AIFunctionToolSource
    {
        public override string SourceId => "fixture-read";

        protected override IEnumerable<AIFunction> CreateFunctions(ToolPlanningContext context)
        {
            yield return AIFunctionFactory.Create(
                (CancellationToken ct) => File.ReadAllTextAsync(path, ct),
                name: "ReadFixture",
                description: "Read the fixture file.");
        }
    }
}
