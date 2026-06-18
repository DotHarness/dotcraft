using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.AppBinding;
using DotCraft.Plugins;
using DotCraft.Protocol.AppServer;

namespace DotCraft.Tests.AppBinding;

public sealed class ManagedDynamicToolRegistryTests
{
    [Fact]
    public void ToolSpecs_AreGeneratedFromAttributesInStableOrder()
    {
        var registry = new ManagedDynamicToolRegistry<SampleDynamicTools>("sample");

        Assert.Equal(["Count", "Echo", "AsyncEcho"], registry.ToolSpecs.Select(spec => spec.Name));
        Assert.All(registry.ToolSpecs, spec =>
        {
            Assert.Equal("sample", spec.Namespace);
            Assert.NotNull(spec.InputSchema);
            Assert.True(
                PluginFunctionSchemaValidator.TryValidateSchema(spec.InputSchema!, out var message),
                message);
        });

        var echo = registry.ToolSpecs.Single(spec => spec.Name == "Echo");
        Assert.Equal("Echo a required text value.", echo.Description);
        Assert.False(echo.DeferLoading.GetValueOrDefault());
        Assert.Equal(["text"], Required(echo));
        Assert.Equal("string", Property(echo, "text")["type"]!.GetValue<string>());
        Assert.Equal("boolean", Property(echo, "flag")["type"]!.GetValue<string>());
        Assert.Equal("array", Property(echo, "tags")["type"]!.GetValue<string>());
        Assert.Equal("string", ((JsonObject)Property(echo, "tags")["items"]!)["type"]!.GetValue<string>());
        Assert.Equal("object", Property(echo, "metadata")["type"]!.GetValue<string>());

        var count = registry.ToolSpecs.Single(spec => spec.Name == "Count");
        Assert.True(count.DeferLoading.GetValueOrDefault());
        Assert.Equal(["amount"], Required(count));
        Assert.Equal("integer", Property(count, "amount")["type"]!.GetValue<string>());
    }

    [Fact]
    public async Task InvokeAsync_BindsTypedArgumentsAndClonesJsonObjects()
    {
        var target = new SampleDynamicTools();
        var registry = new ManagedDynamicToolRegistry<SampleDynamicTools>("sample");
        var metadata = new JsonObject { ["source"] = "test" };
        var result = await registry.InvokeAsync(
            target,
            Context("Echo"),
            new JsonObject
            {
                ["text"] = " hello ",
                ["flag"] = "true",
                ["tags"] = "alpha",
                ["metadata"] = metadata
            },
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("hello", result.StructuredResult!["text"]!.GetValue<string>());
        Assert.True(result.StructuredResult!["flag"]!.GetValue<bool>());
        Assert.Equal(1, result.StructuredResult!["tagCount"]!.GetValue<int>());
        Assert.Equal("Echo", result.StructuredResult!["tool"]!.GetValue<string>());
        Assert.False(metadata.ContainsKey("mutated"));
    }

    [Fact]
    public async Task InvokeAsync_SupportsTaskAndValueTaskResults()
    {
        var target = new SampleDynamicTools();
        var registry = new ManagedDynamicToolRegistry<SampleDynamicTools>("sample");

        var valueTaskResult = await registry.InvokeAsync(
            target,
            Context("Count"),
            new JsonObject { ["amount"] = 3 },
            CancellationToken.None);
        var taskResult = await registry.InvokeAsync(
            target,
            Context("AsyncEcho"),
            new JsonObject { ["text"] = "done" },
            CancellationToken.None);

        Assert.Equal(3, valueTaskResult.StructuredResult!["amount"]!.GetValue<int>());
        Assert.Equal("done", taskResult.StructuredResult!["text"]!.GetValue<string>());
    }

    [Fact]
    public async Task InvokeAsync_UsesFreshInvocationValuesWithCachedMetadata()
    {
        var target = new ContextDynamicTools();
        var registry = new ManagedDynamicToolRegistry<ContextDynamicTools>();
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        var first = await registry.InvokeAsync(
            target,
            Context("Context"),
            new JsonObject(),
            cancelled.Token);
        var second = await registry.InvokeAsync(
            target,
            Context("Context"),
            new JsonObject { ["text"] = " second " },
            CancellationToken.None);

        Assert.True(first.StructuredResult!["cancelled"]!.GetValue<bool>());
        Assert.Equal("default", first.StructuredResult!["text"]!.GetValue<string>());
        Assert.False(second.StructuredResult!["cancelled"]!.GetValue<bool>());
        Assert.Equal("second", second.StructuredResult!["text"]!.GetValue<string>());
    }

    [Fact]
    public async Task InvokeAsync_RejectsMissingRequiredAndInvalidTypes()
    {
        var target = new SampleDynamicTools();
        var registry = new ManagedDynamicToolRegistry<SampleDynamicTools>("sample");

        var missing = await Assert.ThrowsAsync<AppServerException>(async () =>
            await registry.InvokeAsync(target, Context("Echo"), new JsonObject(), CancellationToken.None));
        Assert.Equal(AppServerErrors.InvalidParamsCode, missing.Code);
        Assert.Contains("'text' is required", ErrorDetail(missing), StringComparison.Ordinal);

        var badType = await Assert.ThrowsAsync<AppServerException>(async () =>
            await registry.InvokeAsync(
                target,
                Context("Echo"),
                new JsonObject { ["text"] = "ok", ["tags"] = new JsonArray(1) },
                CancellationToken.None));
        Assert.Equal(AppServerErrors.InvalidParamsCode, badType.Code);
        Assert.Contains("'tags' must be an array of strings", ErrorDetail(badType), StringComparison.Ordinal);
    }

    [Fact]
    public void UnsupportedParameterTypes_FailFast()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => new ManagedDynamicToolRegistry<UnsupportedDynamicTools>());

