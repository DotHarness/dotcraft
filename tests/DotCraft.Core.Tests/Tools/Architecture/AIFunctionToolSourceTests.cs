using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Agents;
using DotCraft.Configuration;
using DotCraft.GeneratedTools.Core;
using DotCraft.Modules;
using DotCraft.Tools;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using DotCraft.Tests.Tools;

namespace DotCraft.Core.Tests.Tools.Architecture;

public sealed class AIFunctionToolSourceTests
{
    [Fact]
    public async Task CommitSuggest_ExecutesThroughSnapshotDispatcherWithProviderCallIdentity()
    {
        var source = new CommitSuggestToolSource();
        var planning = CreatePlanningContext();
        var snapshot = await new EffectiveToolSnapshotBuilder().BuildAsync([source], planning);
        var providerName = Assert.Single(snapshot.ProviderFlatNameIndex.Keys);

        var result = await new ToolDispatcher().DispatchProviderFlatCallAsync(
            snapshot,
            providerName,
            new JsonObject { ["summary"] = "Unify tool runtime" },
            new ToolInvocationRequest(
                planning.ThreadId,
                planning.TurnId,
                "call_original",
                ToolInvocationAudience.Model));

        Assert.True(result.Success);
        Assert.Equal("Recorded.", result.Content);
        Assert.Equal(new ToolName(null, CommitSuggestMethods.ToolName), snapshot.ProviderFlatNameIndex[providerName]);
    }

    [Fact]
    public async Task Runtime_PreservesRichContentFromNativeFunction()
    {
        var imageBytes = "native-image"u8.ToArray();
        var function = AIFunctionFactory.Create(
            () => (IList<AIContent>)
            [
                new TextContent("Image: sample.png"),
                new DataContent(imageBytes, "image/png")
            ],
            name: "ReadImage");
        var definitionId = new ToolDefinitionId(
            ToolSourceKind.CoreNative,
            "test",
            new SourceToolId("ReadImage"));
        var result = await new AIFunctionToolRuntime(function).InvokeAsync(
            new ToolInvocationContext(
                "thread_test",
                "turn_test",
                "call_test",
                ToolInvocationAudience.Model,
                new ToolName(null, "ReadImage"),
                definitionId,
                new RuntimeBindingId("native:test:ReadImage:1"),
                1,
                DateTimeOffset.UtcNow),
            new JsonObject());

        Assert.True(result.Success);
        Assert.Equal("Image: sample.png", result.Content);
        var contentItems = Assert.IsAssignableFrom<IReadOnlyList<AIContent>>(result.ContentItems);
        Assert.Equal("Image: sample.png", Assert.IsType<TextContent>(contentItems[0]).Text);
        var image = Assert.IsType<DataContent>(contentItems[1]);
        Assert.Equal("image/png", image.MediaType);
        Assert.Equal(imageBytes, image.Data.ToArray());
    }

    [Fact]
    public async Task Runtime_ImageOnlySuccessGetsStableTextFallback()
    {
        var function = AIFunctionFactory.Create(
            () => (IList<AIContent>)[new DataContent("image"u8.ToArray(), "image/png")],
            name: "ReadImage");
        var definitionId = new ToolDefinitionId(
            ToolSourceKind.CoreNative,
            "test",
            new SourceToolId("ReadImage"));

        var result = await new AIFunctionToolRuntime(function).InvokeAsync(
            new ToolInvocationContext(
                "thread_test",
                "turn_test",
                "call_test",
                ToolInvocationAudience.Model,
                new ToolName(null, "ReadImage"),
                definitionId,
                new RuntimeBindingId("native:test:ReadImage:1"),
                1,
                DateTimeOffset.UtcNow),
            new JsonObject());

        Assert.True(result.Success);
        Assert.Equal("(ReadImage completed with no output)", result.Content);
        Assert.IsType<DataContent>(Assert.Single(result.ContentItems!));
    }

