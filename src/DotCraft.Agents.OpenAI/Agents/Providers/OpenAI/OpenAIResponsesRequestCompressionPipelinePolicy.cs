using System.ClientModel;
using System.ClientModel.Primitives;
using System.Security.Cryptography;
using System.Text.Json;
using ZstdSharp;

namespace DotCraft.Agents;

/// <summary>
/// Applies Zstandard request encoding to ChatGPT OAuth Responses sampling requests.
/// </summary>
internal sealed class OpenAIResponsesRequestCompressionPipelinePolicy : PipelinePolicy
{
    internal const string ContentEncodingHeader = "Content-Encoding";
    internal const string ZstdContentEncoding = "zstd";

    private sealed record OriginalBodySnapshot(byte[] Bytes, string Sha256);

    public override void Process(
        PipelineMessage message,
        IReadOnlyList<PipelinePolicy> pipeline,
        int currentIndex)
    {
        Compress(message);
        ProcessNext(message, pipeline, currentIndex);
    }

    public override async ValueTask ProcessAsync(
        PipelineMessage message,
        IReadOnlyList<PipelinePolicy> pipeline,
        int currentIndex)
    {
        await CompressAsync(message).ConfigureAwait(false);
        await ProcessNextAsync(message, pipeline, currentIndex).ConfigureAwait(false);
    }

    internal static bool Compress(PipelineMessage message)
    {
        if (!ShouldCompress(message.Request.Uri))
            return false;

        ValidateRequest(message);

        using var stream = new MemoryStream();
        message.Request.Content!.WriteTo(stream, message.CancellationToken);
        return ReplaceWithCompressedContent(message, stream.ToArray());
    }

    internal static async ValueTask<bool> CompressAsync(PipelineMessage message)
    {
        if (!ShouldCompress(message.Request.Uri))
            return false;

        ValidateRequest(message);

        await using var stream = new MemoryStream();
        await message.Request.Content!.WriteToAsync(stream, message.CancellationToken).ConfigureAwait(false);
        return ReplaceWithCompressedContent(message, stream.ToArray());
    }

    internal static bool TryGetOriginalBody(
        PipelineMessage message,
        out BinaryData? body,
        out string? sha256)
    {
        if (message.TryGetProperty(typeof(OriginalBodySnapshot), out var value)
            && value is OriginalBodySnapshot snapshot)
        {
            body = BinaryData.FromBytes(snapshot.Bytes);
            sha256 = snapshot.Sha256;
            return true;
        }

        body = null;
        sha256 = null;
        return false;
    }

    private static void ValidateRequest(PipelineMessage message)
    {
        if (message.Request.Content is null)
            throw new InvalidOperationException("Responses request must contain a JSON body.");

        if (message.Request.Headers.TryGetValue(ContentEncodingHeader, out var contentEncoding))
        {
            throw new InvalidOperationException(
                $"Responses request body must be unencoded before compression; found '{contentEncoding}'.");
        }
    }

    private static bool ShouldCompress(Uri? uri) =>
        OpenAIResponsesLitePipelinePath.IsStreamingResponses(uri);

    private static bool ReplaceWithCompressedContent(PipelineMessage message, byte[] original)
    {
        using var document = JsonDocument.Parse(original);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Responses request body must be a JSON object.");

        using var compressor = new Compressor(3);
        var compressed = compressor.Wrap(original).ToArray();
        var hash = Convert.ToHexString(SHA256.HashData(original)).ToLowerInvariant();
        message.SetProperty(
            typeof(OriginalBodySnapshot),
            new OriginalBodySnapshot((byte[])original.Clone(), hash));
        message.Request.Content = BinaryContent.Create(BinaryData.FromBytes(compressed));
        message.Request.Headers.Set(ContentEncodingHeader, ZstdContentEncoding);
        return true;
    }
}
