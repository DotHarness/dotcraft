using DotCraft.Configuration;
using DotCraft.Context;
using DotCraft.Memory;
using DotCraft.Protocol;
using DotCraft.Skills;

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
        File.WriteAllText(Path.Combine(_craftDir, "AGENTS.md"), "AGENTS instructions");
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
    public void MainPrompt_WhenSpawnAgentAvailable_IncludesLifecycleGuidance()
    {
        var prompt = CreateMainBuilder(
                toolNames: ["SpawnAgent", "SendMessage", "FollowupTask", "WaitAgent", "ListAgents", "CloseAgent"])
            .BuildSystemPrompt();

        Assert.Contains("## SubAgent Lifecycle", prompt, StringComparison.Ordinal);
        Assert.Contains("Use `SpawnAgent` for concrete sidecar work", prompt, StringComparison.Ordinal);
        Assert.Contains("Use `SendMessage` for mailbox-only coordination", prompt, StringComparison.Ordinal);
        Assert.Contains("Use `FollowupTask` to start or queue a target agent turn", prompt, StringComparison.Ordinal);
        Assert.Contains("Use `WaitAgent` only when the parent is blocked", prompt, StringComparison.Ordinal);
        Assert.Contains("Use `CloseAgent` with an `agentPath`", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("### Designing Child Tasks", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("When you are not confident you can find what you need in 1-2 tool calls", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void MainPrompt_WhenSpawnAgentUnavailable_OmitsLifecycleGuidance()
    {
        var prompt = CreateMainBuilder(
                toolNames: ["ReadFile", "GrepFiles", "FindFiles"])
            .BuildSystemPrompt();

        Assert.DoesNotContain("## SubAgent Lifecycle", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("Use `WaitAgent` only when the parent is blocked", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void MainPrompt_WhenRequestUserInputAvailable_IncludesQuestionGuidance()
    {
        var prompt = CreateMainBuilder(
                toolNames: ["ReadFile", "RequestUserInput"])
            .BuildSystemPrompt();

        Assert.Contains("## RequestUserInput", prompt, StringComparison.Ordinal);
        Assert.Contains("Use `RequestUserInput` only when it is listed", prompt, StringComparison.Ordinal);
        Assert.Contains("In Plan mode, after targeted non-mutating exploration, use `RequestUserInput`", prompt, StringComparison.Ordinal);
        Assert.Contains("cannot be answered by repo or environment exploration", prompt, StringComparison.Ordinal);
        Assert.Contains("Do not ask meaningful multiple-choice questions as plain assistant text", prompt, StringComparison.Ordinal);
        Assert.Contains("recommended option first", prompt, StringComparison.Ordinal);
        Assert.Contains("In Agent mode, prefer reasonable assumptions and execution", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("### Plan Mode Questions", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("### Agent Mode Questions", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("### Question Format", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void MainPrompt_WhenRequestUserInputUnavailable_OmitsQuestionGuidance()
    {
        var prompt = CreateMainBuilder(
                toolNames: ["ReadFile", "GrepFiles", "FindFiles"])
            .BuildSystemPrompt();

        Assert.DoesNotContain("## RequestUserInput", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("Do not ask meaningful multiple-choice questions as plain assistant text", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void SubAgentLightPrompt_KeepsEssentialContextAndRoleInstructions()
    {
        var prompt = CreateBuilder(
                toolNames: ["ReadFile", "GrepFiles", "WebSearch"],
                roleInstructions: "Role-specific guidance.")
            .BuildSystemPrompt();

        Assert.Contains("DotCraft", prompt, StringComparison.Ordinal);
        Assert.Contains(_tempDir, prompt, StringComparison.Ordinal);
        Assert.Contains("AGENTS instructions", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("USER instructions", prompt, StringComparison.Ordinal);
        Assert.Contains("## SubAgent Context", prompt, StringComparison.Ordinal);
        Assert.Contains("ReadFile", prompt, StringComparison.Ordinal);
        Assert.Contains("GrepFiles", prompt, StringComparison.Ordinal);
        Assert.Contains("WebSearch", prompt, StringComparison.Ordinal);
        Assert.Contains("Role-specific guidance.", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void SubAgentLightPrompt_DoesNotIncludeParentLifecycleGuidance()
    {
        var prompt = CreateBuilder(
                toolNames: ["SpawnAgent", "SendMessage", "FollowupTask", "WaitAgent", "CloseAgent"],
                roleInstructions: "Role-specific guidance.")
            .BuildSystemPrompt();

        Assert.Contains("## SubAgent Context", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("## SubAgent Lifecycle", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("Use `WaitAgent` only when the parent is blocked", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("## RequestUserInput", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void SubAgentLightPrompt_OmitsHeavySections()
    {
        var prompt = CreateBuilder(
                toolNames: ["SkillManage", "SkillView"],
                roleInstructions: "Role-specific guidance.")
            .BuildSystemPrompt();

        Assert.DoesNotContain("## Skill Self-Learning", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("# Memory", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("## Available Tool Sources", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void Prompt_IncludesResponseStyleGuidance()
    {
        var mainPrompt = CreateMainBuilder(
                toolNames: ["ReadFile", "GrepFiles", "FindFiles"])
            .BuildSystemPrompt();
        var subAgentPrompt = CreateBuilder(
                toolNames: ["ReadFile", "GrepFiles", "FindFiles"],
                roleInstructions: "Role-specific guidance.")
            .BuildSystemPrompt();

        Assert.Contains("## Response Style", mainPrompt, StringComparison.Ordinal);
        Assert.Contains("Be concise, direct, and useful", mainPrompt, StringComparison.Ordinal);
        Assert.Contains("## Response Style", subAgentPrompt, StringComparison.Ordinal);
        Assert.Contains("Be concise, direct, and useful", subAgentPrompt, StringComparison.Ordinal);
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
        Assert.DoesNotContain("Plan mode ACTIVE", agentPrompt, StringComparison.Ordinal);
        Assert.DoesNotContain("Your operational mode has changed", agentPrompt, StringComparison.Ordinal);
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

    private PromptBuilder CreateBuilder(IReadOnlyList<string> toolNames, string roleInstructions) =>
        new(
            new MemoryStore(_craftDir),
            new SkillsLoader(_craftDir),
            _craftDir,
            _tempDir,
            sandboxEnabled: false,
            deferredMcpServerNames: ["example"],
            toolNamesProvider: () => toolNames,
            promptProfile: SubAgentPromptProfiles.Light,
            roleInstructions: roleInstructions);
}