    [Fact]
    public async Task Runtime_ClassifiesArgumentFailuresAsInvalidInput()
    {
        var function = AIFunctionFactory.Create(
            new Func<string>(() => throw new ArgumentException("automation.invalidApprovalPolicy")),
            name: "Automation");
        var definitionId = new ToolDefinitionId(
            ToolSourceKind.CoreNative,
            "test",
            new SourceToolId("Automation"));

        var result = await new AIFunctionToolRuntime(function).InvokeAsync(
            new ToolInvocationContext(
                "thread_test",
                "turn_test",
                "call_test",
                ToolInvocationAudience.Model,
                new ToolName(null, "Automation"),
                definitionId,
                new RuntimeBindingId("native:test:Automation:1"),
                1,
                DateTimeOffset.UtcNow),
            new JsonObject());

        Assert.False(result.Success);
        Assert.Equal(ToolErrorCodes.InputInvalid, result.Error?.Code);
        Assert.Equal("automation.invalidApprovalPolicy", result.Error?.Message);
    }

    [Fact]
    public async Task Source_ProjectsGeneratedResultAndStreamingMetadata()
    {
        var function = GeneratedToolFunctions.ShellTools_Exec(
            new ShellTools(
                Path.GetTempPath(),
                new StubBackgroundTerminalService(),
                requireApprovalOutsideWorkspace: false));
        var snapshot = await new EffectiveToolSnapshotBuilder().BuildAsync(
            [new GeneratedFunctionSource(function)],
            CreatePlanningContext());
        var definition = Assert.Single(snapshot.ModelVisibleDefinitions);

        Assert.Equal(30_000, definition.Annotations["dotcraft/maxResultChars"].GetInt32());
        Assert.True(definition.Annotations["dotcraft/streamArguments"].GetBoolean());
        Assert.Equal("core.shell", definition.Presentation?.Id.Value);
        var projected = Assert.IsAssignableFrom<IGeneratedToolMetadata>(
            Assert.Single(AgentFactory.ProjectSnapshotTools(snapshot)));
        Assert.Equal(30_000, projected.MaxResultChars);
        Assert.True(projected.StreamArgumentsEnabled);
    }

    [Fact]
    public void ProfileRegistry_StoresQualifiedToolSources()
    {
        var registry = new ToolProfileRegistry();
        var source = new CommitSuggestToolSource();

        registry.Register("commit", [source]);

        Assert.True(registry.TryGet("commit", out var sources));
        Assert.Same(source, Assert.Single(sources!));
    }

    [Fact]
    public void ToolSourceCollector_CombinesDiAndEnabledModulesDeterministically()
    {
        var first = new EmptySource("first", priority: 20);
        var second = new EmptySource("second", priority: 10);
        var services = new ServiceCollection()
            .AddSingleton<IToolSource>(first)
            .BuildServiceProvider();
        var modules = new ModuleRegistry();
        modules.RegisterModule(new SourceModule(second));

        var sources = new ToolSourceCollector(modules, services, new AppConfig()).Collect();

        Assert.Equal(["second", "first"], sources.Select(source => source.SourceId));
    }

    [Fact]
    public void A_tool_returning_content_items_says_so_and_a_value_tool_does_not()
    {
        Assert.True(GeneratedToolDeclarations.FileTools_ReadFile_Declaration.ReturnsContent);
        Assert.False(GeneratedToolDeclarations.FileTools_GrepFiles_Declaration.ReturnsContent);
    }

    [Fact]
    public void FirstPartyTools_DeclareTheirDesktopPresentation()
    {
        AssertPresentation(GeneratedToolDeclarations.FileTools_ReadFile_Declaration, "core.read-file", ("operation", "read"));
        AssertPresentation(GeneratedToolDeclarations.LspTool_LSP_Declaration, "core.lsp");
        AssertPresentation(GeneratedToolDeclarations.UserCoordinationTools_Sleep_Declaration, "core.sleep");
        AssertPresentation(GeneratedToolDeclarations.AgentMessageBoardTools_BoardPost_Declaration, "core.board", ("operation", "post"));
        AssertPresentation(GeneratedToolDeclarations.RemoteToolHostTransferTools_Transfer_Declaration, "core.remote-tool-host", ("operation", "transfer"));
        AssertPresentation(GeneratedToolDeclarations.AgentProfileBuilderToolMethods_RemoveAgentSkills_Declaration, "core.agent-builder", ("field", "skills.allow"));
        Assert.Null(GeneratedToolDeclarations.UserCoordinationTools_CurrentTime_Declaration.Presentation);
    }

