using Microsoft.Extensions.AI;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Processing;

namespace DotCraft.Agents;

internal static class ModelImageInputPreparer
{
    internal const string CouldNotProcessPlaceholder = "image content omitted because it could not be processed";
    internal const string TooLargePlaceholder = "image content omitted because it exceeded the supported size limit";

    private const int MaxInputBytes = 64 * 1024 * 1024;
    private const int MaxDimension = 2048;
    private const int PatchSize = 32;
    private const int MaxPatches = 2500;

    public static bool IsImageMediaType(string? mediaType) =>
        mediaType?.StartsWith("image/", StringComparison.OrdinalIgnoreCase) == true;

    public static bool IsSupportedRemoteImageMediaType(string? mediaType) =>
        IsSupportedProviderImageMediaType(NormalizeMediaType(mediaType));

    public static PreparedModelImageInput Prepare(DataContent source)
    {
        if (!IsImageMediaType(source.MediaType))
            return PreparedModelImageInput.Placeholder(CouldNotProcessPlaceholder);

        if (source.Data.Length == 0 || source.Data.Length > MaxInputBytes)
            return PreparedModelImageInput.Placeholder(TooLargePlaceholder);

        var bytes = source.Data.ToArray();
        try
        {
            var detectedFormat = Image.DetectFormat(bytes);
            var detectedMediaType = NormalizeMediaType(detectedFormat.DefaultMimeType);
            var info = Image.Identify(bytes);
            if (info == null || info.Width <= 0 || info.Height <= 0)
                return PreparedModelImageInput.Placeholder(CouldNotProcessPlaceholder);

            var targetSize = CalculateTargetSize(info.Width, info.Height);
            var canPreserveSource = CanPreserveSourceBytes(detectedMediaType);
            if (canPreserveSource && targetSize.Width == info.Width && targetSize.Height == info.Height)
                return PreparedModelImageInput.Image(CopyMetadata(source, new DataContent(bytes, detectedMediaType)));

            using var image = Image.Load(bytes);
            if (targetSize.Width != image.Width || targetSize.Height != image.Height)
            {
                image.Mutate(context => context.Resize(targetSize.Width, targetSize.Height));
            }

            var outputMediaType = canPreserveSource ? detectedMediaType : "image/png";
            using var output = new MemoryStream();
            SaveImage(image, output, outputMediaType);
            return PreparedModelImageInput.Image(CopyMetadata(source, new DataContent(output.ToArray(), outputMediaType)));
        }
        catch (Exception ex) when (IsImagePreparationException(ex))
        {
            return PreparedModelImageInput.Placeholder(CouldNotProcessPlaceholder);
        }
    }

    private static Size CalculateTargetSize(int width, int height)
    {
        var scale = 1.0;
        var maxSide = Math.Max(width, height);
        if (maxSide > MaxDimension)
            scale = Math.Min(scale, MaxDimension / (double)maxSide);

        var patchBudgetPixels = MaxPatches * PatchSize * PatchSize;
        var pixelCount = (long)width * height;
        if (pixelCount > patchBudgetPixels)
            scale = Math.Min(scale, Math.Sqrt(patchBudgetPixels / (double)pixelCount));

        var targetWidth = Math.Max(1, (int)Math.Floor(width * scale));
        var targetHeight = Math.Max(1, (int)Math.Floor(height * scale));

        while (CountPatches(targetWidth, targetHeight) > MaxPatches)
        {
            scale *= 0.98;
            targetWidth = Math.Max(1, (int)Math.Floor(width * scale));
            targetHeight = Math.Max(1, (int)Math.Floor(height * scale));
        }

        return new Size(targetWidth, targetHeight);
    }

    private static long CountPatches(int width, int height) =>
        ((long)(width + PatchSize - 1) / PatchSize) * ((height + PatchSize - 1) / PatchSize);

    private static bool CanPreserveSourceBytes(string mediaType) =>
        string.Equals(mediaType, "image/png", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(mediaType, "image/jpeg", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(mediaType, "image/webp", StringComparison.OrdinalIgnoreCase);

    private static bool IsSupportedProviderImageMediaType(string mediaType) =>
        CanPreserveSourceBytes(mediaType) ||
        string.Equals(mediaType, "image/gif", StringComparison.OrdinalIgnoreCase);

    private static void SaveImage(Image image, Stream output, string mediaType)
    {
        switch (mediaType)
        {
            case "image/jpeg":
                image.SaveAsJpeg(output);
                break;
            case "image/webp":
                image.SaveAsWebp(output);
                break;
            default:
                image.SaveAsPng(output);
                break;
        }
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

    private static bool IsImagePreparationException(Exception ex) =>
        ex is ArgumentException
            or InvalidImageContentException
            or ImageFormatException
            or NotSupportedException
            or UnknownImageFormatException;

    private static string NormalizeMediaType(string? mediaType) =>
        string.IsNullOrWhiteSpace(mediaType)
            ? "application/octet-stream"
            : mediaType.Trim().ToLowerInvariant();

    internal sealed record PreparedModelImageInput(DataContent? Content, string? PlaceholderText)
    {
        public bool HasImage => Content != null;

        public static PreparedModelImageInput Image(DataContent content) => new(content, null);

        public static PreparedModelImageInput Placeholder(string text) => new(null, text);
    }
}
