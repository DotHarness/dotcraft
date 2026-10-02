using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Agents;
using DotCraft.Configuration;
using DotCraft.Security;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace DotCraft.Tools;

public sealed class ImageGenerationToolSource(
    AppConfig config,
    ChatClientRegistry chatClientRegistry,
    IApprovalService approvalService,
    PathBlacklist? pathBlacklist = null,
    string? userDataPath = null,
    IRemoteToolHostClient? remoteToolHostClient = null,
    ILogger? logger = null) : IToolSource
{
    public const string ToolNamespace = "image_gen";
    public const string ToolName = "imagegen";
    private const int HardMaxReferenceImages = 5;

    public string SourceId => "image-generation";

    public int Priority => 30;

    public ValueTask<IReadOnlyList<ToolRegistration>> GetRegistrationsAsync(
        ToolPlanningContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        if (!config.Tools.ImageGeneration.Enabled
            || !TryResolveRuntime(context.EffectiveProviderId, context.EffectiveMainModel, out var conversationRuntime))
        {
            return ValueTask.FromResult<IReadOnlyList<ToolRegistration>>([]);
        }

        var imageProviderId = config.Tools.ImageGeneration.Provider?.Trim();
        var runtime = conversationRuntime;
        if ((!string.IsNullOrEmpty(imageProviderId)
             && !TryResolveRuntime(imageProviderId, config.Tools.ImageGeneration.Model, out runtime))
            || !IsSupportedRuntime(runtime))
        {
            return ValueTask.FromResult<IReadOnlyList<ToolRegistration>>([]);
        }

        var maxReferenceImages = NormalizeMaxReferenceImages(config.Tools.ImageGeneration.MaxReferenceImages);
        var definitionId = new ToolDefinitionId(ToolSourceKind.CoreNative, SourceId, new SourceToolId(ToolName));
        var definition = new ToolDefinition(
            definitionId,
            new ToolName(ToolNamespace, ToolName),
            ImageGenerationToolText.Description(maxReferenceImages),
            ImageGenerationToolText.InputSchema(),
            annotations: CreateAnnotations(conversationRuntime.IsChatGptOAuth),
            provenance: new ToolProvenance(ToolSourceKind.CoreNative, SourceId, "native"));
        var toolRuntime = new ImageGenerationToolRuntime(
            runtime,
            chatClientRegistry,
            config.Tools.ImageGeneration.Model,
            maxReferenceImages,
            context.DataPath,
            new FileAccessGuard(
                context.WorkspacePath,
                context.RequireApprovalOutsideWorkspace ?? config.Tools.File.RequireApprovalOutsideWorkspace,
                approvalService,
                pathBlacklist,
                trustedReadPaths: userDataPath == null
                    ? [Path.GetFullPath(context.DataPath)]
                    : [Path.GetFullPath(userDataPath), Path.GetFullPath(context.DataPath)],
                workspaceRoots: context.WorkspaceRoots),
            remoteToolHostClient,
            logger);
        var binding = new ToolRuntimeBinding(
            new RuntimeBindingId($"native:{SourceId}:{ToolName}:{context.Revision}"),
            definitionId,
            toolRuntime,
            ToolBindingLeases.AlwaysAvailable,
            $"native:{SourceId}",
            context.Revision);
        return ValueTask.FromResult<IReadOnlyList<ToolRegistration>>(
            [new ToolRegistration(definition, binding, ToolProjectionShape.ImageGeneration)]);
    }

    private bool TryResolveRuntime(string? providerId, string? model, out EffectiveModelRuntime runtime)
    {
        try
        {
            runtime = chatClientRegistry.ResolveMainRuntime(config, providerId, model);
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or UriFormatException)
        {
            runtime = null!;
            return false;
        }
    }

    private static Dictionary<string, JsonElement> CreateAnnotations(bool reservedSchema)
    {
        var annotations = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["dotcraft/streamArguments"] = JsonSerializer.SerializeToElement(false)
        };
        if (reservedSchema)
            annotations[ReservedToolSchema.Annotation] = JsonSerializer.SerializeToElement(true);
        return annotations;
    }

    private static bool IsSupportedRuntime(EffectiveModelRuntime runtime) =>
        runtime.IsOpenAICompatible
        && runtime.SupportsImageGeneration
        && (runtime.IsChatGptOAuth
            || runtime.IsRemote
            || string.Equals(runtime.AuthMethod?.Trim(), ModelProviderAuthMethods.ApiKey, StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(runtime.ApiKey));

    private static int NormalizeMaxReferenceImages(int configured) =>
        Math.Clamp(configured, 1, HardMaxReferenceImages);
}

