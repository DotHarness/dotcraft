using Microsoft.Extensions.AI;

namespace DotCraft.Sessions.Wire;

internal static class SessionInputPartResolver
{
    internal const int MaxInlineImageBytes = 64 * 1024 * 1024;
    internal const string RemoteImageUrlErrorCode = "RemoteImageUrlNotSupported";
    internal const string RemoteImageUrlError =
        "remote image URLs are not supported; use an inline data URL instead";
    internal const string RemoteImageOmittedText =
        "image content omitted because remote image URLs are not supported";

    private const string InvalidInlineImageError =
        "image.url must be a valid base64 data:image/... URL";
    private const string OversizedInlineImageError =
        "inline image data exceeds the 64 MiB decoded size limit";
    private const string InvalidImageOmittedText =
        "image content omitted because it could not be processed";

    public static Task<List<AIContent>> ResolveStrictAsync(
        IReadOnlyList<SessionWireInputPart> parts,
        CancellationToken ct) =>
        ResolveAsync(parts, rejectInvalidImages: true, MaxInlineImageBytes, ct);

    internal static Task<List<AIContent>> ResolveStrictAsync(
        IReadOnlyList<SessionWireInputPart> parts,
        int maxInlineImageBytes,
        CancellationToken ct) =>
        ResolveAsync(parts, rejectInvalidImages: true, maxInlineImageBytes, ct);

    public static Task<List<AIContent>> ResolvePersistedAsync(
        IReadOnlyList<SessionWireInputPart> parts,
        CancellationToken ct) =>
        ResolveAsync(parts, rejectInvalidImages: false, MaxInlineImageBytes, ct);

    internal static AIContent ResolvePersistedImage(string? url) =>
        ResolveInlineImage(url, rejectInvalidImages: false, MaxInlineImageBytes);

    private static async Task<List<AIContent>> ResolveAsync(
        IReadOnlyList<SessionWireInputPart> parts,
        bool rejectInvalidImages,
        int maxInlineImageBytes,
        CancellationToken ct)
    {
        var result = new List<AIContent>(parts.Count);
        foreach (var part in parts)
        {
            AIContent content;
            switch (part.Type)
            {
                case "localImage" when part.Path is { } path:
                    content = await ResolveLocalImageAsync(path, part.MimeType, part.FileName, ct);
                    break;
                case "image":
                    content = ResolveInlineImage(part.Url, rejectInvalidImages, maxInlineImageBytes);
                    break;
                default:
                    content = part.ToAIContent();
                    break;
            }

            result.Add(content);
        }

        return result;
    }

    private static AIContent ResolveInlineImage(
        string? url,
        bool rejectInvalidImages,
        int maxInlineImageBytes)
    {
        if (url != null && IsRemoteImageUrl(url))
        {
            if (rejectInvalidImages)
                throw new SessionInputPartValidationException(
                    RemoteImageUrlErrorCode,
                    RemoteImageUrlError);

            return new TextContent(RemoteImageOmittedText);
        }

        if (TryDecodeInlineImage(url, maxInlineImageBytes, out var content, out var validationError))
            return content;

        if (rejectInvalidImages)
            throw new SessionInputPartValidationException("InvalidInlineImage", validationError);

        return new TextContent(InvalidImageOmittedText);
    }

    private static bool TryDecodeInlineImage(
        string? url,
        int maxInlineImageBytes,
        out DataContent content,
        out string validationError)
    {
        content = null!;
        validationError = InvalidInlineImageError;
        if (string.IsNullOrWhiteSpace(url)
            || !url.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var commaIndex = url.IndexOf(',');
        if (commaIndex <= "data:".Length || commaIndex == url.Length - 1)
            return false;

        var metadata = url["data:".Length..commaIndex];
        var metadataParts = metadata.Split(';');
        var mediaType = metadataParts[0].Trim();
        if (mediaType.Length <= "image/".Length
            || !mediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)
            || !metadataParts.Skip(1).Any(part =>
                string.Equals(part.Trim(), "base64", StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        var encoded = url[(commaIndex + 1)..];
        var encodedLength = encoded.Count(c => !char.IsWhiteSpace(c));
        if (encodedLength == 0 || encodedLength % 4 != 0)
            return false;

        var padding = CountBase64Padding(encoded);
        var decodedLengthUpperBound = ((long)encodedLength / 4 * 3) - padding;
        if (decodedLengthUpperBound > maxInlineImageBytes)
        {
            validationError = OversizedInlineImageError;
            return false;
        }

        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(encoded);
        }
        catch (FormatException)
        {
            return false;
        }

        if (bytes.Length == 0)
            return false;
        if (bytes.Length > maxInlineImageBytes)
        {
            validationError = OversizedInlineImageError;
            return false;
        }

        content = new DataContent(bytes, mediaType);
        return true;
    }

    private static int CountBase64Padding(string encoded)
    {
        var padding = 0;
        for (var i = encoded.Length - 1; i >= 0 && padding < 2; i--)
        {
            if (char.IsWhiteSpace(encoded[i]))
                continue;
            if (encoded[i] != '=')
                break;
            padding++;
        }

        return padding;
    }

    private static bool IsRemoteImageUrl(string url) =>
        url.Split(':', 2) is [var scheme, _]
        && (string.Equals(scheme, "http", StringComparison.OrdinalIgnoreCase)
            || string.Equals(scheme, "https", StringComparison.OrdinalIgnoreCase));

    private static async Task<AIContent> ResolveLocalImageAsync(
        string path,
        string? mimeTypeHint,
        string? fileNameHint,
        CancellationToken ct)
    {
        try
        {
            var bytes = await File.ReadAllBytesAsync(path, ct);
            var data = new DataContent(bytes, InferMediaType(path));
            data.AdditionalProperties ??= new AdditionalPropertiesDictionary();
            data.AdditionalProperties["localImage.path"] = path;
            if (!string.IsNullOrWhiteSpace(mimeTypeHint))
                data.AdditionalProperties["localImage.mimeType"] = mimeTypeHint.Trim();
            if (!string.IsNullOrWhiteSpace(fileNameHint))
                data.AdditionalProperties["localImage.fileName"] = fileNameHint.Trim();
            return data;
        }
        catch
        {
            return new TextContent($"[localImage:{path}]");
        }
    }

    private static string InferMediaType(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".bmp" => "image/bmp",
            _ => "image/png"
        };
    }
}

internal sealed class SessionInputPartValidationException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
