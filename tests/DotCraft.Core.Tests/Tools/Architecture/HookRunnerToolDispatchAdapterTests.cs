using System.Text.Json.Nodes;
using DotCraft.Hooks;
using DotCraft.Tools;

namespace DotCraft.Core.Tests.Tools.Architecture;

public sealed class HookRunnerToolDispatchAdapterTests : IDisposable
{
    private readonly string _workspace = Path.Combine(
        Path.GetTempPath(),
        "HookDispatch_" + Guid.NewGuid().ToString("N")[..8]);

    public HookRunnerToolDispatchAdapterTests() => Directory.CreateDirectory(_workspace);

    [Fact]
    public async Task SameCallIdAcrossTurnsKeepsEachInvocationArguments()
    {
        var runner = new HookRunner(new HooksFileConfig
        {
            Hooks =
            {
                [nameof(HookEvent.PostToolUse)] =
                [
                    new HookMatcherGroup
                    {
                        Hooks =
                        [
                            Hook("Write(*first.txt*)", "FIRST"),
                            Hook("Write(*second.txt*)", "SECOND")
                        ]
                    }
                ]
            }
        }, _workspace);
        var adapter = new HookRunnerToolDispatchAdapter(runner);
        var registration = Registration();
        var first = Context("thread_first", "turn_first");
        var second = Context("thread_second", "turn_second");

        await adapter.RunPreToolUseAsync(first, registration, new JsonObject { ["path"] = "first.txt" });
        await adapter.RunPreToolUseAsync(second, registration, new JsonObject { ["path"] = "second.txt" });

        var firstFeedback = new ToolHookFeedbackCollector();
        using (ToolHookFeedbackScope.Set(firstFeedback))
            await adapter.RunTerminalAsync(first, registration, ToolExecutionResult.Succeeded("ok"));
        var secondFeedback = new ToolHookFeedbackCollector();
        using (ToolHookFeedbackScope.Set(secondFeedback))
            await adapter.RunTerminalAsync(second, registration, ToolExecutionResult.Succeeded("ok"));

        Assert.Equal("FIRST", Assert.Single(firstFeedback.Snapshot()).Text);
        Assert.Equal("SECOND", Assert.Single(secondFeedback.Snapshot()).Text);
    }

    public void Dispose()
    {
        try { Directory.Delete(_workspace, recursive: true); }
        catch { }
    }

    private static HookEntry Hook(string condition, string feedback)
    {
        var json = "{\"hookSpecificOutput\":{\"hookEventName\":\"PostToolUse\",\"additionalContext\":\""
                   + feedback + "\"}}";
        return new HookEntry
        {
            Type = "command",
            If = condition,
            Command = OperatingSystem.IsWindows()
                ? $"Write-Output '{json}'"
                : $"printf '%s\\n' '{json}'"
        };
    }

    private static ToolInvocationContext Context(string threadId, string turnId) =>
        new(
            threadId,
            turnId,
            "shared-call-id",
            ToolInvocationAudience.Model,
            new ToolName(null, "WriteFile"),
            new ToolDefinitionId(ToolSourceKind.CoreNative, "test", new SourceToolId("WriteFile")),
            new RuntimeBindingId("native:test:WriteFile:1"),
            1,
            DateTimeOffset.UtcNow);

    private static ToolRegistration Registration()
    {
        var definitionId = new ToolDefinitionId(
            ToolSourceKind.CoreNative,
            "test",
            new SourceToolId("WriteFile"));
        var definition = new ToolDefinition(
            definitionId,
            new ToolName(null, "WriteFile"),
            "Writes a file.",
            System.Text.Json.JsonSerializer.SerializeToElement(new { type = "object" }));
        return new ToolRegistration(
            definition,
            new ToolRuntimeBinding(
                new RuntimeBindingId("native:test:WriteFile:1"),
                definitionId,
                new NoOpRuntime(),
                ToolBindingLeases.AlwaysAvailable,
                "native:test",
                1),
            ToolProjectionShape.StandardPair);
    }

    private sealed class NoOpRuntime : IToolRuntime
    {
        public ValueTask<ToolExecutionResult> InvokeAsync(
            ToolInvocationContext context,
            JsonObject arguments,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(ToolExecutionResult.Succeeded("ok"));
    }
}
