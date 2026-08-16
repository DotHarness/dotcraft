using System.Text.Json;
using DotCraft.Protocol.AppServer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Oratorio.Server.Domain;
using Oratorio.Server.DotCraft;

namespace Oratorio.Server.Tests;

public sealed class OratorioDynamicToolCatalogTests
{
    private readonly OratorioDynamicToolCatalog _catalog =
        new(NullLogger<OratorioDynamicToolCatalog>.Instance);

    [Fact]
    public void Descriptors_generate_nine_closed_typed_schemas()
    {
        Assert.Equal(9, _catalog.Descriptors.Count);
        Assert.All(_catalog.Descriptors, descriptor =>
        {
            Assert.Equal(OratorioDynamicToolCatalog.Namespace, descriptor.Namespace);
            Assert.Equal($"{descriptor.Namespace}.{descriptor.LocalName}", descriptor.QualifiedName);
            AssertAllObjectsClosed(descriptor.InputSchema);
        });

        var list = Descriptor(OratorioDynamicToolCatalog.ListBoardItemsName).InputSchema;
        Assert.Equal(1, list.GetProperty("properties").GetProperty("limit").GetProperty("minimum").GetInt32());
        Assert.Equal(100, list.GetProperty("properties").GetProperty("limit").GetProperty("maximum").GetInt32());

        var create = Descriptor(OratorioDynamicToolCatalog.CreateBoardTaskName);
        Assert.False(create.DeferLoading);
        Assert.Contains("title", Required(create.InputSchema));
    }

    [Fact]
    public void Review_schema_uses_flat_kind_discriminator_without_nested_union_fields()
    {
        var schema = Descriptor(OratorioDynamicToolCatalog.SubmitReviewDraftName).InputSchema;
        var comment = schema.GetProperty("properties").GetProperty("comments")
            .GetProperty("items");
        var properties = comment.GetProperty("properties");

        Assert.Contains("kind", Required(comment));
        Assert.Contains("title", Required(comment));
        Assert.Contains("body", Required(comment));
        Assert.Contains("path", Required(comment));
        Assert.Equal(["suggestion", "commentOnly"],
            properties.GetProperty("kind").GetProperty("enum").EnumerateArray().Select(x => x.GetString()!).ToArray());
        Assert.True(properties.TryGetProperty("oldText", out _));
        Assert.True(properties.TryGetProperty("newText", out _));
        Assert.True(properties.TryGetProperty("line", out _));
        Assert.True(properties.TryGetProperty("reason", out _));
        Assert.False(properties.TryGetProperty("suggestion", out _));
        Assert.False(properties.TryGetProperty("commentOnly", out _));
        Assert.False(comment.GetProperty("additionalProperties").GetBoolean());
    }

    [Fact]
    public void Tool_selection_matrix_keeps_declarations_prompt_ids_and_allowlist_in_sync()
    {
        var normal = _catalog.CreateRunToolSet(RunPurpose.ReviewAnalysis, ItemKind.Issue, "github");
        AssertSet(normal,
            OratorioDynamicToolCatalog.SubmitDiscussionReplyName,
            OratorioDynamicToolCatalog.ResolveReviewFindingName,
            OratorioDynamicToolCatalog.SubmitFollowUpDraftName);

        var review = _catalog.CreateRunToolSet(RunPurpose.ReviewAnalysis, ItemKind.PullRequest, "gitlab");
        AssertSet(review,
            OratorioDynamicToolCatalog.SubmitDiscussionReplyName,
            OratorioDynamicToolCatalog.ResolveReviewFindingName,
            OratorioDynamicToolCatalog.SubmitFollowUpDraftName,
            OratorioDynamicToolCatalog.SubmitReviewDraftName);

        var implementation = _catalog.CreateRunToolSet(RunPurpose.Implementation, ItemKind.LocalTask, "local");
        AssertSet(implementation,
            OratorioDynamicToolCatalog.SubmitDiscussionReplyName,
            OratorioDynamicToolCatalog.ResolveReviewFindingName,
            OratorioDynamicToolCatalog.SubmitFollowUpDraftName,
            OratorioDynamicToolCatalog.SubmitImplementationDraftName);

        AssertSet(
            _catalog.CreateDiscussionToolSet(),
            OratorioDynamicToolCatalog.SubmitDiscussionReplyName,
            OratorioDynamicToolCatalog.ResolveReviewFindingName);
    }

