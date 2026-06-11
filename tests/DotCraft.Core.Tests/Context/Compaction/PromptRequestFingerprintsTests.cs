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

    [Fact]
    public void Capture_ContextUsageFingerprintExcludesBaseInstructions()
    {
        var tool = new SchemaTool("Inspect", """{"type":"object"}""");
        var messages = new List<ChatMessage> { new(ChatRole.User, "hello") };

        var first = PromptRequestSnapshot.Capture(
            messages,
            new ChatOptions
            {
                Instructions = "short memory",
                ModelId = "gpt-test",
                Tools = [tool]
            },
            providerId: "openai",
            mode: "agent");
        var second = PromptRequestSnapshot.Capture(
            messages,
            new ChatOptions
            {
                Instructions = "short memory plus newly consolidated durable memory",
                ModelId = "gpt-test",
                Tools = [tool]
            },
            providerId: "openai",
            mode: "agent");

        Assert.NotEqual(first.RequestFingerprint, second.RequestFingerprint);
        Assert.Equal(first.ContextUsageFingerprint, second.ContextUsageFingerprint);
        Assert.NotEqual(first.BaseInstructionsTokenEstimate, second.BaseInstructionsTokenEstimate);
    }

    [Fact]
    public void Capture_ContextUsageFingerprintIncludesToolShape()
    {
        var messages = new List<ChatMessage> { new(ChatRole.User, "hello") };

        var first = PromptRequestSnapshot.Capture(
            messages,
            new ChatOptions
            {
                Instructions = "same memory",
                ModelId = "gpt-test",
                Tools = [new SchemaTool("Inspect", """{"type":"object"}""")]
            },
            providerId: "openai",
            mode: "agent");
        var second = PromptRequestSnapshot.Capture(
            messages,
            new ChatOptions
            {
                Instructions = "same memory",
                ModelId = "gpt-test",
                Tools = [new SchemaTool("Inspect", """{"type":"object","properties":{"path":{"type":"string"}}}""")]
            },
            providerId: "openai",
            mode: "agent");

        Assert.NotEqual(first.ContextUsageFingerprint, second.ContextUsageFingerprint);
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
