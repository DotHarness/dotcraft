using System.ClientModel;
using System.ClientModel.Primitives;
using System.Text.Json;
using DotCraft.Auth.OpenAI;
using Microsoft.Extensions.Logging;

namespace DotCraft.Agents;

/// <summary>
/// Injects <c>client_metadata.x-codex-installation-id</c> into outgoing OpenAI Responses request
/// bodies when the underlying OpenAI client is bound to a ChatGPT subscription (OAuth) account.
/// The ChatGPT backend uses this field as a stable sticky-routing hint that improves
/// <c>prompt_cache_key</c> hit rates. Only patches requests whose path ends in
/// <c>/responses</c>; preserves matching caller metadata and overwrites a mismatched
/// installation id with the local ChatGPT OAuth installation id.
/// </summary>
internal sealed class OpenAIResponsesClientMetadataPipelinePolicy : PipelinePolicy
{
    private const string ResponsesPathSuffix = "/responses";

    private readonly string _installationId;
    private readonly ILogger? _logger;
    private int _mismatchWarningLogged;

    public OpenAIResponsesClientMetadataPipelinePolicy(string installationId, ILogger? logger = null)
    {
        if (string.IsNullOrWhiteSpace(installationId))
            throw new ArgumentException("Installation id must be non-empty.", nameof(installationId));
        _installationId = installationId.Trim();
        _logger = logger;
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
        var hadMismatchedInstallationId = HasDifferentInstallationIdMetadata(original, _installationId);
        var rewritten = AddInstallationIdMetadata(original, _installationId);
        if (rewritten is null)
            return;

        if (hadMismatchedInstallationId &&
            Interlocked.Exchange(ref _mismatchWarningLogged, 1) == 0)
        {
            _logger?.LogWarning(
                "Overwriting mismatched Responses client_metadata {InstallationIdHeader} with the local ChatGPT OAuth installation id.",
                OpenAIAuthConstants.InstallationIdHeader);
        }

        message.Request.Content = BinaryContent.Create(BinaryData.FromString(rewritten));
    }

    internal static string? AddInstallationIdMetadata(string json, string installationId)
    {
        return OpenAIResponsesRequestBodyCanonicalizer.AddInstallationIdMetadata(
            json,
            OpenAIAuthConstants.InstallationIdHeader,
            installationId);
    }

    private static bool HasDifferentInstallationIdMetadata(string json, string installationId)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty("client_metadata", out var metadata) ||
                metadata.ValueKind != JsonValueKind.Object ||
                !metadata.TryGetProperty(OpenAIAuthConstants.InstallationIdHeader, out var existing) ||
                existing.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            return !string.Equals(existing.GetString(), installationId, StringComparison.Ordinal);
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
