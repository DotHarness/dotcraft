using System.Text.Json;
using DotCraft.Memory;
using DotCraft.Protocol;
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
                        id = "align-content",
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
            Assert.Equal("align-content", todo.Id);
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
                // Best-effort cleanup; Windows may briefly retain the SQLite file handle.
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
    public void PlanMarkdownParser_FallsBackWithoutH1OrSectionOverview()
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
    }

    [Fact]
    public void PlanMarkdownParser_UsesPlanDefaultsForEmptyMarkdown()
    {
        var parsed = PlanMarkdownParser.Parse("   ");

        Assert.Equal("Plan", parsed.Title);
        Assert.Equal("", parsed.Overview);
        Assert.Equal("", parsed.Content);
    }

    [Fact]
    public async Task TodoWrite_SavesTodoState()
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

            _ = await tools.TodoWrite([
                new TodoWriteInput
                {
                    Id = "cache-metrics",
                    Content = "Expose cache hit rate",
                    Status = PlanTodoStatus.InProgress
                }
            ]);

            var plan = await store.LoadStructuredPlanAsync(sessionId);
            Assert.NotNull(plan);
            var todo = Assert.Single(plan.Todos);
            Assert.Equal("cache-metrics", todo.Id);
            Assert.Equal("Expose cache hit rate", todo.Content);
            Assert.Equal(PlanTodoStatus.InProgress, todo.Status);
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
                // Best-effort cleanup; Windows may briefly retain the SQLite file handle.
            }
        }
    }

    private static JsonElement GetPropertySchema(JsonElement schema, string propertyName) =>
        schema.GetProperty("properties").GetProperty(propertyName);
}
