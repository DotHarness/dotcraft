using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.CLI;
using DotCraft.Configuration;
using DotCraft.Processes;
using DotCraft.Tools;
using Microsoft.Extensions.AI;

namespace DotCraft.CodeMode.Tests;

public sealed class CodeModeResultDeliveryTests
{
    [Fact]
    public async Task SpilledDynamicResultRemainsACompleteJsonString()
    {
        var message = new string('x', 120_000) + "终😀";
        var value = JsonSerializer.SerializeToElement(new
        {
            page = new { nextCursor = "older", hasMore = true },
            turns = new[] { new { items = new[] { new { text = message } } } }
        });
        var (script, recorded, _) = await RunAsync(ToolExecutionResult.Succeeded(value.GetRawText()), """
            const result = await tools.Read({});
            const data = JSON.parse(result);
            text({
              type: typeof result,
              length: data.turns[0].items[0].text.length,
              tail: data.turns[0].items[0].text.slice(-3),
              cursor: data.page.nextCursor
            });
            """);

        Assert.Equal("completed", script.StructuredContent?.GetProperty("status").GetString());
        Assert.Contains("\"type\":\"string\"", script.Content);
        Assert.Contains($"\"length\":{message.Length}", script.Content);
        Assert.Contains("终😀", script.Content);
        Assert.Contains("\"cursor\":\"older\"", script.Content);
        Assert.True(recorded.Content!.Length < message.Length);
    }

