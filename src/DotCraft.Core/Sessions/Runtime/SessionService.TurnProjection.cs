using DotCraft.Plugins;
using Microsoft.Extensions.AI;

namespace DotCraft.Sessions;

public sealed partial class SessionService
{
    private static List<UserMessageImage> ExtractUserMessageImages(IList<AIContent> content)
    {
        var images = new List<UserMessageImage>();
        foreach (var part in content.OfType<DataContent>())
        {
            if (part.AdditionalProperties == null)
                continue;
            if (!TryGetStringProperty(part.AdditionalProperties, SessionInputMetadataKeys.LocalImagePath, out var path))
                continue;
            var image = new UserMessageImage
            {
                Path = path,
                MimeType = TryGetStringProperty(
                    part.AdditionalProperties,
                    SessionInputMetadataKeys.LocalImageMimeType,
                    out var mimeType)
                    ? mimeType
                    : null,
                FileName = TryGetStringProperty(
                    part.AdditionalProperties,
                    SessionInputMetadataKeys.LocalImageFileName,
                    out var fileName)
                    ? fileName
                    : null
            };
            images.Add(image);
        }
        return images;
    }

    private static IReadOnlyList<PluginFunctionContentItem>? ExtractToolResultContentItems(object? result)
    {
        if (result is not IEnumerable<AIContent> items)
            return null;

        var contentItems = new List<PluginFunctionContentItem>();
        var hasImage = false;
        foreach (var item in items)
        {
            switch (item)
            {
                case TextContent text when !string.IsNullOrEmpty(text.Text):
                    contentItems.Add(new PluginFunctionContentItem
                    {
                        Type = "text",
                        Text = text.Text
                    });
                    break;
                case DataContent data when IsImageMediaType(data.MediaType):
                    hasImage = true;
                    contentItems.Add(new PluginFunctionContentItem
                    {
                        Type = "image",
                        MediaType = data.MediaType,
                        DataBase64 = Convert.ToBase64String(data.Data.ToArray())
                    });
                    break;
            }
        }

        return hasImage ? contentItems : null;
    }

    private static bool IsImageMediaType(string? mediaType) =>
        mediaType?.StartsWith("image/", StringComparison.OrdinalIgnoreCase) == true;

    private static bool TryGetStringProperty(
        IReadOnlyDictionary<string, object?>? properties,
        string key,
        out string value)
    {
        value = string.Empty;
        if (properties == null)
            return false;
        if (!properties.TryGetValue(key, out var raw) || raw == null)
            return false;
        var text = raw as string ?? raw.ToString();
        if (string.IsNullOrWhiteSpace(text))
            return false;
        value = text.Trim();
        return true;
    }

    private static void RegisterCommandExecutionIfNeeded(
        FunctionCallContent functionCall,
        SessionTurn turn,
        Func<int> nextItemSeq,
        SessionEventChannel eventChannel,
        bool supportsCommandExecutionStreaming,
        string defaultWorkspacePath)
    {
        if (!string.Equals(functionCall.Name, "Exec", StringComparison.Ordinal))
            return;

        if (CommandExecutionRuntimeScope.Current is not { } runtime)
            return;

        var args = functionCall.Arguments;
        var command = args != null && args.TryGetValue("command", out var commandObj)
            ? commandObj?.ToString()
            : null;
        if (string.IsNullOrWhiteSpace(command))
            return;

        var workingDirectory = args != null && args.TryGetValue("workingDir", out var cwdObj)
            ? cwdObj?.ToString()
            : null;
        workingDirectory = !string.IsNullOrWhiteSpace(workingDirectory)
            ? Path.GetFullPath(workingDirectory)
            : defaultWorkspacePath;

        if (!runtime.TryRegisterPendingShellExecution(new PendingShellExecutionRegistration
        {
            CallId = functionCall.CallId,
            Command = command,
            WorkingDirectory = workingDirectory,
            Source = "host"
        }))
        {
            return;
        }

        if (!supportsCommandExecutionStreaming)
            return;

        var item = new SessionItem
        {
            Id = SessionIdGenerator.NewItemId(nextItemSeq()),
            TurnId = turn.Id,
            Type = ItemType.CommandExecution,
            Status = ItemStatus.Started,
            CreatedAt = DateTimeOffset.UtcNow,
            Payload = new CommandExecutionPayload
            {
                CallId = functionCall.CallId,
                Command = command,
                WorkingDirectory = workingDirectory,
                Source = "host",
                Status = "inProgress",
                AggregatedOutput = string.Empty
            }
        };
        if (!runtime.TryRegisterPending(new PendingCommandExecutionRegistration
        {
            CallId = functionCall.CallId,
            Command = command,
            WorkingDirectory = workingDirectory,
            Source = "host",
            Item = item
        }))
        {
            return;
        }
        turn.Items.Add(item);
        eventChannel.EmitItemStarted(item);
    }

    private static void RegisterToolExecutionIfNeeded(
        FunctionCallContent functionCall,
        SessionTurn turn,
        Func<int> nextItemSeq,
        SessionEventChannel eventChannel,
        bool supportsToolExecutionLifecycle)
    {
        if (!supportsToolExecutionLifecycle)
            return;

        if (ToolExecutionRuntimeScope.Current is not { } runtime)
            return;

        if (string.IsNullOrWhiteSpace(functionCall.CallId))
            return;

        var item = new SessionItem
        {
            Id = SessionIdGenerator.NewItemId(nextItemSeq()),
            TurnId = turn.Id,
            Type = ItemType.ToolExecution,
            Status = ItemStatus.Started,
            CreatedAt = DateTimeOffset.UtcNow,
            Payload = new ToolExecutionPayload
            {
                CallId = functionCall.CallId,
                ToolName = functionCall.Name,
                Status = "inProgress"
            }
        };
        turn.Items.Add(item);
        eventChannel.EmitItemStarted(item);
        runtime.RegisterPending(new PendingToolExecutionRegistration
        {
            CallId = functionCall.CallId,
            ToolName = functionCall.Name,
            Item = item
        });
    }

    internal static bool TryRemoveStreamingToolCallIndexByItemReference(
        Dictionary<int, SessionItem>? streamingToolCallItemsByIndex,
        SessionItem targetItem)
    {
        if (streamingToolCallItemsByIndex == null)
            return false;

        int? matchedIndex = null;
        foreach (var kvp in streamingToolCallItemsByIndex)
        {
            if (!ReferenceEquals(kvp.Value, targetItem))
                continue;
            matchedIndex = kvp.Key;
            break;
        }

        return matchedIndex.HasValue && streamingToolCallItemsByIndex.Remove(matchedIndex.Value);
    }
}