    private static void AssertPresentation(
        GeneratedToolDeclaration declaration,
        string presentationId,
        params (string Key, string Value)[] options)
    {
        Assert.NotNull(declaration.Presentation);
        Assert.Equal(presentationId, declaration.Presentation.Id.Value);
        Assert.Equal(
            options.ToDictionary(option => option.Key, option => option.Value),
            declaration.Presentation.Options.ToDictionary(pair => pair.Key, pair => pair.Value.GetString()!));
    }

    [Theory]
    [InlineData(ToolPlanningThreadKind.UserTopLevel, 3, true)]
    [InlineData(ToolPlanningThreadKind.SubAgentChild, 2, false)]
    [InlineData(ToolPlanningThreadKind.Internal, 0, false)]
    public async Task UserCoordinationSource_UsesDefaultThreadBoundaries(
        ToolPlanningThreadKind threadKind,
        int expectedCount,
        bool hasAsyncMessage)
    {
        var planning = CreatePlanningContext(threadKind);
        var registrations = await new UserCoordinationToolSource().GetRegistrationsAsync(planning);

        Assert.Equal(expectedCount, registrations.Count);
        Assert.Equal(
            hasAsyncMessage,
            registrations.Any(registration =>
                registration.Definition.Name == new ToolName(null, nameof(UserCoordinationTools.SendUserMessageAsync))));
        Assert.All(registrations, registration =>
            Assert.Equal(ToolPolicyScope.RuntimeManaged, registration.Definition.PolicyScope));
        if (hasAsyncMessage)
        {
            var send = registrations.Single(registration =>
                registration.Definition.Name.Name == nameof(UserCoordinationTools.SendUserMessageAsync));
            Assert.Equal(ToolExposure.DirectModelOnly, send.Exposure);
            Assert.Equal("core.send-user-message-async", send.Definition.Presentation?.Id.Value);
        }
        if (threadKind != ToolPlanningThreadKind.Internal)
        {
            Assert.Contains(registrations, registration =>
                registration.Definition.Name == new ToolName("clock", nameof(UserCoordinationTools.Sleep)));
            Assert.Contains(registrations, registration =>
                registration.Definition.Name == new ToolName("clock", nameof(UserCoordinationTools.CurrentTime)));
            var snapshot = await new EffectiveToolSnapshotBuilder().BuildAsync(
                [new UserCoordinationToolSource()],
                planning);
            Assert.DoesNotContain(snapshot.ProviderFlatNameIndex.Keys, name =>
                name is "send_user_message_async" or "sleep" or "current_time");
        }
    }

    private static ToolInvocationContext Context(string toolName) => new(
        "thread_test",
        "turn_test",
        "call_test",
        ToolInvocationAudience.Model,
        new ToolName(null, toolName),
        new ToolDefinitionId(ToolSourceKind.CoreNative, "test", new SourceToolId(toolName)),
        new RuntimeBindingId($"native:test:{toolName}:1"),
        1,
        DateTimeOffset.UtcNow);

    private static ToolPlanningContext CreatePlanningContext(
        ToolPlanningThreadKind threadKind = ToolPlanningThreadKind.Unknown) => new(
        "thread_test",
        "turn_test",
        Path.GetTempPath(),
        Path.Combine(Path.GetTempPath(), ".craft"),
        "agent",
        "commit",
        providerCapabilities: [],
        revision: 1,
        threadKind: threadKind);

    private sealed class GeneratedFunctionSource(AIFunction function) : AIFunctionToolSource
    {
        public override string SourceId => "generated-test";

        protected override IEnumerable<AIFunction> CreateFunctions(ToolPlanningContext context) => [function];
    }

    private sealed class EmptySource(string sourceId, int priority) : AIFunctionToolSource
    {
        public override string SourceId => sourceId;
        public override int Priority => priority;
        protected override IEnumerable<AIFunction> CreateFunctions(ToolPlanningContext context) => [];
    }

    private sealed class SourceModule(IToolSource source) : ModuleBase, IToolSourceModule
    {
        public override string Name => "test-source";
        public override bool IsEnabled(AppConfig config) => true;
        public IEnumerable<IToolSource> GetToolSources(IServiceProvider services) => [source];
    }
}
