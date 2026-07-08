using System.ClientModel;
using System.ComponentModel;
using DotCraft.Abstractions;
using DotCraft.Agents;
using DotCraft.Configuration;
using DotCraft.Security;
using DotCraft.Tracing;
using Microsoft.Extensions.AI;
using OpenAI.Images;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;

#pragma warning disable OPENAI001

namespace DotCraft.Tools;

/// <summary>
/// Gates hosted OpenAI image generation support for provider request adapters.
/// </summary>
public sealed class ImageGenerationToolProvider : IAgentToolProvider
{
    internal const string ToolNamespace = "image_gen";
    internal const string ToolName = "imagegen";
    private const int HardMaxReferenceImages = 5;

    public int Priority => 23;

    public IEnumerable<AITool> CreateTools(ToolProviderContext context) => [];

    internal static bool ShouldEnableHostedImageGeneration(ToolProviderContext context) =>
        TryResolveSupportedRuntime(context, out _);

    internal static bool TryResolveSupportedRuntime(
        ToolProviderContext context,
        out EffectiveModelRuntime runtime)
    {
        runtime = null!;
        ArgumentNullException.ThrowIfNull(context);

        if (!context.Config.Tools.ImageGeneration.Enabled)
            return false;

        try
        {
            runtime = context.ChatClientRegistry.ResolveMainRuntime(
                context.Config,
                context.EffectiveProviderId,
                context.EffectiveMainModel);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or UriFormatException)
        {
            runtime = null!;
            return false;
        }

        return IsSupportedRuntime(runtime);
    }

    internal static bool IsSupportedRuntime(EffectiveModelRuntime runtime)
    {
        if (!runtime.IsOpenAIResponses)
            return false;

        if (!runtime.SupportsHostedImageGeneration)
            return false;

        return HasValidHostedImageGenerationAuth(runtime);
    }

    private static bool HasValidHostedImageGenerationAuth(EffectiveModelRuntime runtime)
    {
        if (runtime.IsChatGptOAuth)
            return true;

        return string.Equals(
                   runtime.AuthMethod?.Trim(),
                   ModelProviderAuthMethods.ApiKey,
                   StringComparison.OrdinalIgnoreCase) &&
               !string.IsNullOrWhiteSpace(runtime.ApiKey);
    }

    internal static int NormalizeMaxReferenceImages(int configured) =>
        Math.Clamp(configured, 1, HardMaxReferenceImages);

}

