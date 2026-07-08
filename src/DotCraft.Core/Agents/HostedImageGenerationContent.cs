using Microsoft.Extensions.AI;

namespace DotCraft.Agents;

/// <summary>
/// Internal bridge content for OpenAI Responses hosted image generation output.
/// Session Core maps this to the existing ToolResult image presentation shape.
/// </summary>
public sealed class HostedImageGenerationContent : AIContent
{
    public const string ToolName = "image_generation";

    public string Id { get; init; } = string.Empty;

    public string Status { get; init; } = "completed";

    public string? RevisedPrompt { get; init; }

    public byte[]? ImageBytes { get; init; }

    public string MediaType { get; init; } = "image/png";

    public string? ErrorMessage { get; init; }

    public bool Succeeded =>
        string.Equals(Status, "completed", StringComparison.OrdinalIgnoreCase) &&
        ImageBytes is { Length: > 0 } &&
        string.IsNullOrWhiteSpace(ErrorMessage);
}
