using DotCraft.Channels;
using DotCraft.Security;

namespace DotCraft.ExternalChannel.Messaging;

internal static class SendChannelMessageErrorCodes
{
    public const string InvalidArguments = "invalidArguments";
    public const string UnsupportedTarget = "unsupportedTarget";
    public const string UnsupportedKind = "unsupportedKind";
    public const string TooLarge = "tooLarge";
    public const string ChannelUnavailable = "channelUnavailable";
    public const string DeliveryFailed = "deliveryFailed";
    public const string Declined = "declined";
}

internal sealed record SendChannelMessageError(string Code, string Message);

internal sealed record PlannedChannelPart(
    string Kind,
    string? Text = null,
    string? Caption = null,
    string? FullPath = null,
    string? FileName = null,
    string? MediaType = null,
    string? DowngradedFrom = null);

internal static class SendChannelMessagePlanner
{
    private static readonly Dictionary<string, string> MediaTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".png"] = "image/png",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".gif"] = "image/gif",
        [".webp"] = "image/webp",
        [".bmp"] = "image/bmp",
        [".mp3"] = "audio/mpeg",
        [".ogg"] = "audio/ogg",
        [".opus"] = "audio/opus",
        [".wav"] = "audio/wav",
        [".m4a"] = "audio/mp4",
        [".amr"] = "audio/amr",
        [".mp4"] = "video/mp4",
        [".mov"] = "video/quicktime",
        [".webm"] = "video/webm",
        [".pdf"] = "application/pdf",
        [".txt"] = "text/plain",
        [".md"] = "text/markdown",
        [".csv"] = "text/csv",
        [".json"] = "application/json",
        [".zip"] = "application/zip"
    };

    public static async Task<(IReadOnlyList<PlannedChannelPart> Parts, SendChannelMessageError? Error)> PlanAsync(
        SendChannelMessageRequest request,
        ChannelDeliveryCapabilitySnapshot capabilities,
        FileAccessGuard fileAccess,
        bool hasRemoteRoute,
        CancellationToken cancellationToken)
    {
        var attachments = new List<PlannedChannelPart>();
        foreach (var attachment in request.Attachments)
        {
            var (part, error) = await PlanAttachmentAsync(attachment, capabilities, fileAccess, hasRemoteRoute, cancellationToken);
            if (error != null)
                return ([], error);
            attachments.Add(part!);
        }

        var parts = new List<PlannedChannelPart>();
        if (request.Text != null)
        {
            if (attachments.Count == 1 && SupportsCaption(capabilities, attachments[0].Kind))
                attachments[0] = attachments[0] with { Caption = request.Text };
            else
                parts.Add(new PlannedChannelPart("text", Text: request.Text));
        }

        parts.AddRange(attachments);
        return (parts, null);
    }

    private static async Task<(PlannedChannelPart? Part, SendChannelMessageError? Error)> PlanAttachmentAsync(
        SendChannelMessageAttachment attachment,
        ChannelDeliveryCapabilitySnapshot capabilities,
        FileAccessGuard fileAccess,
        bool hasRemoteRoute,
        CancellationToken cancellationToken)
    {
        var fullPath = fileAccess.ResolvePath(attachment.Path);
        var denied = await fileAccess.ValidatePathAsync(fullPath, "read", attachment.Path, cancellationToken);
        if (denied != null)
            return (null, new SendChannelMessageError(SendChannelMessageErrorCodes.InvalidArguments, denied));
        if (!File.Exists(fullPath))
        {
            return (null, new SendChannelMessageError(
                SendChannelMessageErrorCodes.InvalidArguments,
                hasRemoteRoute
                    ? $"Attachment '{attachment.Path}' was not found on this host. Attachments must be local files; download remote files to the local workspace with the Transfer tool first."
                    : $"Attachment '{attachment.Path}' was not found."));
        }

        var fileName = attachment.Name ?? Path.GetFileName(fullPath);
        var mediaType = MediaTypes.GetValueOrDefault(Path.GetExtension(fullPath));
        var requestedKind = attachment.Kind
            ?? (mediaType?.StartsWith("image/", StringComparison.Ordinal) == true ? "image" : "file");
        var kind = requestedKind;
        string? downgradedFrom = null;
        var constraints = GetConstraints(capabilities, kind);
        var fileConstraints = capabilities.Media?.File;
        if (!Accepts(constraints, fileName, mediaType) && Accepts(fileConstraints, fileName, mediaType))
        {
            if (kind is "audio" or "video")
            {
                downgradedFrom = kind;
                (kind, constraints) = ("file", fileConstraints);
            }
            else if (attachment.Kind == null && kind == "image")
            {
                (kind, constraints) = ("file", fileConstraints);
            }
        }

        if (constraints == null)
        {
            return (null, new SendChannelMessageError(
                SendChannelMessageErrorCodes.UnsupportedKind,
                $"This chat does not accept {requestedKind} attachments."));
        }

        long length;
        try
        {
            length = new FileInfo(fullPath).Length;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return (null, new SendChannelMessageError(
                SendChannelMessageErrorCodes.InvalidArguments,
                $"Attachment '{attachment.Path}' is not readable: {ex.Message}"));
        }

        if (constraints.MaxBytes is > 0 && length > constraints.MaxBytes.Value)
        {
            return (null, new SendChannelMessageError(
                SendChannelMessageErrorCodes.TooLarge,
                $"Attachment '{fileName}' is {length} bytes; this chat accepts {kind} attachments up to {constraints.MaxBytes.Value} bytes."));
        }

        if (!Accepts(constraints, fileName, mediaType))
        {
            return (null, new SendChannelMessageError(
                SendChannelMessageErrorCodes.UnsupportedKind,
                $"This chat does not accept '{fileName}' as a {kind} attachment."));
        }

        return (new PlannedChannelPart(
            kind,
            FullPath: fullPath,
            FileName: fileName,
            MediaType: mediaType,
            DowngradedFrom: downgradedFrom), null);
    }

    private static bool Accepts(ChannelMediaConstraintSnapshot? constraints, string fileName, string? mediaType)
    {
        if (constraints == null || constraints.SupportsHostPath != true && constraints.SupportsBase64 != true)
            return false;

        if (constraints.AllowedMimeTypes is { Count: > 0 } mimeTypes
            && (mediaType == null || !mimeTypes.Contains(mediaType, StringComparer.OrdinalIgnoreCase)))
        {
            return false;
        }

        var extension = Path.GetExtension(fileName);
        return constraints.AllowedExtensions is not { Count: > 0 } extensions
               || string.IsNullOrEmpty(extension)
               || extensions.Contains(extension, StringComparer.OrdinalIgnoreCase);
    }

    private static bool SupportsCaption(ChannelDeliveryCapabilitySnapshot capabilities, string kind) =>
        GetConstraints(capabilities, kind)?.SupportsCaption == true;

    private static ChannelMediaConstraintSnapshot? GetConstraints(ChannelDeliveryCapabilitySnapshot capabilities, string kind) =>
        kind switch
        {
            "image" => capabilities.Media?.Image,
            "file" => capabilities.Media?.File,
            "audio" => capabilities.Media?.Audio,
            "video" => capabilities.Media?.Video,
            _ => null
        };
}
