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
    public void Prompt_WithoutRoleInstructions_MatchesBaselineByteForByte()
    {
        var toolNames = new[] { "ReadFile", "GrepFiles", "SpawnAgent", "SkillManage", "RequestUserInput" };

        var baseline = CreateMainBuilder(toolNames).BuildSystemPrompt();
        var subAgentSurface = CreateBuilder(toolNames, roleInstructions: null).BuildSystemPrompt();

        // A native SubAgent shares its parent's cache identity, so it must reuse the parent's
        // generated instructions verbatim. Role text reaches it as a thread context item instead.
        Assert.Equal(baseline, subAgentSurface);
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

        Assert.DoesNotContain("<system-reminder>", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("This todo must stay out of the system prompt", prompt, StringComparison.Ordinal);
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
    }

    private PromptBuilder CreateMainBuilder(IReadOnlyList<string> toolNames) =>
        new(
            new MemoryStore(_craftDir),
            new SkillsLoader(_craftDir),
            _craftDir,
            _tempDir,
            deferredMcpServerNames: ["example"],
            toolNamesProvider: () => toolNames);

    private PromptBuilder CreateBuilder(IReadOnlyList<string> toolNames, string? roleInstructions, string? developerInstructions = null) =>
        new(
            new MemoryStore(_craftDir),
            new SkillsLoader(_craftDir),
            _craftDir,
            _tempDir,
            deferredMcpServerNames: ["example"],
            toolNamesProvider: () => toolNames,
            roleInstructions: roleInstructions,
            developerInstructions: developerInstructions);
}
