using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Tools;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.AI;
using Xunit;

namespace DotCraft.Tests.Tools;

public sealed class GeneratedToolInvocationTests
{
    private static readonly Assembly FixtureAssembly = ToolGeneratorTestCompilation.Compile(Source);

    [Fact]
    public async Task Runtime_BindsTypedArgumentsAndKeepsConcurrentInvocationContextsSeparate()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var observed = new ConcurrentDictionary<string, (ToolInvocationContext Context, CancellationToken Token)>();
        var function = CreateObserve(async (context, token, summary) =>
        {
            observed[context.CallId] = (context, token);
            if (observed.Count == 2)
                entered.TrySetResult();
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            return ToolExecutionResult.Succeeded($"{context.ThreadId}:{summary}");
        });
        var schema = JsonNode.Parse(function.JsonSchema.GetRawText())!;
        Assert.Equal(["input", "args", "count", "mode"], schema["properties"]!.AsObject().Select(pair => pair.Key));
        Assert.Equal(["input"], schema["required"]!.AsArray().Select(node => node!.GetValue<string>()));
        Assert.Null(function.ReturnJsonSchema);
        using var firstCancellation = new CancellationTokenSource();
        using var secondCancellation = new CancellationTokenSource();
        var first = Context("first");
        var second = Context("second");
        var runtime = new AIFunctionToolRuntime(function);
        var results = await Task.WhenAll(
            runtime.InvokeAsync(first, Arguments(), firstCancellation.Token).AsTask(),
            runtime.InvokeAsync(second, Arguments(), secondCancellation.Token).AsTask());

