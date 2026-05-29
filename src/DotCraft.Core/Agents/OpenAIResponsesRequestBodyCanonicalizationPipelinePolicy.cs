using System.ClientModel;
using System.ClientModel.Primitives;

namespace DotCraft.Agents;

/// <summary>
/// Removes duplicate top-level keys emitted by patched OpenAI Responses request options.
/// </summary>
internal sealed class OpenAIResponsesRequestBodyCanonicalizationPipelinePolicy : PipelinePolicy
{
    private const string ResponsesPathSuffix = "/responses";

    public override void Process(PipelineMessage message, IReadOnlyList<PipelinePolicy> pipeline, int currentIndex)
    {
        if (ShouldPatch(message))
            RewriteRequestContent(message);
        ProcessNext(message, pipeline, currentIndex);
    }

    public override async ValueTask ProcessAsync(
        PipelineMessage message,
        IReadOnlyList<PipelinePolicy> pipeline,
        int currentIndex)
    {
        if (ShouldPatch(message))
            await RewriteRequestContentAsync(message).ConfigureAwait(false);
        await ProcessNextAsync(message, pipeline, currentIndex).ConfigureAwait(false);
    }

    private static bool ShouldPatch(PipelineMessage message)
    {
        var uri = message.Request.Uri;
        if (uri is null)
            return false;

        return uri.AbsolutePath.EndsWith(ResponsesPathSuffix, StringComparison.Ordinal);
    }

    private static void RewriteRequestContent(PipelineMessage message)
    {
        if (message.Request.Content == null)
            return;

        using var stream = new MemoryStream();
        message.Request.Content.WriteTo(stream, message.CancellationToken);
        RewriteRequestContent(message, stream);
    }

    private static async ValueTask RewriteRequestContentAsync(PipelineMessage message)
    {
        if (message.Request.Content == null)
            return;

        await using var stream = new MemoryStream();
        await message.Request.Content.WriteToAsync(stream, message.CancellationToken).ConfigureAwait(false);
        RewriteRequestContent(message, stream);
    }

    private static void RewriteRequestContent(PipelineMessage message, MemoryStream stream)
    {
        var original = BinaryData.FromBytes(stream.ToArray()).ToString();
        var rewritten = OpenAIResponsesRequestBodyCanonicalizer.Canonicalize(original);
        if (rewritten == null)
            return;

        message.Request.Content = BinaryContent.Create(BinaryData.FromString(rewritten));
    }
}
