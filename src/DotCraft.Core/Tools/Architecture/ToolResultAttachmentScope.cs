using System.Text.Json;

namespace DotCraft.Tools;

internal sealed class ToolResultAttachments
{
    public JsonElement? StructuredContent { get; private set; }

    public void SetStructuredContent(JsonElement value) => StructuredContent = value.Clone();
}

internal static class ToolResultAttachmentScope
{
    private static readonly AsyncLocal<ToolResultAttachments?> CurrentAttachments = new();

    public static ToolResultAttachments? Current => CurrentAttachments.Value;

    public static IDisposable Set(ToolResultAttachments attachments) => Replace(attachments);

    public static IDisposable Suppress() => Replace(null);

    private static IDisposable Replace(ToolResultAttachments? attachments)
    {
        var previous = CurrentAttachments.Value;
        CurrentAttachments.Value = attachments;
        return new Scope(previous);
    }

    private sealed class Scope(ToolResultAttachments? previous) : IDisposable
    {
        public void Dispose() => CurrentAttachments.Value = previous;
    }
}
