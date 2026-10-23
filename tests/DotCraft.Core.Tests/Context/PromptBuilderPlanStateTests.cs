using DotCraft.Context;
using DotCraft.Memory;
using DotCraft.Skills;
using DotCraft.Sessions;
using SessionThread = DotCraft.Sessions.SessionThread;
using PlanTodo = DotCraft.Memory.PlanTodo;
using Xunit;

namespace DotCraft.Tests.Context;

public sealed class PromptBuilderPlanStateTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _craftDir;

    public PromptBuilderPlanStateTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"plan_prompt_{Guid.NewGuid():N}");
        _craftDir = Path.Combine(_tempDir, ".craft");
        Directory.CreateDirectory(_craftDir);
        File.WriteAllText(Path.Combine(_craftDir, "USER.md"), "USER instructions");
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); }
        catch { }
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
}
