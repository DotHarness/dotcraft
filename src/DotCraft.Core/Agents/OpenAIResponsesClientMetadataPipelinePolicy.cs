using System.ClientModel;
using System.ClientModel.Primitives;
using DotCraft.Auth.OpenAI;

namespace DotCraft.Agents;

/// <summary>
/// Injects <c>client_metadata.x-codex-installation-id</c> into outgoing OpenAI Responses request
/// bodies when the underlying OpenAI client is bound to a ChatGPT subscription (OAuth) account.
/// The ChatGPT backend uses this field as a stable sticky-routing hint that improves
/// <c>prompt_cache_key</c> hit rates. Only patches requests whose path ends in
/// <c>/responses</c>; never overwrites an existing <c>client_metadata.x-codex-installation-id</c>
/// value placed by the caller.
/// </summary>
internal sealed class OpenAIResponsesClientMetadataPipelinePolicy : PipelinePolicy
{
    private const string ResponsesPathSuffix = "/responses";

    private readonly string _installationId;

    public OpenAIResponsesClientMetadataPipelinePolicy(string installationId)
    {
        if (string.IsNullOrWhiteSpace(installationId))
            throw new ArgumentException("Installation id must be non-empty.", nameof(installationId));
        _installationId = installationId.Trim();
    }

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
        var path = uri.AbsolutePath;
        return path.EndsWith(ResponsesPathSuffix, StringComparison.Ordinal);
    }

    private void RewriteRequestContent(PipelineMessage message)
    {
        if (message.Request.Content is null)
            return;

        using var stream = new MemoryStream();
        message.Request.Content.WriteTo(stream, message.CancellationToken);
        ApplyPatch(message, stream);
    }

    private async ValueTask RewriteRequestContentAsync(PipelineMessage message)
    {
        if (message.Request.Content is null)
            return;

        await using var stream = new MemoryStream();
        await message.Request.Content.WriteToAsync(stream, message.CancellationToken).ConfigureAwait(false);
        ApplyPatch(message, stream);
    }

    private void ApplyPatch(PipelineMessage message, MemoryStream stream)
    {
        var original = BinaryData.FromBytes(stream.ToArray()).ToString();
        var rewritten = AddInstallationIdMetadata(original, _installationId);
        if (rewritten is null)
            return;

        message.Request.Content = BinaryContent.Create(BinaryData.FromString(rewritten));
    }

    internal static string? AddInstallationIdMetadata(string json, string installationId)
    {
        return OpenAIResponsesRequestBodyCanonicalizer.AddInstallationIdMetadata(
            json,
            OpenAIAuthConstants.InstallationIdHeader,
            installationId);
    }
}
