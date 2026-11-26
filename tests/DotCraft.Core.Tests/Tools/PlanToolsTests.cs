using System.Text.Json;
using DotCraft.Memory;
using DotCraft.Tools;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.AI;
using DotCraft.Sessions;
using SessionThread = DotCraft.Sessions.SessionThread;
using Xunit;

namespace DotCraft.Tests.Tools;

public sealed class PlanToolsTests
{
    [Fact]
    public async Task CreatePlanFunction_BindsPlanArgumentAndSavesStructuredPlan()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"plan_tools_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var sessionId = "thread_001";
            await new ThreadStore(tempDir).SaveThreadAsync(new SessionThread
            {
                Id = sessionId,
                WorkspacePath = tempDir,
                UserId = "user",
                OriginChannel = "test",
                Status = ThreadStatus.Active,
                HistoryMode = HistoryMode.Server,
                CreatedAt = DateTimeOffset.UtcNow,
                LastActiveAt = DateTimeOffset.UtcNow
            });
            var store = new PlanStore(tempDir);
            string? callbackThreadId = null;
            StructuredPlan? callbackPlan = null;
            var tools = new PlanTools(store, () => sessionId, (threadId, updatedPlan) =>
            {
                callbackThreadId = threadId;
                callbackPlan = updatedPlan;
            });
            var function = AIFunctionFactory.Create(tools.CreatePlan);
            var planSchema = GetPropertySchema(function.JsonSchema, "plan");

            Assert.Equal("string", planSchema.GetProperty("type").GetString());
            Assert.False(function.JsonSchema.GetProperty("properties").TryGetProperty("title", out _));
            Assert.False(function.JsonSchema.GetProperty("properties").TryGetProperty("overview", out _));
            Assert.False(function.JsonSchema.GetProperty("properties").TryGetProperty("content", out _));

            _ = await function.InvokeAsync(new AIFunctionArguments
            {
                ["plan"] = "# Fix plan\n\n## 概览\n\nUse a single markdown argument for the plan body.\n\n## Implementation Changes\n\n- Parse the title and overview from markdown.",
                ["todos"] = JsonSerializer.SerializeToElement(new[]
                {
                    new
                    {
                        content = "Update CreatePlan argument name"
                    }
                })
            });

            var plan = await store.LoadStructuredPlanAsync(sessionId);

