using System.ComponentModel;
using System.Text.Json;
using DotCraft.Sdk.Tools;

namespace DotCraft.Sdk.Tests;

public class ToolsTests
{
    private static readonly JsonElement Empty = JsonDocument.Parse("{}").RootElement;

    // ---------------- record convention ----------------

    private enum SortMode { Ascending, Descending }

    private sealed class SearchArgs
    {
        [Description("free text query")]
        public required string Query { get; init; }

        [Description("max rows")]
        [SchemaMinimum(1)]
        [SchemaMaximum(100)]
        public int? Limit { get; init; }

        public SortMode Sort { get; init; }
    }

    private sealed class EmptyArgs
    {
    }

    private sealed class ConfirmArgs
    {
        [SchemaConstTrue]
        public bool Confirm { get; init; }
    }

    [SchemaAllowAdditionalProperties]
    private sealed class FreeFormArgs
    {
    }

    private sealed class RecordHandler
    {
        public SearchArgs? LastSearch;

        [DynamicTool("search", "Search things", Order = 2)]
        public object Search(SearchArgs args)
        {
            LastSearch = args;
            return new { echoed = args.Query, args.Limit, args.Sort };
        }

        [DynamicTool("ping", "No args", Order = 1)]
        public object Ping(EmptyArgs args) => new { pong = true };

        [DynamicTool("confirm_only", "Needs confirm", Order = 3)]
        public object ConfirmOnly(ConfirmArgs args) => new { confirmed = args.Confirm };

        [DynamicTool("free_form", "Arbitrary payload", Order = 4)]
        public object FreeForm(FreeFormArgs args) => new { ok = true };

        [DynamicTool("boom", "Throws structured", Order = 5)]
        public object Boom(EmptyArgs args) => throw new DynamicToolException("BAD_THING", "nope", "field1", "try again");

        [DynamicTool("explode", "Throws unexpected", Order = 6)]
        public object Explode(EmptyArgs args) => throw new InvalidOperationException("kaboom");

        [DynamicTool("async_echo", "Async", Order = 7)]
        public Task<object> AsyncEcho(SearchArgs args) => Task.FromResult<object>(new { q = args.Query });
    }

    private static DynamicToolRegistry BuildRecordRegistry(out RecordHandler handler)
    {
        handler = new RecordHandler();
        var registry = new DynamicToolRegistry(new DynamicToolRegistryOptions
        {
            InvalidArgumentHint = "check args",
        });
        registry.Register(handler, "sample");
        return registry;
    }

    [Fact]
    public void Descriptors_are_ordered_and_namespaced()
    {
        DynamicToolRegistry registry = BuildRecordRegistry(out _);
        IReadOnlyList<DynamicToolDescriptor> descriptors = registry.ListDescriptors();

        Assert.Equal(
            new[]
            {
                "sample.ping", "sample.search", "sample.confirm_only", "sample.free_form",
                "sample.boom", "sample.explode", "sample.async_echo",
            },
            descriptors.Select(d => d.Name));
        Assert.Equal("Search things", descriptors.Single(d => d.Name == "sample.search").Description);
    }

    [Fact]
    public void Record_schema_has_constraints_description_and_required()
    {
        DynamicToolRegistry registry = BuildRecordRegistry(out _);
        JsonElement schema = registry.ListDescriptors().Single(d => d.Name == "sample.search").InputSchema;

        Assert.Equal("object", schema.GetProperty("type").GetString());
        Assert.False(schema.GetProperty("additionalProperties").GetBoolean());

        JsonElement props = schema.GetProperty("properties");
        JsonElement query = props.GetProperty("query");
        Assert.Equal("string", query.GetProperty("type").GetString());
        Assert.Equal("free text query", query.GetProperty("description").GetString());

        JsonElement limit = props.GetProperty("limit");
        Assert.Equal("integer", limit.GetProperty("type").GetString());
        Assert.Equal(1, limit.GetProperty("minimum").GetInt32());
        Assert.Equal(100, limit.GetProperty("maximum").GetInt32());

        JsonElement sort = props.GetProperty("sort");
        Assert.Equal("string", sort.GetProperty("type").GetString());
        string[] enumValues = sort.GetProperty("enum").EnumerateArray().Select(e => e.GetString()!).ToArray();
        Assert.Equal(new[] { "ascending", "descending" }, enumValues);

        string[] required = schema.GetProperty("required").EnumerateArray().Select(e => e.GetString()!).ToArray();
        Assert.Equal(new[] { "query" }, required);
    }

