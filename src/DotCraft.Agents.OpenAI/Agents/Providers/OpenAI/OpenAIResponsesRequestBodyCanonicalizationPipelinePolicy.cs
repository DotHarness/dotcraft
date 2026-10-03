using System.ClientModel;
using System.ClientModel.Primitives;
using DotCraft.Auth.OpenAI;
using Microsoft.Extensions.Logging;

namespace DotCraft.Agents;

/// <summary>
/// Canonicalizes Responses requests and applies OAuth body metadata before transport.
/// </summary>
internal sealed class OpenAIResponsesRequestBodyCanonicalizationPipelinePolicy(
    string? installationId = null,
    ILogger? logger = null) : PipelinePolicy
{
    private const string ResponsesPathSuffix = "/responses";
    private int _mismatchWarningLogged;

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

    private void RewriteRequestContent(PipelineMessage message)
    {
        if (message.Request.Content == null)
            return;

        using var stream = new MemoryStream();
        message.Request.Content.WriteTo(stream, message.CancellationToken);
        RewriteRequestContent(message, stream);
    }

    private async ValueTask RewriteRequestContentAsync(PipelineMessage message)
    {
        if (message.Request.Content == null)
            return;

        await using var stream = new MemoryStream();
        await message.Request.Content.WriteToAsync(stream, message.CancellationToken).ConfigureAwait(false);
        RewriteRequestContent(message, stream);
    }

    private void RewriteRequestContent(PipelineMessage message, MemoryStream stream)
    {
        var original = stream.GetBuffer().AsMemory(0, (int)stream.Length);
        ReadOnlyMemory<byte>? rewritten;
        if (string.IsNullOrWhiteSpace(installationId))
        {
            rewritten = OpenAIResponsesRequestBodyCanonicalizer.Canonicalize(original);
        }
        else
        {
            var snapshot = OpenAIResponsesCodexMetadata.GetOrCreateSnapshot(message, installationId);
            var result = OpenAIResponsesRequestBodyCanonicalizer.RewriteOAuthRequest(
                original,
                OpenAIResponsesCodexMetadata.BuildClientMetadata(snapshot));
            rewritten = result.Body;
            if (rewritten != null && result.InstallationIdMismatch
                && Interlocked.Exchange(ref _mismatchWarningLogged, 1) == 0)
            {
                logger?.LogWarning(
                    "Overwriting mismatched Responses client_metadata {InstallationIdHeader} with the local ChatGPT OAuth installation id.",
                    OpenAIAuthConstants.InstallationIdHeader);
            }
        }

        if (rewritten is not { } bytes)
            return;

        message.Request.Content = BinaryContent.Create(BinaryData.FromBytes(bytes));
    }
}