    [Fact]
    public async Task SpilledRichContentKeepsOriginalTextAndImageForTheProgram()
    {
        var text = new string('x', 120_000);
        var imageBytes = new byte[256 * 1024];
        Array.Fill(imageBytes, (byte)0xfb);
        var source = ToolExecutionResult.Succeeded(text, contentItems:
        [
            new TextContent(text),
            new DataContent(imageBytes, "image/png")
        ]);
        var (script, recorded, _) = await RunAsync(source, """
            const result = await tools.Read({});
            text(result.content[0].text.length);
            image(result.content[1]);
            """, returnsContent: true, maxResultBytes: 256 * 1024);

        Assert.Equal("completed", script.StructuredContent?.GetProperty("status").GetString());
        Assert.Contains("120000", script.Content);
        Assert.Equal(imageBytes, Assert.Single(script.ContentItems!.OfType<DataContent>()).Data.ToArray());
        Assert.True(recorded.Content!.Length < text.Length);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OversizedProgramResultRejectsWithoutRepeatingTheTool(bool declaresOutput)
    {
        var value = JsonSerializer.SerializeToElement(new { text = new string('x', 2_000) });
        var source = ToolExecutionResult.Succeeded(value.GetRawText(), declaresOutput ? value : null);
        var (script, recorded, calls) = await RunAsync(source, """
            try {
              await tools.Read({});
              text('unexpected success');
            } catch (error) {
              text(error.message);
            }
            """, declaresOutput: declaresOutput, maxResultBytes: 1024);

        Assert.Equal("completed", script.StructuredContent?.GetProperty("status").GetString());
        Assert.Contains(CodeModeResults.ResultTooLargeErrorCode, script.Content);
        Assert.Equal(1, calls);
        Assert.True(recorded.Success);
    }

    [Fact]
    public async Task BusinessFailuresExposeTheSourceCodeWithoutPrivateErrorParameters()
    {
        var source = ToolExecutionResult.Failed(new ToolError(
            ToolErrorCodes.ExecutionFailed,
            "The requested item is missing.",
            new Dictionary<string, JsonElement>
            {
                ["sourceErrorCode"] = JsonSerializer.SerializeToElement("ItemNotFound"),
                ["private"] = JsonSerializer.SerializeToElement("private-value")
            }));
        var (script, _, calls) = await RunAsync(source, """
            try {
              await tools.Read({});
            } catch (error) {
              text(error.message);
            }
            """);

        Assert.Equal("completed", script.StructuredContent?.GetProperty("status").GetString());
        Assert.Contains(ToolErrorCodes.ExecutionFailed, script.Content);
        Assert.Contains("ItemNotFound", script.Content);
        Assert.DoesNotContain("private-value", script.Content);
        Assert.Equal(1, calls);
    }

    private static async Task<(ToolExecutionResult Script, ToolExecutionResult Recorded, int Calls)> RunAsync(
        ToolExecutionResult source,
        string code,
        bool declaresOutput = false,
        bool returnsContent = false,
        int maxResultBytes = 1024 * 1024)
    {
        _ = typeof(CommandLineArgs).Assembly;
        var workspace = Directory.CreateTempSubdirectory("codemode-result-").FullName;
        try
        {
            var recorder = new ResultRecorder();
            var dispatcher = new ToolDispatcher(
                recorder: recorder,
                resultNormalizer: new DefaultToolResultNormalizer(
                    maxModelContentCharacters: 1000,
                    defaultWorkspacePath: workspace,
                    dataPath: Path.Combine(workspace, ".craft")));
            var kind = declaresOutput || returnsContent ? ToolSourceKind.CoreNative : ToolSourceKind.RuntimeDynamic;
            var id = new ToolDefinitionId(kind, "test", new SourceToolId("Read"));
            var runtime = new ResultRuntime(source);
            var definition = new ToolDefinition(
                id,
                new ToolName(null, "Read"),
                "Read data.",
                JsonSerializer.SerializeToElement(new { type = "object" }),
                declaresOutput ? JsonSerializer.SerializeToElement(new { type = "object" }) : null,
                annotations: returnsContent
                    ? new Dictionary<string, JsonElement>
                    {
                        [ToolContentMetadata.ReturnsContentAnnotation] = JsonSerializer.SerializeToElement(true)
                    }
                    : null);
            var registration = new ToolRegistration(
                definition,
                new ToolRuntimeBinding(new RuntimeBindingId("test:Read"), id, runtime,
                    ToolBindingLeases.AlwaysAvailable, "test", 1),
                kind == ToolSourceKind.RuntimeDynamic ? ToolProjectionShape.DynamicLifecycle : ToolProjectionShape.StandardPair);
            var snapshot = new EffectiveToolSnapshotBuilder().Build([registration], revision: 1);
            await using var host = new CodeModeWorkerHost(new ManagedChildProcessFactory(), workspace,
                new CodeModeLimits { MaxNestedResultBytes = maxResultBytes });
            var config = new AppConfig();
            config.Tools.CodeMode.Mode = AppConfig.CodeModeSetting.Only;
            var finalizer = new CodeModeToolFinalizer(() => config, host, new CodeModeStore(), dispatcher);
            var planning = new ToolPlanningContext("thread", null, workspace, Path.Combine(workspace, ".craft"),
                "agent", null, null, 1);
            var exec = (await finalizer.FinalizeAsync(snapshot, planning)).Registrations
                .Single(static tool => tool.Definition.Name.Name == "CodeMode");
            var context = new ToolInvocationContext("thread", "turn", "exec", ToolInvocationAudience.Model,
                exec.Definition.Name, exec.Definition.Id, exec.Binding.Id, 1, DateTimeOffset.UtcNow,
                WorkspacePath: workspace);

            var result = await exec.Binding.Runtime.InvokeAsync(context, new JsonObject { ["code"] = code });

            Assert.True(result.Success, result.Content);
            return (result, Assert.IsType<ToolExecutionResult>(recorder.Result), runtime.Calls);
        }
        finally
        {
            try { Directory.Delete(workspace, recursive: true); }
            catch (IOException) { }
        }
    }

    private sealed class ResultRuntime(ToolExecutionResult result) : IToolRuntime
    {
        public int Calls { get; private set; }

        public ValueTask<ToolExecutionResult> InvokeAsync(
            ToolInvocationContext context,
            JsonObject arguments,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            return ValueTask.FromResult(result);
        }
    }

    private sealed class ResultRecorder : IToolInvocationRecorder
    {
        public ToolExecutionResult? Result { get; private set; }

        public ValueTask RecordStartedAsync(ToolInvocationContext context, ToolRegistration registration,
            JsonObject arguments, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

        public ValueTask RecordTerminalAsync(ToolInvocationContext context, ToolRegistration registration,
            ToolExecutionResult result, TimeSpan duration, CancellationToken cancellationToken = default)
        {
            Result = result;
            return ValueTask.CompletedTask;
        }
    }
}
