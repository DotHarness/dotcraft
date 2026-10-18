using System.Text.Json.Nodes;

namespace DotCraft.ExternalChannel.Messaging;

internal sealed record SendChannelMessageAttachment(string Path, string? Kind, string? Name);

internal sealed record SendChannelMessageRequest(
    string? Text,
    IReadOnlyList<SendChannelMessageAttachment> Attachments,
    string? To)
{
    internal static readonly string[] AttachmentKinds = ["image", "file", "audio", "video"];

    public static bool TryParse(JsonObject arguments, out SendChannelMessageRequest request, out string error)
    {
        request = new SendChannelMessageRequest(null, [], null);
        if (!TryReadString(arguments, "text", out var text)
            || !TryReadString(arguments, "to", out var to))
        {
            error = "text and to must be strings.";
            return false;
        }

        var attachments = new List<SendChannelMessageAttachment>();
        if (arguments.TryGetPropertyValue("attachments", out var attachmentsNode) && attachmentsNode != null)
        {
            if (attachmentsNode is not JsonArray array)
            {
                error = "attachments must be an array.";
                return false;
            }

            foreach (var entry in array)
            {
                if (!TryReadAttachment(entry, out var attachment, out error))
                    return false;
                attachments.Add(attachment);
            }
        }

        text = string.IsNullOrEmpty(text) ? null : text;
        if (text == null && attachments.Count == 0)
        {
            error = "Provide text, attachments, or both.";
            return false;
        }

        request = new SendChannelMessageRequest(text, attachments, string.IsNullOrWhiteSpace(to) ? null : to.Trim());
        error = string.Empty;
        return true;
    }

    private static bool TryReadAttachment(JsonNode? entry, out SendChannelMessageAttachment attachment, out string error)
    {
        attachment = new SendChannelMessageAttachment(string.Empty, null, null);
        if (entry is not JsonObject value
            || !TryReadString(value, "path", out var path)
            || !TryReadString(value, "kind", out var kind)
            || !TryReadString(value, "name", out var name))
        {
            error = "Each attachment must be an object with a string path and optional string kind and name.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(path))
        {
            error = "Each attachment needs a path.";
            return false;
        }

        if (!string.IsNullOrWhiteSpace(kind) && !AttachmentKinds.Contains(kind, StringComparer.Ordinal))
        {
            error = $"Attachment kind '{kind}' must be one of image, file, audio, video.";
            return false;
        }

        attachment = new SendChannelMessageAttachment(
            path.Trim(),
            string.IsNullOrWhiteSpace(kind) ? null : kind,
            string.IsNullOrWhiteSpace(name) ? null : name.Trim());
        error = string.Empty;
        return true;
    }

    private static bool TryReadString(JsonObject value, string property, out string? result)
    {
        result = null;
        if (!value.TryGetPropertyValue(property, out var node) || node == null)
            return true;
        if (node is JsonValue json && json.TryGetValue<string>(out var text))
        {
            result = text;
            return true;
        }

        return false;
    }
}