/// <summary>
/// Legacy client-side image generation helper. Hosted Responses image generation does not expose this as a model tool.
/// </summary>
public sealed class ImageGenerationTools
{
    private static readonly Dictionary<string, string> ImageExtensionToMediaType = new(StringComparer.OrdinalIgnoreCase)
    {
        [".png"] = "image/png",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".gif"] = "image/gif",
        [".webp"] = "image/webp",
        [".bmp"] = "image/bmp"
    };

    private readonly ToolProviderContext _context;
    private readonly EffectiveModelRuntime _runtime;
    private readonly IImageGenerationService _imageService;

    internal ImageGenerationTools(
        ToolProviderContext context,
        EffectiveModelRuntime runtime,
        IImageGenerationService? imageService = null)
    {
        _context = context;
        _runtime = runtime;
        _imageService = imageService ?? new OpenAIImageGenerationService(context.ChatClientRegistry);
    }

    [Description("Generate an image from a prompt, or edit an image using explicit reference image paths or recent image inputs. Returns a short text summary and image/png bytes.")]
    [Tool(CatalogVisible = false, Icon = "🖼️", MaxResultChars = 0)]
    public async Task<IList<AIContent>> imagegen(
        [Description("The image generation or image editing prompt.")] string prompt,
        [Description("Optional workspace-relative or absolute paths to reference images. Mutually exclusive with numLastImagesToInclude.")] string[]? referencedImagePaths = null,
        [Description("Optional number of most recent image inputs to use as references. Mutually exclusive with referencedImagePaths.")] int? numLastImagesToInclude = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(prompt))
                return TextOnly("Error: prompt is required.");

            if (!_context.Config.Tools.ImageGeneration.Enabled)
                return TextOnly("Error: image generation is disabled by Tools.ImageGeneration.Enabled.");

            if (!ImageGenerationToolProvider.IsSupportedRuntime(_runtime))
            {
                return TextOnly(
                    "Error: image generation is not enabled for this provider. Turn on SupportsHostedImageGeneration for a compatible OpenAI Responses provider.");
            }

            var imageModel = _context.Config.Tools.ImageGeneration.Model.Trim();
            if (string.IsNullOrWhiteSpace(imageModel))
                return TextOnly("Error: Tools.ImageGeneration.Model must be configured.");

            var maxReferenceImages = ImageGenerationToolProvider.NormalizeMaxReferenceImages(
                _context.Config.Tools.ImageGeneration.MaxReferenceImages);
            var explicitPaths = NormalizeReferencedPaths(referencedImagePaths, out var pathError);
            if (pathError != null)
                return TextOnly(pathError);

            if (explicitPaths.Count > 0 && numLastImagesToInclude.HasValue)
                return TextOnly("Error: referenced_image_paths and num_last_images_to_include are mutually exclusive.");

            if (explicitPaths.Count > maxReferenceImages)
            {
                return TextOnly(
                    $"Error: referenced_image_paths accepts at most {maxReferenceImages} image(s).");
            }

            if (numLastImagesToInclude is <= 0)
                return TextOnly("Error: num_last_images_to_include must be greater than zero.");

            if (numLastImagesToInclude > maxReferenceImages)
            {
                return TextOnly(
                    $"Error: num_last_images_to_include accepts at most {maxReferenceImages} image(s).");
            }

            var references = explicitPaths.Count > 0
                ? await LoadExplicitReferenceImagesAsync(explicitPaths, maxReferenceImages, cancellationToken)
                    .ConfigureAwait(false)
                : LoadRecentReferenceImages(numLastImagesToInclude.GetValueOrDefault());
            if (references.Error != null)
                return TextOnly(references.Error);

            var imageBytes = references.Images.Count == 0
                ? await _imageService.GenerateAsync(_runtime, imageModel, prompt.Trim(), cancellationToken)
                    .ConfigureAwait(false)
                : await _imageService.EditAsync(_runtime, imageModel, prompt.Trim(), references.Images, cancellationToken)
                    .ConfigureAwait(false);

            var savedPath = await SaveGeneratedImageAsync(imageBytes, cancellationToken).ConfigureAwait(false);
            var relativePath = Path.GetRelativePath(_context.WorkspacePath, savedPath)
                .Replace(Path.DirectorySeparatorChar, '/');
            var verb = references.Images.Count == 0 ? "Generated" : "Edited";
            var summary = $"{verb} image saved to {relativePath}.";
            return [new TextContent(summary), new DataContent(imageBytes, "image/png")];
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (ClientResultException ex)
        {
            return TextOnly($"Error: image generation failed: {TrimError(ex.Message)}");
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or HttpRequestException or IOException)
        {
            return TextOnly($"Error: image generation failed: {TrimError(ex.Message)}");
        }
    }

    private async Task<ReferenceImageLoadResult> LoadExplicitReferenceImagesAsync(
        IReadOnlyList<string> paths,
        int maxReferenceImages,
        CancellationToken cancellationToken)
    {
        if (paths.Count == 0)
            return ReferenceImageLoadResult.Success([]);

        if (paths.Count > maxReferenceImages)
            return ReferenceImageLoadResult.Failure($"Error: at most {maxReferenceImages} reference image(s) are supported.");

        var guard = CreateFileAccessGuard();
        var images = new List<ImageGenerationReferenceImage>(paths.Count);
        for (var i = 0; i < paths.Count; i++)
        {
            var originalPath = paths[i];
            string fullPath;
            try
            {
                fullPath = guard.ResolvePath(originalPath);
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                return ReferenceImageLoadResult.Failure($"Error: invalid image path '{originalPath}'.");
            }

            var validationError = await guard.ValidatePathAsync(
                fullPath,
                "read",
                originalPath,
                cancellationToken).ConfigureAwait(false);
            if (validationError != null)
                return ReferenceImageLoadResult.Failure(validationError);

            if (Directory.Exists(fullPath))
                return ReferenceImageLoadResult.Failure($"Error: reference image path is a directory: {originalPath}");

            if (!File.Exists(fullPath))
                return ReferenceImageLoadResult.Failure($"Error: reference image not found: {originalPath}");

            var bytes = await File.ReadAllBytesAsync(fullPath, cancellationToken).ConfigureAwait(false);
            var mediaType = DetectImageMediaType(fullPath, bytes);
            var prepared = ModelImageInputPreparer.Prepare(new DataContent(bytes, mediaType));
            if (!prepared.HasImage || prepared.Content == null)
            {
                return ReferenceImageLoadResult.Failure(
                    $"Error: reference image could not be processed: {originalPath}");
            }

            images.Add(ToReferenceImage(
                prepared.Content,
                Path.GetFileName(fullPath),
                index: i + 1));
        }

        return ReferenceImageLoadResult.Success(images);
    }

    private ReferenceImageLoadResult LoadRecentReferenceImages(int count)
    {
        if (count <= 0)
            return ReferenceImageLoadResult.Success([]);

        var invocation = StreamingFunctionInvokingChatClient.CurrentContext;
        if (invocation == null)
        {
            return ReferenceImageLoadResult.Failure(
                "Error: num_last_images_to_include requires an active tool invocation with image history.");
        }

        var images = new List<ImageGenerationReferenceImage>(count);
        var messages = invocation.Messages?.ToList() ?? [];
        for (var messageIndex = messages.Count - 1; messageIndex >= 0 && images.Count < count; messageIndex--)
        {
            var contents = messages[messageIndex].Contents;
            for (var contentIndex = contents.Count - 1; contentIndex >= 0 && images.Count < count; contentIndex--)
            {
                if (contents[contentIndex] is not DataContent dataContent ||
                    !ModelImageInputPreparer.IsImageMediaType(dataContent.MediaType))
                {
                    continue;
                }

                var prepared = ModelImageInputPreparer.Prepare(dataContent);
                if (!prepared.HasImage || prepared.Content == null)
                    continue;

                images.Add(ToReferenceImage(
                    prepared.Content,
                    $"recent-image-{images.Count + 1}",
                    index: images.Count + 1));
            }
        }

        if (images.Count < count)
        {
            return ReferenceImageLoadResult.Failure(
                $"Error: requested {count} recent image(s), but only found {images.Count}.");
        }

        images.Reverse();
        return ReferenceImageLoadResult.Success(images);
    }

    private FileAccessGuard CreateFileAccessGuard()
    {
        var userDotCraftPath = Path.GetFullPath(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".craft"));
        var botPath = Path.GetFullPath(_context.BotPath);
        return new FileAccessGuard(
            _context.WorkspacePath,
            _context.RequireApprovalOutsideWorkspace ?? _context.Config.Tools.File.RequireApprovalOutsideWorkspace,
            _context.ApprovalService,
            _context.PathBlacklist,
            trustedReadPaths: [userDotCraftPath, botPath]);
    }

    private async Task<string> SaveGeneratedImageAsync(byte[] imageBytes, CancellationToken cancellationToken)
    {
        var threadId = _context.CurrentThreadId ??
                       TracingChatClient.CurrentSessionKey ??
                       TracingChatClient.GetActiveSessionKey() ??
                       "thread";
        var invocation = StreamingFunctionInvokingChatClient.CurrentContext;
        var callId = invocation?.CallContent?.CallId ?? Guid.NewGuid().ToString("N");

        var outputDirectory = Path.Combine(
            _context.WorkspacePath,
            ".craft",
            "generated_images",
            SanitizePathSegment(threadId));
        Directory.CreateDirectory(outputDirectory);

        var outputPath = Path.Combine(outputDirectory, SanitizePathSegment(callId) + ".png");
        await File.WriteAllBytesAsync(outputPath, imageBytes, cancellationToken).ConfigureAwait(false);
        return outputPath;
    }

    private static List<string> NormalizeReferencedPaths(string[]? paths, out string? error)
    {
        error = null;
        if (paths == null || paths.Length == 0)
            return [];

        var result = new List<string>(paths.Length);
        foreach (var path in paths)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                error = "Error: referenced_image_paths cannot contain empty paths.";
                return [];
            }

            result.Add(path.Trim());
        }

        return result;
    }

    private static ImageGenerationReferenceImage ToReferenceImage(
        DataContent content,
        string fileName,
        int index)
    {
        var mediaType = NormalizeMediaType(content.MediaType);
        return new ImageGenerationReferenceImage(
            content.Data.ToArray(),
            mediaType,
            EnsureImageFileName(fileName, mediaType, index));
    }

    private static string DetectImageMediaType(string path, byte[] bytes)
    {
        if (ImageExtensionToMediaType.TryGetValue(Path.GetExtension(path), out var mediaType))
            return mediaType;

        try
        {
            var format = Image.DetectFormat(bytes);
            return NormalizeMediaType(format.DefaultMimeType);
        }
        catch (Exception ex) when (ex is ArgumentException or ImageFormatException or UnknownImageFormatException)
        {
            return "application/octet-stream";
        }
    }

    private static string EnsureImageFileName(string fileName, string mediaType, int index)
    {
        var stem = Path.GetFileNameWithoutExtension(fileName);
        if (string.IsNullOrWhiteSpace(stem))
            stem = $"reference-{index}";

        var extension = mediaType switch
        {
            "image/jpeg" => ".jpg",
            "image/webp" => ".webp",
            _ => ".png"
        };
        return SanitizePathSegment(stem) + extension;
    }

    private static string NormalizeMediaType(string? mediaType) =>
        string.IsNullOrWhiteSpace(mediaType)
            ? "application/octet-stream"
            : mediaType.Trim().ToLowerInvariant();

    private static string SanitizePathSegment(string value)
    {
        var trimmed = string.IsNullOrWhiteSpace(value) ? "image" : value.Trim();
        var invalid = Path.GetInvalidFileNameChars();
        var chars = trimmed.Select(ch =>
            invalid.Contains(ch) || ch is '/' or '\\' or ':' || char.IsControl(ch)
                ? '_'
                : ch).ToArray();
        var sanitized = new string(chars).Trim('.', ' ');
        return string.IsNullOrWhiteSpace(sanitized) ? "image" : sanitized;
    }

    private static IList<AIContent> TextOnly(string text) => [new TextContent(text)];

    private static string TrimError(string message)
    {
        var trimmed = string.IsNullOrWhiteSpace(message) ? "unknown error" : message.Trim();
        return trimmed.Length <= 1000 ? trimmed : trimmed[..1000];
    }

    private sealed record ReferenceImageLoadResult(
        IReadOnlyList<ImageGenerationReferenceImage> Images,
        string? Error)
    {
        public static ReferenceImageLoadResult Success(IReadOnlyList<ImageGenerationReferenceImage> images) =>
            new(images, null);

        public static ReferenceImageLoadResult Failure(string error) =>
            new([], error);
    }
}