            Assert.NotNull(plan);
            Assert.Equal("Fix plan", plan.Title);
            Assert.Equal("Use a single markdown argument for the plan body.", plan.Overview);
            Assert.Equal("## 概览\n\nUse a single markdown argument for the plan body.\n\n## Implementation Changes\n\n- Parse the title and overview from markdown.", plan.Content);
            Assert.Equal(sessionId, callbackThreadId);
            Assert.NotNull(callbackPlan);
            Assert.Equal("Fix plan", callbackPlan.Title);
            var todo = Assert.Single(plan.Todos);
            Assert.Equal("todo-1", todo.Id);
            Assert.Equal("Update CreatePlan argument name", todo.Content);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try
            {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, recursive: true);
            }
            catch
            {
            }
        }
    }

    [Fact]
    public void PlanMarkdownParser_ExtractsTitleOverviewAndContentWithoutLanguageSpecificHeadings()
    {
        var parsed = PlanMarkdownParser.Parse("""
            # 中文计划

            ## 概览

            第一段概览。
            第二行继续。

            第二段不应进入概要。

            ## 验证方案

            - 运行测试。
            """);

        Assert.Equal("中文计划", parsed.Title);
        Assert.Equal("第一段概览。 第二行继续。", parsed.Overview);
        Assert.False(parsed.Content.StartsWith("# 中文计划", StringComparison.Ordinal));
        Assert.Contains("## 概览", parsed.Content);
        Assert.Contains("## 验证方案", parsed.Content);
    }

    [Fact]
    public void PlanMarkdownParser_SkipsEmptySectionsWhenExtractingOverview()
    {
        var parsed = PlanMarkdownParser.Parse("""
            # 计划

            ## 空章节

            ## 背景

            使用结构而不是英文标题识别概要。
            """);

        Assert.Equal("使用结构而不是英文标题识别概要。", parsed.Overview);
    }

    [Fact]
    public void PlanMarkdownParser_FallsBackWithoutH1OrSectionOverviewAndDefaultsWhenEmpty()
    {
        var parsed = PlanMarkdownParser.Parse("""
            Implement cache invalidation

            Keep the existing storage format.

            ## Changes

            - Add targeted invalidation.
            """);

        Assert.Equal("Implement cache invalidation", parsed.Title);
        Assert.Equal("Keep the existing storage format.", parsed.Overview);
        Assert.StartsWith("Implement cache invalidation", parsed.Content);

        var empty = PlanMarkdownParser.Parse("   ");
        Assert.Equal(("Plan", "", ""), (empty.Title, empty.Overview, empty.Content));
    }

    [Fact]
    public async Task TodoWrite_ReplacesTheListAndKeepsThePlan()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"plan_tools_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var sessionId = "thread_001";
            await new ThreadStore(tempDir).SaveThreadAsync(new SessionThread
            {
                Id = sessionId,
                WorkspacePath = tempDir,
                UserId = "user",
                OriginChannel = "test",
                Status = ThreadStatus.Active,
                HistoryMode = HistoryMode.Server,
                CreatedAt = DateTimeOffset.UtcNow,
                LastActiveAt = DateTimeOffset.UtcNow
            });
            var store = new PlanStore(tempDir);
            var tools = new PlanTools(store, () => sessionId);
            await tools.CreatePlan("# Cache plan", [new PlanTodoInput { Content = "Add metrics" }, new PlanTodoInput { Content = "Document it" }]);

            _ = await tools.TodoWrite([
                new TodoWriteInput { Content = "Add metrics", Status = PlanTodoInputStatus.Completed },
                new TodoWriteInput { Content = "Expose cache hit rate", Status = PlanTodoInputStatus.InProgress, ActiveForm = "Exposing cache hit rate" }
            ], TodoWriteScope.Continuation);

            var plan = await store.LoadStructuredPlanAsync(sessionId);
            Assert.NotNull(plan);
            Assert.Equal("Cache plan", plan.Title);
            Assert.True(store.HasActivePlan(sessionId));
            Assert.Equal(
                [("todo-1", "Add metrics", PlanTodoStatus.Completed, (string?)null),
                    ("todo-2", "Expose cache hit rate", PlanTodoStatus.InProgress, "Exposing cache hit rate")],
                plan.Todos.Select(static todo => (todo.Id, todo.Content, todo.Status, todo.ActiveForm)));

            var rejected = await tools.TodoWrite([new TodoWriteInput { Content = " ", Status = PlanTodoInputStatus.Pending }], TodoWriteScope.Continuation);
            Assert.False(rejected.Success);
            Assert.Equal(ToolErrorCodes.InputInvalid, rejected.Error?.Code);
            var unscoped = await tools.TodoWrite([new TodoWriteInput { Content = "Next", Status = PlanTodoInputStatus.Pending }]);
            Assert.False(unscoped.Success);
            Assert.Equal(2, (await store.LoadStructuredPlanAsync(sessionId))!.Todos.Count);
        }
        finally
        {
            DeleteTempDir(tempDir);
        }
    }

    [Fact]
    public async Task TodoWrite_ForNewWorkDetachesTheListFromThePlan()
    {
        var tempDir = await CreateThreadDirAsync();
        try
        {
            var store = new PlanStore(tempDir);
            var tools = new PlanTools(store, () => "thread_001");
            await tools.CreatePlan("# Cache plan", [new PlanTodoInput { Content = "Add metrics" }]);

            _ = await tools.TodoWrite([new TodoWriteInput { Content = "Fix the menu", Status = PlanTodoInputStatus.InProgress }], TodoWriteScope.NewTask);

            var plan = await store.LoadStructuredPlanAsync("thread_001");
            Assert.Equal(("", "Fix the menu"), (plan!.Title, Assert.Single(plan.Todos).Content));
            Assert.False(store.HasActivePlan("thread_001"));
        }
        finally
        {
            DeleteTempDir(tempDir);
        }
    }

    [Fact]
    public async Task TodoWrite_AdvancesTheStoredListByPosition()
    {
        var tempDir = await CreateThreadDirAsync();
        try
        {
            var store = new PlanStore(tempDir);
            var tools = new PlanTools(store, () => "thread_001");
            Assert.False((await tools.TodoWrite(advance: true)).Success);
            _ = await tools.TodoWrite([
                new TodoWriteInput { Content = "Read", Status = PlanTodoInputStatus.InProgress },
                new TodoWriteInput { Content = "Change", Status = PlanTodoInputStatus.Pending },
                new TodoWriteInput { Content = "Test", Status = PlanTodoInputStatus.Pending },
                new TodoWriteInput { Content = "Ship", Status = PlanTodoInputStatus.Pending }
            ], TodoWriteScope.FirstTask);

            var advanced = await tools.TodoWrite(advance: true);
            Assert.True(advanced.Success);
            Assert.Equal([PlanTodoStatus.Completed, PlanTodoStatus.InProgress, PlanTodoStatus.Pending, PlanTodoStatus.Pending], await StatusesAsync());

            Assert.True((await tools.TodoWrite(done: [2, 3], start: 4)).Success);
            Assert.Equal([PlanTodoStatus.Completed, PlanTodoStatus.Completed, PlanTodoStatus.Completed, PlanTodoStatus.InProgress], await StatusesAsync());

            var outOfRange = await tools.TodoWrite(done: [5]);
            Assert.False(outOfRange.Success);
            Assert.Equal(ToolErrorCodes.InputInvalid, outOfRange.Error?.Code);
            Assert.Equal(PlanTodoStatus.InProgress, (await StatusesAsync())[3]);
        }
        finally
        {
            DeleteTempDir(tempDir);
        }

        async Task<string[]> StatusesAsync() =>
            [.. (await new PlanStore(tempDir).LoadStructuredPlanAsync("thread_001"))!.Todos.Select(static todo => todo.Status)];
    }

    private static async Task<string> CreateThreadDirAsync()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"plan_tools_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        await new ThreadStore(tempDir).SaveThreadAsync(new SessionThread
        {
            Id = "thread_001",
            WorkspacePath = tempDir,
            UserId = "user",
            OriginChannel = "test",
            Status = ThreadStatus.Active,
            HistoryMode = HistoryMode.Server,
            CreatedAt = DateTimeOffset.UtcNow,
            LastActiveAt = DateTimeOffset.UtcNow
        });
        return tempDir;
    }

    private static void DeleteTempDir(string tempDir)
    {
        SqliteConnection.ClearAllPools();
        try
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
        catch
        {
        }
    }

    private static JsonElement GetPropertySchema(JsonElement schema, string propertyName) =>
        schema.GetProperty("properties").GetProperty(propertyName);
}
