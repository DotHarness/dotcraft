using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Tools;
using Microsoft.Extensions.AI;

namespace DotCraft.Sessions;

internal static class ImageGenerationProjection
{
    public static ImageGenerationPayload Started(string callId, JsonObject? arguments) => new()
    {
        CallId = callId,
        RevisedPrompt = arguments?["prompt"] is JsonValue prompt && prompt.TryGetValue<string>(out var text)
            ? text.Trim()
            : null
    };

    public static ImageGenerationPayload Completed(ImageGenerationPayload started, ToolExecutionResult result)
    {
        var image = result.ContentItems?
            .OfType<DataContent>()
            .FirstOrDefault(static item => item.HasTopLevelMediaType("image"));
        if (!result.Success || image is null)
        {
            return started with
            {
                Status = "failed",
                ErrorCode = result.Error?.Code ?? ImageGenerationToolRuntime.FailedErrorCode,
                ErrorMessage = result.Error?.Message ?? result.Content ?? "Image generation failed.",
                ImagegenRequestId = ReadMeta(result, ImageGenerationToolRuntime.ImagegenRequestIdMeta)
            };
        }

        var savedPath = ReadMeta(result, ImageGenerationToolRuntime.SavedPathMeta);
        var saveErrorCode = ReadMeta(result, ImageGenerationToolRuntime.SaveErrorCodeMeta);
        return started with
        {
            Status = "completed",
            Result = Convert.ToBase64String(image.Data.ToArray()),
            MediaType = image.MediaType,
            SavedPath = savedPath,
            SaveStatus = savedPath is not null && saveErrorCode is null ? "saved" : "failed",
            SaveErrorCode = saveErrorCode,
            SavedHostId = ReadMeta(result, ImageGenerationToolRuntime.SavedHostIdMeta),
            SavedWorkspaceId = ReadMeta(result, ImageGenerationToolRuntime.SavedWorkspaceIdMeta),
            ImagegenRequestId = ReadMeta(result, ImageGenerationToolRuntime.ImagegenRequestIdMeta),
            GenerationId = ReadMeta(result, ImageGenerationToolRuntime.GenerationIdMeta)
        };
    }

    private static string? ReadMeta(ToolExecutionResult result, string name) =>
        result.Meta is { ValueKind: JsonValueKind.Object } meta
        && meta.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
