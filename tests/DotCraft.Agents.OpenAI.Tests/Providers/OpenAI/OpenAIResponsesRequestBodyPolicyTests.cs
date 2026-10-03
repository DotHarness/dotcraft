using System.ClientModel;
using System.ClientModel.Primitives;
using System.Text.Json;
using DotCraft.Agents;
using Xunit;

namespace DotCraft.Tests.Agents;

public sealed class OpenAIResponsesRequestBodyPolicyTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task RewrittenContentRemainsReadableAfterProcessing(bool useAsync, bool oauth)
    {
        using var message = ClientPipeline.Create(new ClientPipelineOptions()).CreateMessage();
        message.Request.Uri = new Uri("https://example.test/responses");
        message.Request.Content = BinaryContent.Create(BinaryData.FromBytes(
            """{"model":"old","input":[ { "text":"\u0061" } ],"model":"current","max_output_tokens":100}"""u8.ToArray()));
        var policy = new OpenAIResponsesRequestBodyCanonicalizationPipelinePolicy(oauth ? "install-1" : null);
        PipelinePolicy[] pipeline = [policy, new TerminalPolicy()];

        if (useAsync)
            await policy.ProcessAsync(message, pipeline, 0);
        else
            policy.Process(message, pipeline, 0);

        using var output = new MemoryStream();
        await message.Request.Content!.WriteToAsync(output, CancellationToken.None);
        using var document = JsonDocument.Parse(output.GetBuffer().AsMemory(0, (int)output.Length));
        var root = document.RootElement;
        Assert.Equal("current", root.GetProperty("model").GetString());
        Assert.Equal(1, root.EnumerateObject().Count(p => p.Name == "model"));
        Assert.Equal("""[ { "text":"\u0061" } ]""", root.GetProperty("input").GetRawText());
        Assert.Equal(!oauth, root.TryGetProperty("max_output_tokens", out _));
        if (oauth)
            Assert.Equal("install-1", root.GetProperty("client_metadata").GetProperty("x-codex-installation-id").GetString());
        else
            Assert.False(root.TryGetProperty("client_metadata", out _));
    }

    private sealed class TerminalPolicy : PipelinePolicy
    {
        public override void Process(PipelineMessage message, IReadOnlyList<PipelinePolicy> pipeline, int currentIndex) { }

        public override ValueTask ProcessAsync(
            PipelineMessage message, IReadOnlyList<PipelinePolicy> pipeline, int currentIndex) => ValueTask.CompletedTask;
    }
}
