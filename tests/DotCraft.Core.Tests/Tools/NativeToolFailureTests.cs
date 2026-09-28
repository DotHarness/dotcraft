using System.Text.Json.Nodes;
using DotCraft.GeneratedTools.Core;
using DotCraft.Tools;
using DotCraft.Sessions;
using Microsoft.Extensions.AI;
using Xunit;

namespace DotCraft.Tests.Tools;

public sealed class NativeToolFailureTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "tool-outcomes-" + Guid.NewGuid().ToString("N"));

    public NativeToolFailureTests() => Directory.CreateDirectory(_root);
    public void Dispose() => Directory.Delete(_root, true);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CommittedWriteWithBrokenDiffRemainsSuccessful(bool edit)
    {
        var file = Path.Combine(_root, "file.txt");
        await File.WriteAllTextAsync(file, "before");
        var failure = new InvalidOperationException("diff unavailable");
        var tools = new FileTools(_root) { ChangeReporter = _ => throw failure };
        var reported = new List<Exception>();
        using var diagnostics = ToolInvocationDiagnostics.Enter((exception, _) => reported.Add(exception));
        var result = await Invoke(edit ? GeneratedToolFunctions.FileTools_EditFile(tools) : GeneratedToolFunctions.FileTools_WriteFile(tools),
            edit ? new() { ["path"] = "file.txt", ["oldText"] = "before", ["newText"] = "after" }
                 : new() { ["path"] = "file.txt", ["content"] = "after" });

        Assert.True(result.Success);
        Assert.Null(result.Error);
        Assert.Equal("after", await File.ReadAllTextAsync(file));
        var payload = result.StructuredContent!.Value;
        Assert.Equal("applied", payload.GetProperty("writeState").GetString());
        Assert.Empty(payload.GetProperty("changes").EnumerateArray());
        Assert.Equal("file_change_report_failed", payload.GetProperty("warnings")[0].GetProperty("code").GetString());
        Assert.Same(failure, Assert.Single(reported));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RejectedEditAndFailedWriteHaveExplicitFailureStates(bool edit)
    {
        var tools = new FileTools(_root);
        var result = await Invoke(edit ? GeneratedToolFunctions.FileTools_EditFile(tools) : GeneratedToolFunctions.FileTools_WriteFile(tools),
            edit ? new() { ["path"] = "missing.txt", ["oldText"] = "before", ["newText"] = "after" }
                 : new() { ["path"] = _root, ["content"] = "after" });

        Assert.False(result.Success);
        Assert.NotNull(result.Error);
        Assert.Equal(edit ? "notApplied" : "unknown", result.StructuredContent!.Value.GetProperty("writeState").GetString());
        Assert.False(File.Exists(Path.Combine(_root, "missing.txt")));
        Assert.True(Directory.Exists(_root));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OnlyUnknownWriteFailureInvalidatesThePublishedTurnDiff(bool unknown)
    {
        var tracker = new TurnDiffTracker();
        using var scope = TurnDiffTrackerScope.Set(tracker);
        var tools = new FileTools(_root);
        Assert.True((await tools.WriteFile("tracked.txt", "before")).Success);
        Assert.True(tracker.TryTakeSnapshot(out var previous));
        Assert.NotEmpty(previous);

        var failure = unknown
            ? await tools.WriteFile(".", "cannot write a directory")
            : await tools.EditFile("missing.txt", "before", "after");

        Assert.False(failure.Success);
        Assert.Equal(unknown ? "unknown" : "notApplied", failure.StructuredContent!.Value.GetProperty("writeState").GetString());
        Assert.Equal(unknown, tracker.TryTakeSnapshot(out var current));
        Assert.Equal(unknown ? "" : previous, current);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("original error")]
    public async Task NativeExceptionPreservesOriginalDiagnosticsAndNonemptyFallback(string message)
    {
        var failure = new InvalidOperationException(message);
        var reported = new List<Exception>();
        using var diagnostics = ToolInvocationDiagnostics.Enter((exception, _) => reported.Add(exception));
        var function = AIFunctionFactory.Create(new Func<string>(() => throw failure), name: "Fail");

        var result = await Invoke(function, new());

        Assert.False(result.Success);
        Assert.Equal(ToolErrorCodes.ExecutionFailed, result.Error!.Code);
        Assert.False(string.IsNullOrWhiteSpace(result.Error.Message));
        if (!string.IsNullOrWhiteSpace(message)) Assert.Equal(message, result.Error.Message);
        Assert.Same(failure, Assert.Single(reported));
        Assert.NotNull(reported[0].StackTrace);
    }

    [Fact]
    public async Task EmptyArgumentErrorKeepsInputClassification()
    {
        var function = AIFunctionFactory.Create(new Func<string>(() => throw new ArgumentException(" ")), name: "Fail");
        var result = await Invoke(function, new());
        Assert.Equal(ToolErrorCodes.InputInvalid, result.Error!.Code);
        Assert.Contains("Fail", result.Error.Message);
    }

    [Fact]
    public async Task CancellationIsNotConvertedToExecutionFailure()
    {
        using var cancellation = new CancellationTokenSource();
        var function = AIFunctionFactory.Create(new Func<string>(() =>
        {
            cancellation.Cancel();
            throw new OperationCanceledException(cancellation.Token);
        }), name: "Cancel");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Invoke(function, new(), cancellation.Token));
    }

    internal static async Task<ToolExecutionResult> Invoke(AIFunction function, JsonObject arguments,
        CancellationToken ct = default) => await new AIFunctionToolRuntime(function).InvokeAsync(
            new ToolInvocationContext("thread", "turn", "call", ToolInvocationAudience.Model,
                new ToolName(null, function.Name), new ToolDefinitionId(ToolSourceKind.CoreNative, "test", new SourceToolId(function.Name)),
                new RuntimeBindingId("native:test"), 1, DateTimeOffset.UtcNow), arguments, ct);
}
