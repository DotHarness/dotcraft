using DotCraft.Agents;
using DotCraft.Tools;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

#pragma warning disable MEAI001

namespace DotCraft.Sessions;

internal sealed class ImageGenerationLifecycle(
    string dataPath, ILogger? logger, IRemoteToolHostClient? remote, string threadId,
    SessionTurn turn, SessionEventChannel eventChannel, Func<int> nextItemSeq)
{
    private readonly Dictionary<string, ImageCall> _calls = new(StringComparer.Ordinal);
    private sealed record ImageCall(SessionItem Item, RemoteToolRoute? Route);

    public void Start(ImageGenerationToolCallContent call) =>
        Start(ResolveImageGenerationCallId(call.CallId), TryReadImageGenerationRevisedPrompt(call));

    public void FinalizePending()
    {
        foreach (var call in _calls.Values)
            if (call.Item.Status != ItemStatus.Completed)
                Complete(call.Item, call.Item.AsImageGeneration! with
                {
                    Status = "failed",
                    ErrorMessage = "Image generation ended without a final result.",
                    ErrorCode = "image_generation_result_missing"
                });
    }

    public Task CompleteAsync(ImageGenerationToolResultContent result, CancellationToken ct)
    {
        var success = TryExtractImageGenerationImage(result, out var bytes, out var mediaType, out var error);
        return CompleteAsync(new HostedImageGenerationContent
        {
            Id = ResolveImageGenerationCallId(result.CallId),
            Status = success ? "completed" : "failed",
            RevisedPrompt = TryReadImageGenerationRevisedPrompt(result),
            ImageBytes = success ? bytes : null,
            MediaType = mediaType,
            ErrorMessage = success ? null : error
        }, ct);
    }

    public async Task CompleteAsync(HostedImageGenerationContent content, CancellationToken ct)
    {
        var callId = ResolveImageGenerationCallId(content.Id);
        var item = Start(callId, content.RevisedPrompt);
        if (item.Status == ItemStatus.Completed) return;
        var payload = item.AsImageGeneration!;
        if (content.Succeeded && content.ImageBytes is { Length: > 0 } bytes)
        {
            var saved = await SaveHostedImageGenerationAsync(callId, bytes, ct).ConfigureAwait(false);
            Complete(item, payload with
            {
                Status = "completed",
                Result = Convert.ToBase64String(bytes),
                MediaType = NormalizeImageGenerationMediaType(content.MediaType),
                SavedPath = saved.Path,
                SaveStatus = saved.ErrorCode is null ? "saved" : "failed",
                SaveErrorCode = saved.ErrorCode,
                SavedHostId = saved.Route?.HostId,
                SavedWorkspaceId = saved.Route?.WorkspaceId
            });
        }
        else
        {
            Complete(item, payload with
            {
                Status = "failed",
                ErrorCode = "image_generation_failed",
                ErrorMessage = string.IsNullOrWhiteSpace(content.ErrorMessage) ? "Image generation failed." : content.ErrorMessage.Trim()
            });
        }
    }

    private SessionItem Start(string callId, string? revisedPrompt)
    {
        if (_calls.TryGetValue(callId, out var existing))
        {
            if (existing.Item.Status != ItemStatus.Completed && !string.IsNullOrWhiteSpace(revisedPrompt))
                existing.Item.Payload = existing.Item.AsImageGeneration! with { RevisedPrompt = revisedPrompt.Trim() };
            return existing.Item;
        }
        var item = new SessionItem
        {
            Id = SessionIdGenerator.NewItemId(nextItemSeq()),
            TurnId = turn.Id,
            Type = ItemType.ImageGeneration,
            Status = ItemStatus.Started,
            CreatedAt = DateTimeOffset.UtcNow,
            Payload = new ImageGenerationPayload { CallId = callId, RevisedPrompt = revisedPrompt?.Trim() }
        };
        _calls.Add(callId, new(item, remote?.TryGetRoute(threadId, out var route) == true ? route : null));
        turn.Items.Add(item);
        eventChannel.EmitItemStarted(item);
        return item;
    }

    private void Complete(SessionItem item, ImageGenerationPayload payload)
    {
        item.Status = ItemStatus.Completed;
        item.CompletedAt = DateTimeOffset.UtcNow;
        item.Payload = payload;
        eventChannel.EmitItemCompleted(item);
    }

    private static bool TryGetStringProperty(AdditionalPropertiesDictionary? properties, string key, out string? value)
    {
        value = properties?.TryGetValue(key, out var raw) == true ? raw?.ToString() : null;
        return !string.IsNullOrWhiteSpace(value);
    }

    private async Task<ImageSaveResult> SaveHostedImageGenerationAsync(
        string callId, byte[] imageBytes, CancellationToken ct)
    {
        var destination = _calls[callId].Route;
        try
        {
            if (destination is not null)
            {
                var path = await remote!.WriteImageAsync(destination, threadId, callId, imageBytes, ct).ConfigureAwait(false);
                return new(path, destination, null);
            }
            var directory = Path.Combine(dataPath, "generated_images", SanitizePathSegment(threadId));
            Directory.CreateDirectory(directory);
            var output = Path.Combine(directory, SanitizePathSegment(callId) + ".png");
            await File.WriteAllBytesAsync(output, imageBytes, ct).ConfigureAwait(false);
            return new(output, null, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
                                   or NotSupportedException or RemoteToolHostException or OperationCanceledException)
        {
            logger?.LogWarning("Image artifact persistence failed for thread {ThreadId}: {ErrorType}", threadId, ex.GetType().Name);
            return new(null, destination, ex is RemoteToolHostException remoteError
                ? remoteError.Code : "image_generation_save_failed");
        }
    }

    private sealed record ImageSaveResult(string? Path, RemoteToolRoute? Route, string? ErrorCode);

    private static string ResolveImageGenerationCallId(string? callId) =>
        string.IsNullOrWhiteSpace(callId)
            ? "ig_" + Guid.NewGuid().ToString("N")
            : callId.Trim();

    private static string? TryReadImageGenerationRevisedPrompt(AIContent content)
    {
        if (TryGetStringProperty(content.AdditionalProperties, "revisedPrompt", out var revisedPrompt) ||
            TryGetStringProperty(content.AdditionalProperties, "revised_prompt", out revisedPrompt))
        {
            return revisedPrompt;
        }

        return null;
    }

    private static bool TryExtractImageGenerationImage(
        ImageGenerationToolResultContent result,
        out byte[] imageBytes,
        out string mediaType,
        out string errorMessage)
    {
        imageBytes = [];
        mediaType = "image/png";
        errorMessage = "Image generation returned no inline image data.";
        var sawRemoteImage = false;

        if (result.Outputs is not { Count: > 0 } outputs)
            return false;

        foreach (var output in outputs)
        {
            switch (output)
            {
                case DataContent data when data.HasTopLevelMediaType("image"):
                    try
                    {
                        var bytes = data.Data.ToArray();
                        if (bytes.Length > 0)
                        {
                            imageBytes = bytes;
                            mediaType = NormalizeImageGenerationMediaType(data.MediaType);
                            errorMessage = string.Empty;
                            return true;
                        }
                    }
                    catch (InvalidOperationException)
                    {
                    }
                    break;
                case UriContent uri when uri.HasTopLevelMediaType("image"):
                    sawRemoteImage = true;
                    mediaType = NormalizeImageGenerationMediaType(uri.MediaType);
                    break;
            }
        }

        if (sawRemoteImage)
            errorMessage = "Image generation returned a remote image URI, but no inline image data.";
        return false;
    }

    private static string NormalizeImageGenerationMediaType(string? mediaType) =>
        string.IsNullOrWhiteSpace(mediaType) ? "image/png" : mediaType.Trim();

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

}