    [Fact]
    public void Empty_args_schema_has_empty_properties()
    {
        DynamicToolRegistry registry = BuildRecordRegistry(out _);
        JsonElement schema = registry.ListDescriptors().Single(d => d.Name == "sample.ping").InputSchema;

        Assert.Equal("object", schema.GetProperty("type").GetString());
        Assert.False(schema.GetProperty("additionalProperties").GetBoolean());
        Assert.Equal(JsonValueKind.Object, schema.GetProperty("properties").ValueKind);
        Assert.Empty(schema.GetProperty("properties").EnumerateObject());
    }

    [Fact]
    public void Const_true_flag_becomes_boolean_enum()
    {
        DynamicToolRegistry registry = BuildRecordRegistry(out _);
        JsonElement schema = registry.ListDescriptors().Single(d => d.Name == "sample.confirm_only").InputSchema;
        JsonElement confirm = schema.GetProperty("properties").GetProperty("confirm");

        Assert.Equal("boolean", confirm.GetProperty("type").GetString());
        Assert.True(confirm.GetProperty("enum")[0].GetBoolean());
    }

    [Fact]
    public void Free_form_object_allows_additional_properties()
    {
        DynamicToolRegistry registry = BuildRecordRegistry(out _);
        JsonElement schema = registry.ListDescriptors().Single(d => d.Name == "sample.free_form").InputSchema;
        Assert.True(schema.GetProperty("additionalProperties").GetBoolean());
    }

    [Fact]
    public async Task Invoke_record_success_returns_outcome_data()
    {
        DynamicToolRegistry registry = BuildRecordRegistry(out RecordHandler handler);
        JsonElement args = JsonDocument.Parse("""{"query":"hello","limit":5,"sort":"descending"}""").RootElement;

        DynamicToolOutcome outcome = await registry.InvokeAsync("sample", "search", args, default);

        Assert.True(outcome.Ok);
        Assert.Equal("hello", handler.LastSearch!.Query);
        Assert.Equal(5, handler.LastSearch.Limit);
        Assert.Equal(SortMode.Descending, handler.LastSearch.Sort);
    }

    [Fact]
    public async Task Invoke_async_tool_unwraps_task_result()
    {
        DynamicToolRegistry registry = BuildRecordRegistry(out _);
        JsonElement args = JsonDocument.Parse("""{"query":"x"}""").RootElement;
        DynamicToolOutcome outcome = await registry.InvokeAsync("sample", "async_echo", args, default);
        Assert.True(outcome.Ok);
    }

    [Fact]
    public async Task Structured_exception_maps_to_error_outcome()
    {
        DynamicToolRegistry registry = BuildRecordRegistry(out _);
        DynamicToolOutcome outcome = await registry.InvokeAsync("sample", "boom", Empty, default);

        Assert.False(outcome.Ok);
        Assert.Equal("BAD_THING", outcome.Code);
        Assert.Equal("nope", outcome.Message);
        Assert.Equal("field1", outcome.Field);
        Assert.Equal("try again", outcome.Hint);
    }

    [Fact]
    public async Task Unexpected_exception_maps_to_internal_and_logs()
    {
        Exception? logged = null;
        var registry = new DynamicToolRegistry(new DynamicToolRegistryOptions
        {
            InternalErrorLogger = (ex, _) => logged = ex,
        });
        registry.Register(new RecordHandler(), "sample");

        DynamicToolOutcome outcome = await registry.InvokeAsync("sample", "explode", Empty, default);

        Assert.False(outcome.Ok);
        Assert.Equal("INTERNAL", outcome.Code);
        Assert.IsType<InvalidOperationException>(logged);
    }

