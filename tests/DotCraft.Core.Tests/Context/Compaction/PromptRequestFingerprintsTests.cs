using System.Reflection;
using System.Text.Json;
using DotCraft.Context;
using DotCraft.Context.Compaction;
using Microsoft.Extensions.AI;

namespace DotCraft.Tests.Context.Compaction;

public sealed class PromptRequestFingerprintsTests
{
    [Fact]
    public void ComputeToolFingerprint_CanonicalizesSchemaPropertyOrder()
    {
        var left = new SchemaTool(
            "Inspect",
            """{"properties":{"b":{"type":"string"},"a":{"type":"number"}},"type":"object"}""");
        var right = new SchemaTool(
            "Inspect",
            """{"type":"object","properties":{"a":{"type":"number"},"b":{"type":"string"}}}""");

        Assert.Equal(
            PromptRequestFingerprints.ComputeToolFingerprint([left]),
            PromptRequestFingerprints.ComputeToolFingerprint([right]));
    }

    private sealed class SchemaTool : AIFunction
    {
        private readonly JsonElement _schema;

        public SchemaTool(string name, string schemaJson)
        {
            Name = name;
            using var document = JsonDocument.Parse(schemaJson);
            _schema = document.RootElement.Clone();
        }

        public override string Name { get; }

        public override string Description => "Schema ordering test tool.";

        public override JsonElement JsonSchema => _schema;

        public override JsonElement? ReturnJsonSchema => null;

        public override MethodInfo? UnderlyingMethod => null;

        public override JsonSerializerOptions JsonSerializerOptions => JsonSerializerOptions.Default;

        protected override ValueTask<object?> InvokeCoreAsync(
            AIFunctionArguments arguments,
            CancellationToken cancellationToken) =>
            new((object?)null);
    }
}
