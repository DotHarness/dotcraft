using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Channels;
using DotCraft.Security;
using DotCraft.Tools;

namespace DotCraft.ExternalChannel.Messaging;

internal sealed class SendChannelMessageRuntime(
    ChatBindingResolver resolver,
    string threadId,
    string? originChannel,
    string? channelContext,
    string workspacePath,
    PathBlacklist? pathBlacklist) : IToolRuntime
{
    internal const string ApprovalType = "channelMessage";
    internal const string ApprovalOperation = "send";

    public async ValueTask<ToolExecutionResult> InvokeAsync(
        ToolInvocationContext context,
        JsonObject arguments,
        CancellationToken cancellationToken = default)
    {
        if (!SendChannelMessageRequest.TryParse(arguments, out var request, out var parseError))
            return Fail(SendChannelMessageErrorCodes.InvalidArguments, parseError, null, null, []);

        var bound = resolver.Resolve(threadId, originChannel, channelContext);
        if (bound == null)
        {
            return Fail(SendChannelMessageErrorCodes.ChannelUnavailable,
                "This conversation is no longer connected to a chat.", null, request.Text, []);
        }

        if (!bound.CanDeliver)
        {
            return Fail(SendChannelMessageErrorCodes.ChannelUnavailable,
                $"The connection to {bound.Address} is not active.", null, request.Text, []);
        }

        var (destination, targetError) = ResolveDestination(request, bound);
        if (targetError != null)
            return Fail(targetError.Code, targetError.Message, destination, request.Text, []);

        var hostScope = ToolHostExecutionScope.Current;
        var approvalService = hostScope?.ApprovalService;
        var fileAccess = new FileAccessGuard(
            context.WorkspacePath ?? hostScope?.WorkspacePath ?? workspacePath,
            requireApprovalOutsideWorkspace: true,
            approvalService,
            pathBlacklist);
        var (parts, planError) = await SendChannelMessagePlanner.PlanAsync(
            request,
            destination!.Capabilities,
            fileAccess,
            cancellationToken);
        if (planError != null)
            return Fail(planError.Code, planError.Message, destination, request.Text, []);

        if (!destination.IsBound)
        {
            var approved = approvalService != null && await approvalService.RequestResourceApprovalAsync(
                new ResourceApprovalRequest(ApprovalType, ApprovalOperation, destination.Address)
                {
                    TargetLabel = $"a message to {destination.Runtime.DisplayName} {destination.Target}"
                },
                ApprovalContextScope.Current);
            if (!approved)
            {
                return Fail(SendChannelMessageErrorCodes.Declined,
                    $"Sending to {destination.Address} was not approved.", destination, request.Text, []);
            }
        }

        var delivered = new List<JsonObject>();
        for (var index = 0; index < parts.Count; index++)
        {
            var part = parts[index];
            var result = await destination.Runtime.DeliverAsync(
                destination.Target,
                ToMessage(part),
                new { ThreadId = threadId, context.TurnId, context.CallId, Origin = SendChannelMessageToolSource.ToolName },
                DeliveryId(context.TurnId, context.CallId, index),
                cancellationToken);
            if (!result.Delivered)
            {
                return Fail(
                    MapDeliveryError(result.ErrorCode),
                    result.ErrorMessage ?? $"The chat did not accept the {part.Kind} part.",
                    destination,
                    request.Text,
                    delivered);
            }

            delivered.Add(DescribePart(part, result.RemoteMessageId));
        }

        var content = new JsonObject
        {
            ["status"] = "sent",
            ["to"] = destination.Address,
            ["parts"] = new JsonArray(delivered.Select(part => (JsonNode)part.DeepClone()).ToArray())
        };
        return ToolExecutionResult.Succeeded(
            content.ToJsonString(),
            Structured(destination, request.Text, delivered, null));
    }

    internal static string DeliveryId(string? turnId, string callId, int partIndex)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{turnId}\n{callId}\n{partIndex}"));
        return Convert.ToHexString(hash, 0, 16).ToLowerInvariant();
    }

    private (Destination? Destination, SendChannelMessageError? Error) ResolveDestination(
        SendChannelMessageRequest request,
        ChatBinding bound)
    {
        var channelName = bound.ChannelName;
        var target = bound.Target;
        if (request.To != null && !ChatAddress.TryParse(request.To, out channelName, out target))
        {
            return (null, new SendChannelMessageError(SendChannelMessageErrorCodes.InvalidArguments,
                $"'{request.To}' is not a chat address. Addresses look like {bound.Address}."));
        }

        if (!resolver.Registry.TryGet(channelName, out var runtime) || runtime == null)
        {
            return (null, new SendChannelMessageError(SendChannelMessageErrorCodes.ChannelUnavailable,
                $"Channel '{channelName}' is not connected."));
        }

        var isBound = string.Equals(runtime.Name, bound.ChannelName, StringComparison.OrdinalIgnoreCase)
            && string.Equals(target, bound.Target, StringComparison.Ordinal);
        var capabilities = runtime.IsReady ? runtime.GetDeliveryCapabilities() : null;
        var destination = new Destination(runtime, target, isBound, isBound ? bound.Chat : null, capabilities!);
        if (capabilities == null)
        {
            return (destination, new SendChannelMessageError(SendChannelMessageErrorCodes.ChannelUnavailable,
                $"Channel '{runtime.Name}' is not connected."));
        }

        if (!isBound && !capabilities.Targets.Group
            && string.Equals(capabilities.Targets.Direct, ChannelDirectReach.None, StringComparison.Ordinal))
        {
            return (destination, new SendChannelMessageError(SendChannelMessageErrorCodes.UnsupportedTarget,
                $"{runtime.DisplayName} can only send to the chat this conversation is connected to."));
        }

        return (destination, null);
    }

    private static ChannelDeliveryMessage ToMessage(PlannedChannelPart part) =>
        part.Kind == "text"
            ? new ChannelDeliveryMessage { Kind = "text", Text = part.Text }
            : new ChannelDeliveryMessage
            {
                Kind = part.Kind,
                Caption = part.Caption,
                FileName = part.FileName,
                MediaType = part.MediaType,
                Source = new ChannelDeliveryMediaSource { Kind = "hostPath", HostPath = part.FullPath }
            };

    private static JsonObject DescribePart(PlannedChannelPart part, string? messageId)
    {
        var value = new JsonObject { ["kind"] = part.Kind };
        if (part.FileName != null)
            value["name"] = part.FileName;
        if (part.Caption != null)
            value["captioned"] = true;
        if (part.DowngradedFrom != null)
            value["downgradedFrom"] = part.DowngradedFrom;
        if (!string.IsNullOrWhiteSpace(messageId))
            value["messageId"] = messageId;
        return value;
    }

    private static string MapDeliveryError(string? adapterCode) => adapterCode switch
    {
        "UnsupportedTarget" => SendChannelMessageErrorCodes.UnsupportedTarget,
        "MediaTooLarge" => SendChannelMessageErrorCodes.TooLarge,
        "UnsupportedDeliveryKind" or "MediaTypeNotAllowed" or "UnsupportedMediaSource"
            => SendChannelMessageErrorCodes.UnsupportedKind,
        "MediaArtifactNotFound" => SendChannelMessageErrorCodes.InvalidArguments,
        _ => SendChannelMessageErrorCodes.DeliveryFailed
    };

    private static ToolExecutionResult Fail(
        string code,
        string message,
        Destination? destination,
        string? text,
        IReadOnlyList<JsonObject> delivered)
    {
        var content = delivered.Count == 0
            ? $"{code}: {message}"
            : $"{code}: {message} Delivered before the failure: {new JsonArray(delivered.Select(part => (JsonNode)part.DeepClone()).ToArray()).ToJsonString()}";
        return new ToolExecutionResult(
            false,
            content,
            destination == null ? null : Structured(destination, text, delivered, new ToolError(code, message)),
            error: new ToolError(code, message));
    }

    private static JsonElement Structured(
        Destination destination,
        string? text,
        IReadOnlyList<JsonObject> parts,
        ToolError? error)
    {
        var value = new JsonObject
        {
            ["channelName"] = destination.Runtime.Name,
            ["displayName"] = destination.Runtime.DisplayName,
            ["address"] = destination.Address,
            ["parts"] = new JsonArray(parts.Select(part => (JsonNode)part.DeepClone()).ToArray())
        };
        if (destination.Chat?.Label is { } label)
            value["label"] = label;
        if (text != null)
            value["text"] = text;
        if (error != null)
            value["error"] = new JsonObject { ["code"] = error.Code, ["message"] = error.Message };
        return JsonSerializer.SerializeToElement(value);
    }

    private sealed record Destination(
        IChannelRuntime Runtime,
        string Target,
        bool IsBound,
        ChannelChatDescriptor? Chat,
        ChannelDeliveryCapabilitySnapshot Capabilities)
    {
        public string Address => ChatAddress.Format(Runtime.Name, Target);
    }
}