internal interface IImageGenerationService
{
    Task<byte[]> GenerateAsync(
        EffectiveModelRuntime runtime,
        string imageModel,
        string prompt,
        CancellationToken cancellationToken);

    Task<byte[]> EditAsync(
        EffectiveModelRuntime runtime,
        string imageModel,
        string prompt,
        IReadOnlyList<ImageGenerationReferenceImage> images,
        CancellationToken cancellationToken);
}

internal sealed class OpenAIImageGenerationService(ChatClientRegistry registry) : IImageGenerationService
{
    public async Task<byte[]> GenerateAsync(
        EffectiveModelRuntime runtime,
        string imageModel,
        string prompt,
        CancellationToken cancellationToken)
    {
        var client = registry.GetOpenAIImageClient(runtime, imageModel);
        var result = await client.GenerateImageAsync(
            prompt,
            CreateGenerationOptions(),
            cancellationToken).ConfigureAwait(false);
        return ExtractImageBytes(result.Value);
    }

    public async Task<byte[]> EditAsync(
        EffectiveModelRuntime runtime,
        string imageModel,
        string prompt,
        IReadOnlyList<ImageGenerationReferenceImage> images,
        CancellationToken cancellationToken)
    {
        if (images.Count == 0)
            throw new ArgumentException("At least one reference image is required.", nameof(images));

        if (images.Count == 1)
        {
            var client = registry.GetOpenAIImageClient(runtime, imageModel);
            using var imageStream = new MemoryStream(images[0].Bytes, writable: false);
            var result = await client.GenerateImageEditAsync(
                imageStream,
                images[0].FileName,
                prompt,
                CreateEditOptions(),
                cancellationToken).ConfigureAwait(false);
            return ExtractImageBytes(result.Value);
        }

        var editInputs = images
            .Select(image => new OpenAIImageEditInput(image.Bytes, image.FileName, image.MediaType))
            .ToArray();
        return await registry.GenerateOpenAIImageEditAsync(
            runtime,
            imageModel,
            prompt,
            editInputs,
            cancellationToken).ConfigureAwait(false);
    }

    private static OpenAI.Images.ImageGenerationOptions CreateGenerationOptions() => new()
    {
        ResponseFormat = GeneratedImageFormat.Bytes,
        OutputFileFormat = GeneratedImageFileFormat.Png,
        Size = GeneratedImageSize.Auto,
        Quality = GeneratedImageQuality.Auto
    };

    private static ImageEditOptions CreateEditOptions() => new()
    {
        ResponseFormat = GeneratedImageFormat.Bytes,
        OutputFileFormat = GeneratedImageFileFormat.Png,
        Size = GeneratedImageSize.Auto,
        Quality = GeneratedImageQuality.Auto
    };

    private static byte[] ExtractImageBytes(GeneratedImage image)
    {
        if (image.ImageBytes == null)
            throw new InvalidOperationException("OpenAI image response did not include image bytes.");

        return image.ImageBytes.ToArray();
    }
}

internal sealed record ImageGenerationReferenceImage(
    byte[] Bytes,
    string MediaType,
    string FileName);