    [Fact]
    public async Task Invalid_arguments_map_to_invalid_argument_with_hint()
    {
        DynamicToolRegistry registry = BuildRecordRegistry(out _);
        JsonElement bad = JsonDocument.Parse("""{"limit":"not-a-number"}""").RootElement;

        DynamicToolOutcome outcome = await registry.InvokeAsync("sample", "search", bad, default);

        Assert.False(outcome.Ok);
        Assert.Equal("INVALID_ARGUMENT", outcome.Code);
        Assert.Equal("check args", outcome.Hint);
    }

    [Fact]
    public async Task Unknown_tool_returns_unknown_tool_error()
    {
        DynamicToolRegistry registry = BuildRecordRegistry(out _);
        DynamicToolOutcome outcome = await registry.InvokeAsync("sample", "missing", Empty, default);
        Assert.False(outcome.Ok);
        Assert.Equal("UNKNOWN_TOOL", outcome.Code);
    }

    [Fact]
    public async Task Json_envelope_shapes_success_and_error()
    {
        DynamicToolRegistry registry = BuildRecordRegistry(out _);

        JsonElement ok = await registry.InvokeJsonEnvelopeAsync(
            "sample", "search", JsonDocument.Parse("""{"query":"z"}""").RootElement, default);
        Assert.True(ok.GetProperty("ok").GetBoolean());
        Assert.Equal("z", ok.GetProperty("data").GetProperty("echoed").GetString());

        JsonElement err = await registry.InvokeJsonEnvelopeAsync("sample", "boom", Empty, default);
        Assert.False(err.GetProperty("ok").GetBoolean());
        Assert.Equal("BAD_THING", err.GetProperty("error").GetProperty("code").GetString());
        // null field/hint are omitted only when null; here they are present.
        Assert.Equal("field1", err.GetProperty("error").GetProperty("field").GetString());
    }

    // ---------------- flat-parameter convention ----------------

    private sealed class SampleContext
    {
        public string User { get; init; } = "";
    }

    private sealed class FlatHandler
    {
        public SampleContext? SeenContext;

        [DynamicTool("assign_task")]
        [Description("Assign a task")]
        public object AssignTask(
            SampleContext context,
            [Description("assignee id")] string assignee,
            [Description("optional note")] string? note = null,
            CancellationToken cancellationToken = default)
        {
            SeenContext = context;
            return new { assignee, note };
        }
    }

    [Fact]
    public void Flat_schema_excludes_context_and_token_and_marks_required()
    {
        var registry = new DynamicToolRegistry(new DynamicToolRegistryOptions { ContextType = typeof(SampleContext) });
        registry.Register(new FlatHandler(), "sample");

        JsonElement schema = registry.ListDescriptors().Single().InputSchema;
        JsonElement props = schema.GetProperty("properties");

        Assert.True(props.TryGetProperty("assignee", out _));
        Assert.True(props.TryGetProperty("note", out _));
        Assert.False(props.TryGetProperty("context", out _));
        Assert.False(props.TryGetProperty("cancellationToken", out _));
        Assert.Equal("assignee id", props.GetProperty("assignee").GetProperty("description").GetString());

        string[] required = schema.GetProperty("required").EnumerateArray().Select(e => e.GetString()!).ToArray();
        Assert.Equal(new[] { "assignee" }, required);
    }

    [Fact]
    public async Task Flat_invoke_binds_properties_and_injects_context()
    {
        var handler = new FlatHandler();
        var registry = new DynamicToolRegistry(new DynamicToolRegistryOptions { ContextType = typeof(SampleContext) });
        registry.Register(handler, "sample");

        var context = new SampleContext { User = "alice" };
        JsonElement args = JsonDocument.Parse("""{"assignee":"bob","note":"hi"}""").RootElement;

        DynamicToolOutcome outcome = await registry.InvokeAsync("sample", "assign_task", args, context, default);

        Assert.True(outcome.Ok);
        Assert.Equal("alice", handler.SeenContext!.User);
    }

    [Fact]
    public void Duplicate_tool_name_throws()
    {
        var registry = new DynamicToolRegistry();
        registry.Register(new RecordHandler(), "sample");
        Assert.Throws<InvalidOperationException>(() => registry.Register(new RecordHandler(), "sample"));
    }
}