        Assert.Same(first, observed[first.CallId].Context);
        Assert.Same(second, observed[second.CallId].Context);
        Assert.Equal(firstCancellation.Token, observed[first.CallId].Token);
        Assert.Equal(secondCancellation.Token, observed[second.CallId].Token);
        Assert.Equal("thread_first:sample:payload:7:Inline", results[0].Content);
        Assert.Equal("thread_second:sample:payload:7:Inline", results[1].Content);
    }

    [Fact]
    public async Task DirectFunctionInvocation_CannotSubstituteModelArgumentsForHostContext()
    {
        var function = CreateObserve((_, _, _) => throw new InvalidOperationException("Business method ran."));
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await function.InvokeAsync(new AIFunctionArguments
            {
                ["context"] = Context("spoof"),
                ["input"] = JsonSerializer.SerializeToElement(new { name = "sample" })
            }));
        Assert.Contains("requires a live ToolInvocationContext", exception.Message);
    }

    [Theory]
    [InlineData("Sync")]
    [InlineData("TaskResult")]
    [InlineData("ValueTaskResult")]
    public async Task Runtime_ForwardsExecutionResultsWithoutSerializingTheirEnvelope(string method)
    {
        var target = Activator.CreateInstance(FixtureAssembly.GetType("Fixture.Results")!)!;
        var function = ToolGeneratorTestCompilation.Function(FixtureAssembly, $"Results_{method}", target);
        Assert.Null(function.ReturnJsonSchema);
        var expected = new ToolExecutionResult(
            true, "model content", JsonSerializer.SerializeToElement(new { client = true }),
            JsonSerializer.SerializeToElement(new { host = true }),
            JsonSerializer.SerializeToElement(new { raw = true }),
            providerResult: new object(), contentItems: [new TextContent("rich")],
            directive: ToolExecutionDirective.TerminateTurn);
        var property = target.GetType().GetProperty("Result")!;
        var runtime = new AIFunctionToolRuntime(function);
        property.SetValue(target, expected);
        Assert.Same(expected, await runtime.InvokeAsync(Context(method), new JsonObject()));

        var failure = ToolExecutionResult.Failed(new ToolError("OutcomeUnknown", "Do not replay.",
            new Dictionary<string, JsonElement> { ["executionId"] = JsonSerializer.SerializeToElement("execution-1") }));
        property.SetValue(target, failure);
        Assert.Same(failure, await runtime.InvokeAsync(Context(method), new JsonObject()));

        property.SetValue(target, null);
        var invalid = await runtime.InvokeAsync(Context(method), new JsonObject());
        Assert.False(invalid.Success);
        Assert.Equal(ToolErrorCodes.ResultInvalid, invalid.Error?.Code);
    }

    [Fact]
    public async Task OrdinaryTools_NeedNoInvocationContextAndKeepDtoAndRichResults()
    {
        var plain = ToolGeneratorTestCompilation.Function(FixtureAssembly, "Ordinary_Read");
        Assert.NotNull(plain.ReturnJsonSchema);
        var result = await new AIFunctionToolRuntime(plain).InvokeAsync(Context("plain"), new JsonObject { ["text"] = "hello" });
        Assert.True(result.Success);
        Assert.Equal("hello", JsonNode.Parse(result.Content!)!["value"]!.GetValue<string>());

        var rich = ToolGeneratorTestCompilation.Function(FixtureAssembly, "Ordinary_ReadImage");
        var imageResult = await new AIFunctionToolRuntime(rich).InvokeAsync(Context("image"), new JsonObject());
        Assert.True(imageResult.Success);
        Assert.Equal("image", imageResult.Content);
        Assert.IsType<DataContent>(imageResult.ContentItems![1]);
    }

    [Fact]
    public async Task Runtime_PreservesExplicitUncertainOutcomeAndPropagatesUnhandledCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var handled = CreateObserve((_, token, _) =>
        {
            try { token.ThrowIfCancellationRequested(); }
            catch (OperationCanceledException)
            {
                return Task.FromResult(ToolExecutionResult.Failed(new ToolError("OutcomeUnknown", "Do not replay.")));
            }
            throw new InvalidOperationException("Cancellation was not passed to the tool.");
        });
        var outcome = await new AIFunctionToolRuntime(handled).InvokeAsync(Context("handled"), Arguments(), cancellation.Token);
        Assert.Equal("OutcomeUnknown", outcome.Error?.Code);

        var unhandled = CreateObserve((_, token, _) =>
        {
            token.ThrowIfCancellationRequested();
            throw new InvalidOperationException("Cancellation was not passed to the tool.");
        });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await new AIFunctionToolRuntime(unhandled).InvokeAsync(Context("unhandled"), Arguments(), cancellation.Token));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("enum")]
    public async Task Dispatcher_RejectsInvalidModelArgumentsBeforeCallingTheMethod(string invalid)
    {
        var invoked = false;
        var function = CreateObserve((_, _, _) =>
        {
            invoked = true;
            return Task.FromResult(ToolExecutionResult.Succeeded("unexpected"));
        });
        var planning = new ToolPlanningContext("thread", "planned-turn", Path.GetTempPath(),
            Path.GetTempPath(), "agent", null, [], 1);
        var snapshot = await new EffectiveToolSnapshotBuilder().BuildAsync([new SourceAdapter(function)], planning);
        var arguments = Arguments();
        if (invalid == "missing") arguments.Remove("input");
        if (invalid == "enum") arguments["mode"] = 1;
        var result = await new ToolDispatcher().DispatchAsync(snapshot, new ToolName(null, function.Name), arguments,
            new ToolInvocationRequest("thread", "live-turn", "call", ToolInvocationAudience.Model));
        Assert.False(result.Success);
        Assert.Equal(ToolErrorCodes.InputInvalid, result.Error?.Code);
        Assert.False(invoked);
    }

    [Theory]
    [InlineData("ToolInvocationContext? context")]
    [InlineData("ToolInvocationContext context = null!")]
    [InlineData("ref ToolInvocationContext context")]
    [InlineData("ToolInvocationContext first, ToolInvocationContext second")]
    [InlineData("[ToolParameter(Name = \"context\")] ToolInvocationContext context")]
    public void Generator_RejectsInvalidContextDeclarations(string parameters)
    {
        var source = $$"""
            using System;
            using System.ComponentModel;
            using System.ComponentModel.DataAnnotations;
            using DotCraft.Tools;
            public class InvalidTool
            {
                [GeneratedTool, Description("Invalid tool.")]
                public void Run({{parameters}}) { throw new NotImplementedException(); }
            }
            """;
        var generated = ToolGeneratorTestCompilation.Run(source, "InvalidContext", out _);
        Assert.Contains(generated.Diagnostics, diagnostic => diagnostic.Id == "DCGEN012"
            && diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(generated.Diagnostics, diagnostic => diagnostic.Id == "DCGEN002");
    }

    private static AIFunction CreateObserve(Func<ToolInvocationContext, CancellationToken, string, Task<ToolExecutionResult>> callback)
    {
        var target = Activator.CreateInstance(FixtureAssembly.GetType("Fixture.ObserveTools")!, callback)!;
        return ToolGeneratorTestCompilation.Function(FixtureAssembly, "ObserveTools_Observe", target);
    }

    private static JsonObject Arguments() => new()
    {
        ["input"] = new JsonObject { ["name"] = "sample" },
        ["args"] = new JsonObject { ["value"] = "payload" }
    };

    private static ToolInvocationContext Context(string suffix) => new(
        $"thread_{suffix}", $"turn_{suffix}", $"call_{suffix}", ToolInvocationAudience.Model,
        new ToolName(null, "observe"), new ToolDefinitionId(ToolSourceKind.CoreNative, "test", new SourceToolId("observe")),
        new RuntimeBindingId($"runtime_{suffix}"), 4, DateTimeOffset.UtcNow,
        WorkspacePath: Path.Combine(Path.GetTempPath(), suffix), ExecutionLocation: new("local", Path.GetTempPath()));

    private sealed class SourceAdapter(AIFunction function) : AIFunctionToolSource
    {
        public override string SourceId => "typed-fixture";
        protected override IEnumerable<AIFunction> CreateFunctions(ToolPlanningContext context) => [function];
    }

    private const string Source = """
        using System;
        using System.Collections.Generic;
        using System.ComponentModel;
        using System.ComponentModel.DataAnnotations;
        using System.Text.Json.Nodes;
        using System.Threading;
        using System.Threading.Tasks;
        using DotCraft.Tools;
        using Microsoft.Extensions.AI;
        namespace Fixture;
        public enum Mode { Inline, Background }
        public sealed class Input { public required string Name { get; init; } }
        public sealed class Output { public string Value { get; init; } = ""; }
        public sealed class ObserveTools(Func<ToolInvocationContext, CancellationToken, string, Task<ToolExecutionResult>> callback)
        {
            [GeneratedTool(Name = "observe"), Description("Observe the invocation.")]
            public async Task<ToolExecutionResult> Observe(
                ToolInvocationContext context,
                [Description("Input object.")] Input input,
                [Description("Open payload.")] JsonObject? args = null,
                [Range(0, 100), Description("Count.")] int count = 7,
                [Description("Mode.")] Mode mode = Mode.Inline,
                CancellationToken cancellationToken = default)
                => await callback(context, cancellationToken, $"{input.Name}:{args?["value"]}:{count}:{mode}");
        }
        public sealed class Results
        {
            public ToolExecutionResult Result { get; set; } = null!;
            [GeneratedTool, Description("Synchronous result.")]
            public ToolExecutionResult Sync() => Result;
            [GeneratedTool, Description("Task result.")]
            public Task<ToolExecutionResult> TaskResult() => Task.FromResult(Result);
            [Tool(CatalogVisible = false), Description("Value task result.")]
            public ValueTask<ToolExecutionResult> ValueTaskResult() => ValueTask.FromResult(Result);
        }
        public static class Ordinary
        {
            [Tool(CatalogVisible = false), Description("Read a value.")]
            public static Output Read([Description("Text.")] string text) => new() { Value = text };
            [GeneratedTool, Description("Read an image.")]
            public static IList<AIContent> ReadImage() => [new TextContent("image"), new DataContent(new byte[] { 1 }, "image/png")];
        }
        """;
}
