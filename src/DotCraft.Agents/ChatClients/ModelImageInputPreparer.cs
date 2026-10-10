using Microsoft.Extensions.AI;
using DotCraft.Imaging;

namespace DotCraft.Agents;

internal static class ModelImageInputPreparer
{
    internal const string CouldNotProcessPlaceholder = "image content omitted because it could not be processed";
    internal const string TooLargePlaceholder = "image content omitted because it exceeded the supported size limit";
    private const int MaxInputBytes = 64 * 1024 * 1024;

    public static bool IsImageMediaType(string? mediaType) =>
        mediaType?.StartsWith("image/", StringComparison.OrdinalIgnoreCase) == true;

    public static bool IsSupportedRemoteImageMediaType(string? mediaType) =>
        NormalizeMediaType(mediaType) is "image/png" or "image/jpeg" or "image/webp" or "image/gif";

    public static PreparedModelImageInput Prepare(DataContent source)
    {
        if (!IsImageMediaType(source.MediaType))
            return PreparedModelImageInput.Placeholder(CouldNotProcessPlaceholder);
        if (source.Data.Length == 0 || source.Data.Length > MaxInputBytes)
            return PreparedModelImageInput.Placeholder(TooLargePlaceholder);
        if (!ImageProcessor.TryIdentify(source.Data.Span, out var info))
            return PreparedModelImageInput.Placeholder(CouldNotProcessPlaceholder);

        var (width, height) = ModelImageSizing.Fit(info.Size.Width, info.Size.Height);
        var format = info.Format is ImageFormat.Png or ImageFormat.Jpeg or ImageFormat.Webp ? info.Format : ImageFormat.Png;
        var result = ImageProcessor.Process(source.Data.Span, new(format) { TargetSize = new(width, height) });
        if (!result.IsSuccess)
            return PreparedModelImageInput.Placeholder(result.Error == ImageError.LimitExceeded ? TooLargePlaceholder : CouldNotProcessPlaceholder);
        var image = result.Image;
        return PreparedModelImageInput.Image(CopyMetadata(source, new DataContent(image.Data, image.MediaType)));
    }

    private static DataContent CopyMetadata(DataContent source, DataContent prepared)
    {
        if (source.AdditionalProperties is not { Count: > 0 } additionalProperties)
            return prepared;
        prepared.AdditionalProperties ??= new AdditionalPropertiesDictionary();
        foreach (var (key, value) in additionalProperties)
            prepared.AdditionalProperties[key] = value;
        return prepared;
    }

    private static string NormalizeMediaType(string? mediaType) =>
        string.IsNullOrWhiteSpace(mediaType) ? "application/octet-stream" : mediaType.Trim().ToLowerInvariant();

    internal sealed record PreparedModelImageInput(DataContent? Content, string? PlaceholderText)
    {
        public bool HasImage => Content != null;
        public static PreparedModelImageInput Image(DataContent content) => new(content, null);
        public static PreparedModelImageInput Placeholder(string text) => new(null, text);
    }
}