internal sealed class ImageGenerationToolRuntime(
    EffectiveModelRuntime runtime,
    ChatClientRegistry chatClientRegistry,
    string imageModel,
    int maxReferenceImages,
    string dataPath,
    FileAccessGuard fileAccessGuard,
    IRemoteToolHostClient? remoteToolHostClient,
    ILogger? logger) : IToolRuntime
{
    internal const string FailedErrorCode = "image_generation_failed";
    internal const string SavedPathMeta = "savedPath";
    internal const string SaveErrorCodeMeta = "saveErrorCode";
    internal const string SavedHostIdMeta = "savedHostId";
    internal const string SavedWorkspaceIdMeta = "savedWorkspaceId";
    internal const string ImagegenRequestIdMeta = "imagegenRequestId";
    internal const string GenerationIdMeta = "generationId";
    private const int MaxOutputHintBytes = 1024;

    private static readonly string[] ArgumentNames =
        ["prompt", "transparent_background", "referenced_image_paths", "num_last_images_to_include"];

    public async ValueTask<ToolExecutionResult> InvokeAsync(
        ToolInvocationContext context,
        JsonObject arguments,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(arguments);

        if (!TryParseArguments(arguments, out var request, out var error))
            return InvalidInput(error);

        RemoteToolRoute? route = remoteToolHostClient?.TryGetRoute(context.ThreadId, out var current) == true
            ? current
            : null;
        var references = request.ReferencedImagePaths.Count > 0
            ? await LoadReferencedImagesAsync(context, route, request.ReferencedImagePaths, cancellationToken)
                .ConfigureAwait(false)
            : request.RecentImageCount is { } count
                ? CollectRecentImages(StreamingFunctionInvokingChatClient.CurrentContext?.Messages ?? [], count)
                : ImageReferences.None;
        if (references.Error is not null)
            return InvalidInput(references.Error);

        ProviderImageResult generated;
        try
        {
            var provider = chatClientRegistry.GetProviderService<IProviderImageGeneration>(runtime)
                           ?? throw new InvalidOperationException(
                               $"Provider '{runtime.ProviderId}' does not support image generation.");
            generated = await provider.GenerateImageAsync(
                runtime,
                new ProviderImageRequest(
                    imageModel, request.Prompt, request.TransparentBackground, references.Urls, context.TurnId),
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is ProviderImageException or HttpRequestException or InvalidOperationException
                                       or ArgumentException or IOException or OperationCanceledException)
        {
            var message = ex is OperationCanceledException
                ? "the Images API request timed out"
                : TrimError(ex.Message);
            var failureMeta = new JsonObject
            {
                [ImagegenRequestIdMeta] = (ex as ProviderImageException)?.ImagegenRequestId
            };
            return new ToolExecutionResult(
                false,
                $"image generation failed: {message}",
                meta: JsonSerializer.SerializeToElement(failureMeta),
                error: new ToolError(FailedErrorCode, message));
        }

        var image = generated.Image;
        var saved = await SaveAsync(context.ThreadId, context.CallId, route, image, cancellationToken).ConfigureAwait(false);
        var hint = saved.Path is null ? null : CreateOutputHint(saved.Path);
        List<AIContent> contentItems = [new DataContent(image, "image/png")];
        if (hint is not null)
            contentItems.Add(new TextContent(hint));
        var meta = new JsonObject
        {
            [SavedPathMeta] = saved.Path,
            [SaveErrorCodeMeta] = saved.ErrorCode,
            [SavedHostIdMeta] = saved.Route?.HostId,
            [SavedWorkspaceIdMeta] = saved.Route?.WorkspaceId,
            [ImagegenRequestIdMeta] = generated.ImagegenRequestId,
            [GenerationIdMeta] = generated.GenerationId
        };
        return ToolExecutionResult.Succeeded(
            hint ?? "Generated image.",
            meta: JsonSerializer.SerializeToElement(meta),
            contentItems: contentItems);
    }

    private static bool TryParseArguments(
        JsonObject arguments,
        out ImageGenerationArguments request,
        out string error)
    {
        request = null!;
        error = string.Empty;
        foreach (var (name, _) in arguments)
        {
            if (Array.IndexOf(ArgumentNames, name) < 0)
            {
                error = $"unknown field `{name}`, expected one of `prompt`, `transparent_background`, "
                        + "`referenced_image_paths`, `num_last_images_to_include`";
                return false;
            }
        }

        if (arguments["prompt"] is not JsonValue promptValue
            || !promptValue.TryGetValue<string>(out var prompt)
            || string.IsNullOrWhiteSpace(prompt))
        {
            error = "`prompt` is required";
            return false;
        }

        var transparent = false;
        if (arguments["transparent_background"] is { } transparentNode
            && (transparentNode is not JsonValue transparentValue || !transparentValue.TryGetValue(out transparent)))
        {
            error = "`transparent_background` must be a boolean";
            return false;
        }

        var paths = new List<string>();
        if (arguments["referenced_image_paths"] is { } pathsNode)
        {
            if (pathsNode is not JsonArray pathsArray)
            {
                error = "`referenced_image_paths` must be an array of paths";
                return false;
            }

            foreach (var pathNode in pathsArray)
            {
                if (pathNode is not JsonValue pathValue
                    || !pathValue.TryGetValue<string>(out var path)
                    || string.IsNullOrWhiteSpace(path))
                {
                    error = "`referenced_image_paths` must contain non-empty paths";
                    return false;
                }

                paths.Add(path.Trim());
            }
        }

        int? recentCount = null;
        if (arguments["num_last_images_to_include"] is { } countNode)
        {
            if (countNode is not JsonValue countValue || !countValue.TryGetValue<int>(out var count))
            {
                error = "`num_last_images_to_include` must be an integer";
                return false;
            }

            recentCount = count;
        }

        if (paths.Count > 0 && recentCount is not null)
        {
            error = "provide only one of `referenced_image_paths` or `num_last_images_to_include`";
            return false;
        }

        request = new ImageGenerationArguments(prompt.Trim(), transparent, paths, recentCount);
        return true;
    }

    internal ImageReferences CollectRecentImages(IEnumerable<ChatMessage> messages, int count)
    {
        if (count < 1 || count > maxReferenceImages)
            return ImageReferences.Failure($"`num_last_images_to_include` must be between 1 and {maxReferenceImages}");

        var urls = new List<string>(count);
        foreach (var message in messages.Reverse())
        {
            foreach (var content in message.Contents.Reverse())
            {
                foreach (var url in ImageUrlsNewestFirst(content))
                {
                    urls.Add(url);
                    if (urls.Count == count)
                    {
                        urls.Reverse();
                        return new ImageReferences(urls, null);
                    }
                }
            }
        }

        return ImageReferences.Failure(
            $"requested the last {count} conversation images, but only {urls.Count} were available");
    }

    private static IEnumerable<string> ImageUrlsNewestFirst(AIContent content)
    {
        switch (content)
        {
            case DataContent data when ModelImageInputPreparer.IsImageMediaType(data.MediaType):
                if (ToDataUrl(data) is { } url)
                    yield return url;
                break;
            case FunctionResultContent result
                when ImageContentSanitizingChatClient.TryGetResultContentItems(result.Result, out var items):
                foreach (var item in items.Reverse())
                {
                    if (item is DataContent itemData
                        && ModelImageInputPreparer.IsImageMediaType(itemData.MediaType)
                        && ToDataUrl(itemData) is { } itemUrl)
                    {
                        yield return itemUrl;
                    }
                }
                break;
        }
    }

    private async Task<ImageReferences> LoadReferencedImagesAsync(
        ToolInvocationContext context,
        RemoteToolRoute? route,
        IReadOnlyList<string> paths,
        CancellationToken cancellationToken)
    {
        if (paths.Count > maxReferenceImages)
            return ImageReferences.Failure($"`referenced_image_paths` must contain at most {maxReferenceImages} paths");

        var urls = new List<string>(paths.Count);
        foreach (var path in paths)
        {
            var read = route is null
                ? await ReadLocalImageAsync(path, cancellationToken).ConfigureAwait(false)
                : await ReadRemoteImageAsync(context, route, path, cancellationToken).ConfigureAwait(false);
            if (read.Error is not null)
                return ImageReferences.Failure(read.Error);

            var url = ToDataUrl(new DataContent(read.Bytes!, DetectImageMediaType(read.Bytes!)));
            if (url is null)
                return ImageReferences.Failure($"unable to process referenced image at `{path}`");
            urls.Add(url);
        }

        return new ImageReferences(urls, null);
    }

    private async Task<ReferencedImage> ReadLocalImageAsync(string path, CancellationToken cancellationToken)
    {
        string fullPath;
        try
        {
            fullPath = fileAccessGuard.ResolvePath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return new ReferencedImage(null, $"invalid referenced image path `{path}`");
        }

        var accessError = await fileAccessGuard.ValidatePathAsync(fullPath, "read", path, cancellationToken)
            .ConfigureAwait(false);
        if (accessError is not null)
            return new ReferencedImage(null, accessError);

        try
        {
            return new ReferencedImage(await File.ReadAllBytesAsync(fullPath, cancellationToken).ConfigureAwait(false), null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new ReferencedImage(null, $"unable to read referenced image at `{path}`: {ex.Message}");
        }
    }

    private async Task<ReferencedImage> ReadRemoteImageAsync(
        ToolInvocationContext context,
        RemoteToolRoute route,
        string path,
        CancellationToken cancellationToken)
    {
        try
        {
            var bytes = await remoteToolHostClient!
                .ReadImageAsync(route, context.ThreadId, context.CallId, path, cancellationToken)
                .ConfigureAwait(false);
            return new ReferencedImage(bytes, null);
        }
        catch (RemoteToolHostException ex)
        {
            return new ReferencedImage(null, $"unable to read referenced image at `{path}`: {ex.Message}");
        }
    }

    private async Task<SavedImage> SaveAsync(
        string threadId,
        string callId,
        RemoteToolRoute? route,
        byte[] image,
        CancellationToken cancellationToken)
    {
        var threadSegment = SanitizeSegment(threadId);
        var callSegment = SanitizeSegment(callId);
        try
        {
            if (route is not null)
            {
                var remotePath = await remoteToolHostClient!
                    .WriteImageAsync(route, threadSegment, callSegment, image, cancellationToken)
                    .ConfigureAwait(false);
                return new SavedImage(remotePath, route, null);
            }

            var directory = Path.Combine(dataPath, "generated_images", threadSegment);
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, callSegment + ".png");
            await File.WriteAllBytesAsync(path, image, cancellationToken).ConfigureAwait(false);
            return new SavedImage(path, null, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
                                       or NotSupportedException or RemoteToolHostException or OperationCanceledException)
        {
            logger?.LogWarning("Generated image could not be saved for thread {ThreadId}: {ErrorType}", threadId, ex.GetType().Name);
            return new SavedImage(null, route, ex is RemoteToolHostException remoteError
                ? remoteError.Code
                : "image_generation_save_failed");
        }
    }

    internal static string? CreateOutputHint(string savedPath)
    {
        var separator = savedPath.LastIndexOfAny(['/', '\\']);
        var directory = separator > 0 ? savedPath[..separator] : savedPath;
        var hint =
            $"Generated images are saved to {directory} as {savedPath} by default.\n" +
            "If you need to use a generated image at another path, copy it and leave the original in place unless the user explicitly asks you to delete it.\n" +
            "The generated image is already displayed to the user. There is no need to render it in the final response as a Markdown image or file link.";
        return System.Text.Encoding.UTF8.GetByteCount(hint) <= MaxOutputHintBytes ? hint : null;
    }

    private static string SanitizeSegment(string value)
    {
        var sanitized = new string(value
            .Select(static ch => char.IsAsciiLetterOrDigit(ch) || ch is '-' or '_' ? ch : '_')
            .ToArray());
        return sanitized.Length == 0 ? "generated_image" : sanitized;
    }

    private static string? ToDataUrl(DataContent content)
    {
        var prepared = ModelImageInputPreparer.Prepare(content);
        return prepared.Content is { } image
            ? $"data:{image.MediaType};base64,{Convert.ToBase64String(image.Data.ToArray())}"
            : null;
    }

    private static string DetectImageMediaType(byte[] bytes)
    {
        try
        {
            return SixLabors.ImageSharp.Image.DetectFormat(bytes).DefaultMimeType;
        }
        catch (Exception ex) when (ex is ArgumentException or SixLabors.ImageSharp.ImageFormatException
                                       or SixLabors.ImageSharp.UnknownImageFormatException)
        {
            return "application/octet-stream";
        }
    }

    private static ToolExecutionResult InvalidInput(string message) =>
        ToolExecutionResult.Failed(new ToolError(ToolErrorCodes.InputInvalid, message), message);

    private static string TrimError(string message)
    {
        var trimmed = string.IsNullOrWhiteSpace(message) ? "unknown error" : message.Trim();
        return trimmed.Length <= 1000 ? trimmed : trimmed[..1000];
    }

    private sealed record SavedImage(string? Path, RemoteToolRoute? Route, string? ErrorCode);

    private sealed record ReferencedImage(byte[]? Bytes, string? Error);
}

internal sealed record ImageGenerationArguments(
    string Prompt,
    bool TransparentBackground,
    IReadOnlyList<string> ReferencedImagePaths,
    int? RecentImageCount);

internal sealed record ImageReferences(IReadOnlyList<string> Urls, string? Error)
{
    public static ImageReferences None { get; } = new([], null);

    public static ImageReferences Failure(string error) => new([], error);
}
