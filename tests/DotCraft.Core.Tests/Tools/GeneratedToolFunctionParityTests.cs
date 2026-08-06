using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Agents;
using DotCraft.Configuration;
using DotCraft.Cron;
using DotCraft.GeneratedTools.Core;
using DotCraft.Hosting;
using DotCraft.Lsp;
using DotCraft.Memory;
using DotCraft.Skills;
using DotCraft.Tools;
using DotCraft.Tools.BackgroundTerminals;
using DotCraft.Tools.Sandbox;
using Microsoft.Extensions.AI;
using Xunit;
using DeferredToolRegistry = DotCraft.Tools.DeferredToolActivationIndex;

namespace DotCraft.Tests.Tools;

public sealed class GeneratedToolFunctionParityTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), $"generated_tools_{Guid.NewGuid():N}");
    private readonly List<IDisposable> _disposables = [];
    private readonly List<IAsyncDisposable> _asyncDisposables = [];

    public GeneratedToolFunctionParityTests()
    {
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        foreach (var disposable in _disposables)
            disposable.Dispose();
        foreach (var asyncDisposable in _asyncDisposables)
            asyncDisposable.DisposeAsync().AsTask().GetAwaiter().GetResult();

        try
        {
            if (Directory.Exists(_tempRoot))
                Directory.Delete(_tempRoot, recursive: true);
        }
        catch
        {
            // Best-effort cleanup on Windows.
        }
    }

    [Fact]
    public void GeneratedWrappers_MatchAIFunctionFactorySchemas_ForReplacedProductionTools()
    {
        foreach (var pair in CreateSchemaParityPairs())
            AssertFunctionShape(pair);
    }

    [Fact]
    public async Task GeneratedWrappers_InvokeLikeAIFunctionFactory_ForRepresentativeSignatures()
    {
        await AssertInvocationMatchesAsync(
            GeneratedToolFunctions.CommitSuggestMethods_CommitSuggest(),
            AIFunctionFactory.Create(CommitSuggestMethods.CommitSuggest),
            new AIFunctionArguments
            {
                ["summary"] = "feat: add generated tools",
                ["body"] = "Replace reflection wrappers."
            });

        var generatedPlan = CreatePlanTools("generated-plan");
        var factoryPlan = CreatePlanTools("factory-plan");
        var planArgs = new AIFunctionArguments
        {
            ["plan"] = "# Generated Plan\n\n## Summary\n\nKeep schema stable.",
            ["todos"] = JsonSerializer.SerializeToElement(new[]
            {
                new { id = "schema-parity", content = "Compare generated and factory schemas" }
            })
        };
        await AssertInvocationMatchesAsync(
            GeneratedToolFunctions.PlanTools_CreatePlan(generatedPlan),
            AIFunctionFactory.Create(factoryPlan.CreatePlan),
            planArgs);

        var generatedBuilder = CreateAgentBuilderMethods("generated-builder");
        var factoryBuilder = CreateAgentBuilderMethods("factory-builder");
        var arrayArgs = new AIFunctionArguments
        {
            ["names"] = JsonSerializer.SerializeToElement(new[] { "ReadFile", "WriteFile" })
        };
        await AssertInvocationMatchesAsync(
            GeneratedToolFunctions.AgentProfileBuilderToolMethods_AddAgentTools(generatedBuilder),
            AIFunctionFactory.Create(factoryBuilder.AddAgentTools),
            arrayArgs);

        var generatedCron = CreateCronTools();
        var factoryCron = CreateCronTools();
        await AssertInvocationMatchesAsync(
            GeneratedToolFunctions.CronTools_Cron(generatedCron),
            AIFunctionFactory.Create(factoryCron.Cron),
            new AIFunctionArguments { ["action"] = "list" });
    }

    [Fact]
    public void GeneratedMetadata_PreservesResultLimitAndStreamArgumentsPolicy()
    {
        var shellTools = new ShellTools(
            _tempRoot,
            new StubBackgroundTerminalService(),
            requireApprovalOutsideWorkspace: false);
        var exec = GeneratedToolFunctions.ShellTools_Exec(shellTools);
        var skillManage = GeneratedToolFunctions.SkillManageTool_SkillManage(CreateSkillManageTool());

        Assert.True(GeneratedToolMetadataResolver.TryGet(exec, out var execMetadata));
        Assert.Equal(30_000, execMetadata.MaxResultChars);
        Assert.True(execMetadata.StreamArgumentsEnabled);

        Assert.True(GeneratedToolMetadataResolver.TryGet(skillManage, out var skillMetadata));
        Assert.False(skillMetadata.StreamArgumentsEnabled);
        Assert.DoesNotContain(skillManage.Name, AgentFactory.BuildStreamOptOutToolNames([]));
        Assert.Contains(skillManage.Name, AgentFactory.BuildStreamOptOutToolNames([skillManage]));
    }

    [Fact]
    public async Task ShellExec_CancellationToken_IsInfrastructureOnlyAndReachesRuntime()
    {
        CancellationToken observedToken = default;
        var terminals = new StubBackgroundTerminalService
        {
            StartHandler = (request, token) =>
            {
                observedToken = token;
                return Task.FromResult(new BackgroundTerminalSnapshot
                {
                    SessionId = "term_token",
                    ThreadId = request.ThreadId,
                    Command = request.Command,
                    WorkingDirectory = request.WorkingDirectory,
                    Status = BackgroundTerminalStatus.Completed,
                    Output = "ok",
                    OutputPath = Path.Combine(request.WorkingDirectory, "term_token.log"),
                    ExitCode = 0,
                    StartedAt = DateTimeOffset.UtcNow,
                    CompletedAt = DateTimeOffset.UtcNow,
                    OriginalOutputChars = 2
                });
            }
        };
        var function = GeneratedToolFunctions.ShellTools_Exec(new ShellTools(_tempRoot, terminals));
        using var cts = new CancellationTokenSource();

        await function.InvokeAsync(
            new AIFunctionArguments { ["command"] = "echo token" },
            cts.Token);

        Assert.Equal(cts.Token, observedToken);
        Assert.DoesNotContain("cancellationToken", function.JsonSchema.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    private IReadOnlyList<FunctionPair> CreateSchemaParityPairs()
    {
        var fileTools = new FileTools(_tempRoot, requireApprovalOutsideWorkspace: false);
        var shellTools = new ShellTools(
            _tempRoot,
            new StubBackgroundTerminalService(),
            timeoutSeconds: 1,
            requireApprovalOutsideWorkspace: false);
        var webTools = new WebTools();
        var lspManager = new LspServerManager(
            new AppConfig(),
            new DotCraftPaths { WorkspacePath = _tempRoot, CraftPath = Path.Combine(_tempRoot, ".craft") });
        _asyncDisposables.Add(lspManager);
        var lspTool = new LspTool(_tempRoot, lspManager, requireApprovalOutsideWorkspace: false);
        var planTools = CreatePlanTools("schema-plan");
        var requestInputTools = new RequestUserInputTools();
        var goalMethods = new GoalToolMethods();
        var agentTools = new AgentTools();
        var searchTool = new ToolSearchTool(new DeferredToolRegistry(
        [
            AIFunctionFactory.Create(() => "ok", name: "DeferredExample", description: "Deferred example.")
        ]));
        var welcomeMethods = new WelcomeSuggestionToolMethods(new MemoryStore(_tempRoot));
        var builderMethods = CreateAgentBuilderMethods("schema-builder");
        var skillView = new SkillViewTool(new SkillsLoader(_tempRoot), variantModeEnabled: false, new SkillVariantTarget());
        var skillManage = CreateSkillManageTool();
        var cronTools = CreateCronTools();
        var sandboxManager = new SandboxSessionManager(new AppConfig.SandboxConfig { IdleTimeoutSeconds = 0 }, _tempRoot);
        _asyncDisposables.Add(sandboxManager);
        var sandboxFileTools = new SandboxFileTools(sandboxManager);
        var sandboxShellTools = new SandboxShellTools(new StubSandboxCommandClient());

        return
        [
            Pair(GeneratedToolFunctions.FileTools_ReadFile(fileTools), AIFunctionFactory.Create(fileTools.ReadFile)),
            Pair(GeneratedToolFunctions.FileTools_WriteFile(fileTools), AIFunctionFactory.Create(fileTools.WriteFile)),
            Pair(GeneratedToolFunctions.FileTools_EditFile(fileTools), AIFunctionFactory.Create(fileTools.EditFile)),
            Pair(GeneratedToolFunctions.FileTools_GrepFiles(fileTools), AIFunctionFactory.Create(fileTools.GrepFiles)),
            Pair(GeneratedToolFunctions.FileTools_FindFiles(fileTools), AIFunctionFactory.Create(fileTools.FindFiles)),
            Pair(GeneratedToolFunctions.ShellTools_Exec(shellTools), AIFunctionFactory.Create(shellTools.Exec)),
            Pair(GeneratedToolFunctions.ShellTools_WriteStdin(shellTools), AIFunctionFactory.Create(shellTools.WriteStdin)),
            Pair(GeneratedToolFunctions.WebTools_WebSearch(webTools), AIFunctionFactory.Create(webTools.WebSearch)),
            Pair(GeneratedToolFunctions.WebTools_WebFetch(webTools), AIFunctionFactory.Create(webTools.WebFetch)),
            Pair(GeneratedToolFunctions.LspTool_LSP(lspTool), AIFunctionFactory.Create(lspTool.LSP)),
            Pair(GeneratedToolFunctions.PlanTools_CreatePlan(planTools), AIFunctionFactory.Create(planTools.CreatePlan)),
            Pair(GeneratedToolFunctions.PlanTools_UpdateTodos(planTools), AIFunctionFactory.Create(planTools.UpdateTodos)),
            Pair(GeneratedToolFunctions.PlanTools_TodoWrite(planTools), AIFunctionFactory.Create(planTools.TodoWrite)),
            Pair(GeneratedToolFunctions.RequestUserInputTools_RequestUserInput(requestInputTools), AIFunctionFactory.Create(requestInputTools.RequestUserInput)),
            Pair(GeneratedToolFunctions.GoalToolMethods_GetGoal(goalMethods), AIFunctionFactory.Create(goalMethods.GetGoal)),
            Pair(GeneratedToolFunctions.GoalToolMethods_CreateGoal(goalMethods), AIFunctionFactory.Create(goalMethods.CreateGoal)),
            Pair(GeneratedToolFunctions.GoalToolMethods_UpdateGoal(goalMethods), AIFunctionFactory.Create(goalMethods.UpdateGoal)),
            Pair(GeneratedToolFunctions.AgentTools_SpawnAgent(agentTools), AIFunctionFactory.Create(agentTools.SpawnAgent)),
            Pair(GeneratedToolFunctions.AgentTools_SendMessage(agentTools), AIFunctionFactory.Create(agentTools.SendMessage)),
            Pair(GeneratedToolFunctions.AgentTools_FollowupTask(agentTools), AIFunctionFactory.Create(agentTools.FollowupTask)),
            Pair(GeneratedToolFunctions.AgentTools_WaitAgent(agentTools), AIFunctionFactory.Create(agentTools.WaitAgent)),
            Pair(GeneratedToolFunctions.AgentTools_ListAgents(agentTools), AIFunctionFactory.Create(agentTools.ListAgents)),
            Pair(GeneratedToolFunctions.AgentTools_CloseAgent(agentTools), AIFunctionFactory.Create(agentTools.CloseAgent)),
            Pair(GeneratedToolFunctions.ToolSearchTool_SearchTools(searchTool), AIFunctionFactory.Create(searchTool.SearchTools)),
            Pair(GeneratedToolFunctions.WelcomeSuggestionToolMethods_ReadWelcomeWorkspaceMemory(welcomeMethods), AIFunctionFactory.Create(welcomeMethods.ReadWelcomeWorkspaceMemory)),
            Pair(GeneratedToolFunctions.WelcomeSuggestionToolMethods_EmitWelcomeSuggestions(welcomeMethods), AIFunctionFactory.Create(welcomeMethods.EmitWelcomeSuggestions)),
            Pair(GeneratedToolFunctions.AgentProfileBuilderToolMethods_SetAgentName(builderMethods), AIFunctionFactory.Create(builderMethods.SetAgentName)),
            Pair(GeneratedToolFunctions.AgentProfileBuilderToolMethods_SetAgentDescription(builderMethods), AIFunctionFactory.Create(builderMethods.SetAgentDescription)),
            Pair(GeneratedToolFunctions.AgentProfileBuilderToolMethods_SetAgentInstructions(builderMethods), AIFunctionFactory.Create(builderMethods.SetAgentInstructions)),
            Pair(GeneratedToolFunctions.AgentProfileBuilderToolMethods_AppendAgentInstructions(builderMethods), AIFunctionFactory.Create(builderMethods.AppendAgentInstructions)),
            Pair(GeneratedToolFunctions.AgentProfileBuilderToolMethods_AddAgentTools(builderMethods), AIFunctionFactory.Create(builderMethods.AddAgentTools)),
            Pair(GeneratedToolFunctions.AgentProfileBuilderToolMethods_RemoveAgentTools(builderMethods), AIFunctionFactory.Create(builderMethods.RemoveAgentTools)),
            Pair(GeneratedToolFunctions.AgentProfileBuilderToolMethods_SetAgentToolControl(builderMethods), AIFunctionFactory.Create(builderMethods.SetAgentToolControl)),
            Pair(GeneratedToolFunctions.AgentProfileBuilderToolMethods_AddAgentSkills(builderMethods), AIFunctionFactory.Create(builderMethods.AddAgentSkills)),
            Pair(GeneratedToolFunctions.AgentProfileBuilderToolMethods_RemoveAgentSkills(builderMethods), AIFunctionFactory.Create(builderMethods.RemoveAgentSkills)),
            Pair(GeneratedToolFunctions.AgentProfileBuilderToolMethods_AddAgentMcpServers(builderMethods), AIFunctionFactory.Create(builderMethods.AddAgentMcpServers)),
            Pair(GeneratedToolFunctions.AgentProfileBuilderToolMethods_RemoveAgentMcpServers(builderMethods), AIFunctionFactory.Create(builderMethods.RemoveAgentMcpServers)),
            Pair(GeneratedToolFunctions.AgentProfileBuilderToolMethods_SetAgentProviderPreference(builderMethods), AIFunctionFactory.Create(builderMethods.SetAgentProviderPreference)),
            Pair(GeneratedToolFunctions.AgentProfileBuilderToolMethods_ClearAgentProviderPreference(builderMethods), AIFunctionFactory.Create(builderMethods.ClearAgentProviderPreference)),
            Pair(GeneratedToolFunctions.AgentProfileBuilderToolMethods_SetAgentApproval(builderMethods), AIFunctionFactory.Create(builderMethods.SetAgentApproval)),
            Pair(GeneratedToolFunctions.SkillViewTool_SkillView(skillView), AIFunctionFactory.Create(skillView.SkillView)),
            Pair(GeneratedToolFunctions.SkillManageTool_SkillManage(skillManage), AIFunctionFactory.Create(skillManage.SkillManage)),
            Pair(GeneratedToolFunctions.CommitSuggestMethods_CommitSuggest(), AIFunctionFactory.Create(CommitSuggestMethods.CommitSuggest)),
            Pair(GeneratedToolFunctions.CronTools_Cron(cronTools), AIFunctionFactory.Create(cronTools.Cron)),
            Pair(GeneratedToolFunctions.SandboxFileTools_ReadFile(sandboxFileTools), AIFunctionFactory.Create(sandboxFileTools.ReadFile)),
            Pair(GeneratedToolFunctions.SandboxFileTools_WriteFile(sandboxFileTools), AIFunctionFactory.Create(sandboxFileTools.WriteFile)),
            Pair(GeneratedToolFunctions.SandboxFileTools_EditFile(sandboxFileTools), AIFunctionFactory.Create(sandboxFileTools.EditFile)),
            Pair(GeneratedToolFunctions.SandboxFileTools_GrepFiles(sandboxFileTools), AIFunctionFactory.Create(sandboxFileTools.GrepFiles)),
            Pair(GeneratedToolFunctions.SandboxFileTools_FindFiles(sandboxFileTools), AIFunctionFactory.Create(sandboxFileTools.FindFiles)),
            Pair(GeneratedToolFunctions.SandboxShellTools_Exec(sandboxShellTools), AIFunctionFactory.Create(sandboxShellTools.Exec))
        ];
    }

    private PlanTools CreatePlanTools(string sessionId)
    {
        var planRoot = Path.Combine(_tempRoot, sessionId);
        Directory.CreateDirectory(planRoot);
        return new PlanTools(new PlanStore(planRoot), () => sessionId);
    }

    private AgentProfileBuilderToolMethods CreateAgentBuilderMethods(string threadId)
    {
        ProfileBuilderDraftStore.Remove(threadId);
        ProfileBuilderDraftStore.Seed(threadId, "test-agent", AgentProfileSources.Workspace, string.Empty);
        return new AgentProfileBuilderToolMethods(threadId, skillsLoader: null, mcpClientManager: null);
    }

    private CronTools CreateCronTools()
    {
        var path = Path.Combine(_tempRoot, $"cron_{Guid.NewGuid():N}.json");
        var service = new CronService(path);
        _disposables.Add(service);
        return new CronTools(service);
    }

    private SkillManageTool CreateSkillManageTool() =>
        new(new NoOpSkillMutationApplier(), new AppConfig.SelfLearningConfig());

    private static FunctionPair Pair(AIFunction generated, AIFunction factory) =>
        new(generated.Name, generated, factory);

    private static void AssertFunctionShape(FunctionPair pair)
    {
        Assert.Equal(pair.Factory.Name, pair.Generated.Name);
        Assert.Equal(pair.Factory.Description, pair.Generated.Description);
        AssertJsonEqual(pair.Factory.JsonSchema, pair.Generated.JsonSchema, $"{pair.Name} raw input schema");
        AssertJsonEqual(
            ToolSchemaSanitizer.SanitizeJsonSchema(pair.Factory.JsonSchema),
            ToolSchemaSanitizer.SanitizeJsonSchema(pair.Generated.JsonSchema),
            $"{pair.Name} input schema");
        AssertNullableJsonEqual(pair.Factory.ReturnJsonSchema, pair.Generated.ReturnJsonSchema, $"{pair.Name} return schema");
        Assert.Same(pair.Factory.JsonSerializerOptions, pair.Generated.JsonSerializerOptions);
        Assert.NotNull(pair.Factory.UnderlyingMethod);
        Assert.Null(pair.Generated.UnderlyingMethod);
    }

    private static async Task AssertInvocationMatchesAsync(
        AIFunction generated,
        AIFunction factory,
        AIFunctionArguments arguments)
    {
        var generatedResult = await generated.InvokeAsync(arguments);
        var factoryResult = await factory.InvokeAsync(arguments);

        Assert.Equal(factoryResult?.GetType(), generatedResult?.GetType());
        Assert.Equal(
            JsonSerializer.SerializeToElement(factoryResult, factoryResult?.GetType() ?? typeof(object)).GetRawText(),
            JsonSerializer.SerializeToElement(generatedResult, generatedResult?.GetType() ?? typeof(object)).GetRawText());
    }

    private static void AssertNullableJsonEqual(JsonElement? expected, JsonElement? actual, string because)
    {
        Assert.Equal(expected.HasValue, actual.HasValue);
        if (expected.HasValue)
            AssertJsonEqual(expected.Value, actual!.Value, because);
    }

    private static void AssertJsonEqual(JsonElement expected, JsonElement actual, string because)
    {
        var expectedNode = JsonNode.Parse(expected.GetRawText());
        var actualNode = JsonNode.Parse(actual.GetRawText());
        Assert.True(JsonNode.DeepEquals(expectedNode, actualNode), $"{because}\nExpected: {expected}\nActual:   {actual}");
    }

    private sealed record FunctionPair(string Name, AIFunction Generated, AIFunction Factory);

    private sealed class NoOpSkillMutationApplier : ISkillMutationApplier
    {
        public Task<SkillMutationResult> CreateAsync(SkillCreateRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(SkillMutationResult.Ok("created"));

        public Task<SkillMutationResult> EditAsync(SkillEditRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(SkillMutationResult.Ok("edited"));

        public Task<SkillMutationResult> PatchAsync(SkillPatchRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(SkillMutationResult.Ok("patched"));

        public Task<SkillMutationResult> DeleteAsync(SkillDeleteRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(SkillMutationResult.Ok("deleted"));

        public Task<SkillMutationResult> WriteFileAsync(SkillWriteFileRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(SkillMutationResult.Ok("wrote"));

        public Task<SkillMutationResult> RemoveFileAsync(SkillRemoveFileRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(SkillMutationResult.Ok("removed"));
    }
}