    [Theory]
    [InlineData("""{"summary":{"body":"summary"},"comments":[{"kind":"suggestion","title":"t","body":"b","path":"a.cs","suggestion":{"oldText":"a","newText":"b"}}]}""")]
    [InlineData("""{"summary":{"body":"summary"},"comments":[{"kind":"unknown","title":"t","body":"b","path":"a.cs"}]}""")]
    public async Task Review_rejects_old_nested_shape_and_unknown_kind(string json)
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        var arguments = JsonDocument.Parse(json).RootElement.Clone();
        var call = new DynamicToolCallParams
        {
            ThreadId = string.Empty,
            TurnId = string.Empty,
            CallId = "call-1",
            Tool = OratorioDynamicToolCatalog.SubmitReviewDraftName,
            Arguments = arguments
        };
        var result = await _catalog.InvokeAsync(
            call,
            new OratorioToolInvocationContext(
                services,
                call,
                OratorioToolSurface.AppBinding,
                BindingGrant: new OratorioAppBindingGrantContext("binding-1", 1)),
            new HashSet<string>(StringComparer.Ordinal) { OratorioDynamicToolCatalog.SubmitReviewDraftName },
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("InvalidArguments", result.ErrorCode);
    }

    [Theory]
    [InlineData("""{"summary":{"body":"summary"},"comments":[{"kind":"suggestion","title":"t","body":"b","path":"a.cs","newText":"b"}]}""")]
    [InlineData("""{"summary":{"body":"summary"},"comments":[{"kind":"commentOnly","title":"t","body":"b","path":"a.cs","line":3}]}""")]
    public async Task Review_kind_requires_its_branch_fields(string json)
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        var arguments = JsonDocument.Parse(json).RootElement.Clone();
        var call = new DynamicToolCallParams
        {
            ThreadId = string.Empty,
            TurnId = string.Empty,
            CallId = "call-1",
            Tool = OratorioDynamicToolCatalog.SubmitReviewDraftName,
            Arguments = arguments
        };
        var result = await _catalog.InvokeAsync(
            call,
            new OratorioToolInvocationContext(
                services,
                call,
                OratorioToolSurface.AppBinding,
                BindingGrant: new OratorioAppBindingGrantContext("binding-1", 1)),
            new HashSet<string>(StringComparer.Ordinal) { OratorioDynamicToolCatalog.SubmitReviewDraftName },
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("InvalidArguments", result.ErrorCode);
        Assert.Contains("requires", result.ErrorMessage);
    }

    private global::DotCraft.Sdk.DynamicTools.DynamicToolDescriptor Descriptor(string name) =>
        Assert.Single(_catalog.Descriptors, x => x.LocalName == name);

    private static string[] Required(JsonElement schema) =>
        schema.TryGetProperty("required", out var required)
            ? required.EnumerateArray().Select(x => x.GetString()!).ToArray()
            : [];

    private static void AssertSet(OratorioDynamicToolSet set, params string[] expected)
    {
        Assert.Equal(expected.Order(StringComparer.Ordinal), set.AllowedLocalNames.Order(StringComparer.Ordinal));
        Assert.Equal(
            expected.Select(x => $"{OratorioDynamicToolCatalog.Namespace}.{x}").Order(StringComparer.Ordinal),
            set.QualifiedIds);
        Assert.Equal(expected.Order(StringComparer.Ordinal), Flatten(set.Declarations).Order(StringComparer.Ordinal));
    }

    private static IEnumerable<string> Flatten(IEnumerable<RuntimeDynamicToolDeclaration> declarations) =>
        declarations.SelectMany(declaration => declaration switch
        {
            RuntimeDynamicToolNamespace group =>
                group.Tools.OfType<RuntimeDynamicToolFunction>().Select(x => x.Name),
            RuntimeDynamicToolFunction function => [function.Name],
            _ => []
        });

    private static void AssertAllObjectsClosed(JsonElement schema)
    {
        if (schema.ValueKind != JsonValueKind.Object)
        {
            return;
        }
        if (schema.TryGetProperty("type", out var type) && type.GetString() == "object")
        {
            Assert.False(schema.GetProperty("additionalProperties").GetBoolean());
        }
        if (schema.TryGetProperty("properties", out var properties))
        {
            foreach (var property in properties.EnumerateObject())
            {
                AssertAllObjectsClosed(property.Value);
            }
        }
        if (schema.TryGetProperty("items", out var items))
        {
            AssertAllObjectsClosed(items);
        }
    }
}