        Assert.Contains("unsupported type", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GeneratedArgumentBinder_MatchesFallbackSupportedTypesAndClonesJsonObjects()
    {
        var arguments = JsonNode.Parse("""
            {
              "text": " hello ",
              "flag": "true",
              "shortValue": 1,
              "intValue": 2,
              "longValue": 3,
              "floatValue": 1.5,
              "doubleValue": 2.5,
              "decimalValue": 3.5,
              "metadata": { "source": "test" },
              "tags": ["alpha", "alpha", " beta ", ""]
            }
            """)!.AsObject();
        var metadata = (JsonObject)arguments["metadata"]!;

        Assert.Equal("hello", GeneratedDynamicToolArgumentBinder.BindRequiredString(arguments, "text"));
        Assert.True(GeneratedDynamicToolArgumentBinder.BindRequiredBool(arguments, "flag"));
        Assert.Equal((short)1, GeneratedDynamicToolArgumentBinder.BindRequiredShort(arguments, "shortValue"));
        Assert.Equal(2, GeneratedDynamicToolArgumentBinder.BindRequiredInt(arguments, "intValue"));
        Assert.Equal(3L, GeneratedDynamicToolArgumentBinder.BindRequiredLong(arguments, "longValue"));
        Assert.Equal(1.5F, GeneratedDynamicToolArgumentBinder.BindRequiredFloat(arguments, "floatValue"));
        Assert.Equal(2.5D, GeneratedDynamicToolArgumentBinder.BindRequiredDouble(arguments, "doubleValue"));
        Assert.Equal(3.5M, GeneratedDynamicToolArgumentBinder.BindRequiredDecimal(arguments, "decimalValue"));
        Assert.Equal(["alpha", "beta"], GeneratedDynamicToolArgumentBinder.BindRequiredStringList(arguments, "tags"));
        Assert.Equal(["alpha", "beta"], GeneratedDynamicToolArgumentBinder.BindRequiredStringArray(arguments, "tags"));

        var clone = GeneratedDynamicToolArgumentBinder.BindRequiredJsonObject(arguments, "metadata");
        clone["mutated"] = true;
        Assert.False(metadata.ContainsKey("mutated"));

        Assert.Equal(42, GeneratedDynamicToolArgumentBinder.BindOptionalInt(new JsonObject(), "missing", 42));
        Assert.Null(GeneratedDynamicToolArgumentBinder.BindOptionalString(new JsonObject { ["empty"] = "   " }, "empty", "fallback"));
    }

    private static ManagedAppBindingToolCallContext Context(string toolName) =>
        new(
            WorkspaceCraftPath: "craft",
            WorkspacePath: "workspace",
            BindingId: "binding",
            ThreadId: "thread",
            TurnId: "turn",
            CallId: "call",
            AppId: "app",
            GrantId: "grant",
            ToolName: toolName);

    private static JsonObject Property(DynamicToolSpec spec, string name) =>
        (JsonObject)((JsonObject)spec.InputSchema!["properties"]!)[name]!;

    private static IReadOnlyList<string> Required(DynamicToolSpec spec) =>
        spec.InputSchema!["required"] is JsonArray required
            ? required.Select(item => item!.GetValue<string>()).ToList()
            : [];

    private static string ErrorDetail(AppServerException ex) =>
        JsonSerializer.SerializeToNode(ex.ErrorData)!["detail"]!.GetValue<string>();

    private sealed class SampleDynamicTools
    {
        [DynamicTool("Echo", Order = 20)]
        [Description("Echo a required text value.")]
        private DynamicToolCallResult Echo(
            ManagedAppBindingToolCallContext context,
            [Description("Required text.")] string text,
            [Description("Optional flag.")] bool? flag = null,
            [Description("Related tags.")] List<string>? tags = null,
            [Description("Metadata object.")] JsonObject? metadata = null)
        {
            metadata!["mutated"] = true;
            return Result(new JsonObject
            {
                ["tool"] = context.ToolName,
                ["text"] = text,
                ["flag"] = flag,
                ["tagCount"] = tags?.Count ?? 0
            });
        }

        [DynamicTool("Count", Order = 10, DeferLoading = true)]
        [Description("Return a count.")]
        private ValueTask<DynamicToolCallResult> Count([Description("Amount to return.")] int amount) =>
            ValueTask.FromResult(Result(new JsonObject { ["amount"] = amount }));

        [DynamicTool("AsyncEcho", Order = 30)]
        [Description("Return text asynchronously.")]
        private Task<DynamicToolCallResult> AsyncEcho([Description("Text to return.")] string text) =>
            Task.FromResult(Result(new JsonObject { ["text"] = text }));

        private static DynamicToolCallResult Result(JsonObject result) =>
            new()
            {
                Success = true,
                StructuredResult = result
            };
    }

    private sealed class UnsupportedDynamicTools
    {
        [DynamicTool("Unsupported", Order = 10)]
        [Description("Unsupported tool.")]
        private DynamicToolCallResult Unsupported([Description("Unsupported value.")] DateTime value) =>
            new() { Success = true };
    }

    private sealed class ContextDynamicTools
    {
        [DynamicTool("Context", Order = 10)]
        [Description("Returns context and cancellation data.")]
        private DynamicToolCallResult Context(
            ManagedAppBindingToolCallContext context,
            CancellationToken cancellationToken,
            [Description("Optional text.")] string text = "default") =>
            new()
            {
                Success = true,
                StructuredResult = new JsonObject
                {
                    ["tool"] = context.ToolName,
                    ["cancelled"] = cancellationToken.IsCancellationRequested,
                    ["text"] = text
                }
            };
    }
}
