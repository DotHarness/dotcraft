using DotCraft.Context;
using DotCraft.Memory;
using DotCraft.Skills;
using DotCraft.Sessions;
using SessionThread = DotCraft.Sessions.SessionThread;
using PlanTodo = DotCraft.Memory.PlanTodo;
using Xunit;

namespace DotCraft.Tests.Context;

public sealed class PromptBuilderSubAgentTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _craftDir;

    public PromptBuilderSubAgentTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"subagent_prompt_{Guid.NewGuid():N}");
        _craftDir = Path.Combine(_tempDir, ".craft");
        Directory.CreateDirectory(_craftDir);
        File.WriteAllText(Path.Combine(_craftDir, "USER.md"), "USER instructions");
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, recursive: true);
        }
        catch
        {
            // Best-effort cleanup.
        }
    }

    [Fact]
    public void MainPrompt_WhenSpawnAgentAvailable_IncludesLifecycleSection()
    {
        var prompt = CreateMainBuilder(
                toolNames: ["SpawnAgent", "SendMessage", "FollowupTask", "WaitAgent", "ListAgents", "CloseAgent"])
            .BuildSystemPrompt();

        Assert.Contains("## SubAgent Lifecycle", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void MainPrompt_WhenCloseAgentAvailable_TiesCloseGuidanceToConcurrencyLimit()
    {
        var prompt = CreateMainBuilder(
                toolNames: ["SpawnAgent", "WaitAgent", "CloseAgent"])
            .BuildSystemPrompt();

        Assert.Contains("`CloseAgent`", prompt, StringComparison.Ordinal);
        Assert.Contains("count toward the concurrency limit until closed", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void MainPrompt_WhenSpawnAgentUnavailable_OmitsLifecycleGuidance()
    {
        var prompt = CreateMainBuilder(
                toolNames: ["ReadFile", "GrepFiles", "FindFiles"])
            .BuildSystemPrompt();

        Assert.DoesNotContain("## SubAgent Lifecycle", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void MainPrompt_WhenRequestUserInputAvailable_IncludesQuestionSection()
    {
        var prompt = CreateMainBuilder(
                toolNames: ["ReadFile", "RequestUserInput"])
            .BuildSystemPrompt();

        Assert.Contains("## RequestUserInput", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void MainPrompt_WhenRequestUserInputUnavailable_OmitsQuestionGuidance()
    {
        var prompt = CreateMainBuilder(
                toolNames: ["ReadFile", "GrepFiles", "FindFiles"])
            .BuildSystemPrompt();

        Assert.DoesNotContain("## RequestUserInput", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void MainPrompt_WithAllCoordinationTools_ExplainsBlockingAsyncAndSleepBoundaries()
    {
        var prompt = CreateMainBuilder(
                ["RequestUserInput", "SendUserMessageAsync", "clock__Sleep", "clock__CurrentTime"])
            .BuildSystemPrompt();

        Assert.Contains("## User Coordination", prompt, StringComparison.Ordinal);
        Assert.Contains("prerequisite for further work", prompt, StringComparison.Ordinal);
        Assert.Contains("continue every authorized task that does not depend on the answer", prompt, StringComparison.Ordinal);
        Assert.Contains("no independent work remains", prompt, StringComparison.Ordinal);
        Assert.Contains("Do not create a Goal implicitly", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void MainPrompt_WithoutCoordinationTools_OmitsCoordinationSection()
    {
        var prompt = CreateMainBuilder(["ReadFile"]).BuildSystemPrompt();

        Assert.DoesNotContain("## User Coordination", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void Prompt_WithoutRoleInstructions_MatchesBaselineByteForByte()
    {
        var toolNames = new[] { "ReadFile", "GrepFiles", "SpawnAgent", "SkillManage", "RequestUserInput" };

        var baseline = CreateMainBuilder(toolNames).BuildSystemPrompt();
        var subAgentSurface = CreateBuilder(toolNames, roleInstructions: null).BuildSystemPrompt();

        // A native SubAgent shares its parent's cache identity, so it must reuse the parent's
        // generated instructions verbatim. Role text reaches it as a thread context item instead.
        Assert.Equal(baseline, subAgentSurface);
        Assert.Contains("USER instructions", baseline, StringComparison.Ordinal);
        Assert.Contains("## SubAgent Lifecycle", baseline, StringComparison.Ordinal);
        Assert.Contains("## RequestUserInput", baseline, StringComparison.Ordinal);
        Assert.Contains("## Skill Self-Learning", baseline, StringComparison.Ordinal);
    }

    [Fact]
    public void Prompt_WithRoleInstructions_AppendsRoleSectionForOrdinaryThreads()
    {
        var toolNames = new[] { "ReadFile", "GrepFiles" };

        var prompt = CreateBuilder(toolNames, roleInstructions: "Role-specific guidance.").BuildSystemPrompt();

        Assert.Contains("## Role Instructions", prompt, StringComparison.Ordinal);
        Assert.Contains("Role-specific guidance.", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AgentPrompt_WithExistingTodoList_DoesNotInjectTodoState()
    {
        var planStore = new PlanStore(_craftDir);
        await new ThreadStore(_craftDir).SaveThreadAsync(new SessionThread
        {
            Id = "thread-1",
            WorkspacePath = _tempDir,
            UserId = "user",
            OriginChannel = "test",
            Status = ThreadStatus.Active,
            HistoryMode = HistoryMode.Server,
            CreatedAt = DateTimeOffset.UtcNow,
            LastActiveAt = DateTimeOffset.UtcNow
        });
        await planStore.SaveStructuredPlanAsync("thread-1", new StructuredPlan
        {
            Title = "Cache Recovery",
            Overview = "",
            Content = "Do not inject this plan body.",
            Todos =
            [
                new PlanTodo
                {
                    Id = "stabilize-prefix",
                    Content = "This todo must stay out of the system prompt",
                    Status = PlanTodoStatus.InProgress
                }
            ],
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });

        var prompt = new PromptBuilder(
                new MemoryStore(_craftDir),
                new SkillsLoader(_craftDir),
                _craftDir,
                _tempDir,
                toolNamesProvider: () => ["TodoWrite"])
            .BuildSystemPrompt();

        Assert.Contains("## Mode Protocol", prompt, StringComparison.Ordinal);
        Assert.Contains("### Task State", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("<system-reminder>", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("This todo must stay out of the system prompt", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("## Current Plan", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void MainPrompt_IsStableAcrossPlanAndAgentModes()
    {
        var agentPrompt = new PromptBuilder(
                new MemoryStore(_craftDir),
                new SkillsLoader(_craftDir),
                _craftDir,
                _tempDir,
                toolNamesProvider: () => ["ReadFile", "CreatePlan", "UpdateTodos", "TodoWrite"])
            .BuildSystemPrompt();

        var planPrompt = new PromptBuilder(
                new MemoryStore(_craftDir),
                new SkillsLoader(_craftDir),
                _craftDir,
                _tempDir,
                toolNamesProvider: () => ["ReadFile", "CreatePlan", "UpdateTodos", "TodoWrite"])
            .BuildSystemPrompt();

        Assert.Equal(agentPrompt, planPrompt);
        Assert.Contains("## Mode Protocol", agentPrompt, StringComparison.Ordinal);
    }

    private PromptBuilder CreateMainBuilder(IReadOnlyList<string> toolNames) =>
        new(
            new MemoryStore(_craftDir),
            new SkillsLoader(_craftDir),
            _craftDir,
            _tempDir,
            sandboxEnabled: false,
            deferredMcpServerNames: ["example"],
            toolNamesProvider: () => toolNames);

    private PromptBuilder CreateBuilder(IReadOnlyList<string> toolNames, string? roleInstructions) =>
        new(
            new MemoryStore(_craftDir),
            new SkillsLoader(_craftDir),
            _craftDir,
            _tempDir,
            sandboxEnabled: false,
            deferredMcpServerNames: ["example"],
            toolNamesProvider: () => toolNames,
            roleInstructions: roleInstructions);
}
