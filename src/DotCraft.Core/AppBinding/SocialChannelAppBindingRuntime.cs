using System.Text.Json.Nodes;
using DotCraft.Abstractions;
using DotCraft.Protocol;
using DotCraft.Protocol.AppServer;

namespace DotCraft.AppBinding;

/// <summary>
/// First-party managed App Binding runtime that projects a channel adapter as a thread-bound app.
/// </summary>
public sealed class SocialChannelAppBindingRuntime(
    string channelName,
    string displayName,
    string description,
    IChannelRuntimeRegistry runtimeRegistry) : IManagedAppBindingRuntime
{
    private const string ConversationReceiveScope = "conversation.receive";
    private const string MessageSendScope = "message.send";
    private const string SendMessageToolName = "SendMessageToBoundConversation";

    private readonly string _channelName = NormalizeChannelName(channelName);
    private readonly string _displayName = string.IsNullOrWhiteSpace(displayName) ? channelName : displayName.Trim();
    private readonly string _description = string.IsNullOrWhiteSpace(description)
        ? $"Continue this thread in {displayName}."
        : description.Trim();

    public AppDescriptor Descriptor => BuildDescriptor(_channelName, _displayName, _description);

    public IReadOnlySet<string> CatalogSurfaces { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        AppBindingCatalogSurfaces.ThreadBinding
    };

    public bool RequiresExternalConnection => false;

    public IReadOnlyList<DynamicToolSpec> ToolSpecs => BuildToolSpecs(_channelName, GetChannelToolDescriptors());

    public bool AllowDirectMutatingToolExposure => false;

    public AppDescriptor GetCatalogDescriptor(string surface) => Descriptor;

    public AppConnectionStatusWire GetConnectionStatus(string appId) =>
        new()
        {
            AppId = appId,
            State = runtimeRegistry.TryGet(_channelName, out var runtime) && runtime is { IsReady: true }
                ? AppConnectionStates.Connected
                : AppConnectionStates.NotConnected
        };

    public IReadOnlyList<DynamicToolSpec> GetToolSpecsForSurface(string surface) => ToolSpecs;

    public async ValueTask<DynamicToolCallResult> InvokeToolAsync(
        ManagedAppBindingToolCallContext context,
        JsonObject arguments,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(context.ToolName, SendMessageToolName, StringComparison.Ordinal))
            return await InvokeNativeChannelToolAsync(context, arguments, cancellationToken);

        var text = arguments.TryGetPropertyValue("text", out var textNode)
            ? textNode?.GetValue<string>()?.Trim()
            : null;
        if (string.IsNullOrWhiteSpace(text))
            return Fail("InvalidArguments", "'text' is required.");

        var target = context.AppBindingService?.GetSocialTarget(context.WorkspaceCraftPath, context.BindingId);
        if (target == null)
            return Fail(AppBindingErrorCodes.ToolUnavailable, "The binding does not have a social channel target.");
        if (!string.Equals(target.ChannelName, _channelName, StringComparison.OrdinalIgnoreCase))
            return Fail(AppBindingErrorCodes.ProtocolViolation, "The binding target channel does not match this runtime.");
        if (!runtimeRegistry.TryGet(_channelName, out var runtime) || runtime == null || !runtime.IsReady)
            return Fail(AppBindingErrorCodes.Offline, $"Channel '{_channelName}' is not connected.");

        var delivery = await runtime.DeliverAsync(
            target.DeliveryTarget,
            new ChannelOutboundMessage
            {
                Kind = "text",
                Text = text
            },
            metadata: new
            {
                context.ThreadId,
                context.TurnId,
                context.BindingId,
                context.AppId
            },
            cancellationToken);

        return delivery.Delivered
            ? Ok($"Message sent to {_displayName}.", new { target = target.DeliveryTarget })
            : Fail(delivery.ErrorCode ?? "ChannelDeliveryFailed", delivery.ErrorMessage ?? "Channel delivery failed.");
    }

    private async ValueTask<DynamicToolCallResult> InvokeNativeChannelToolAsync(
        ManagedAppBindingToolCallContext context,
        JsonObject arguments,
        CancellationToken cancellationToken)
    {
        var target = context.AppBindingService?.GetSocialTarget(context.WorkspaceCraftPath, context.BindingId);
        if (target == null)
            return Fail(AppBindingErrorCodes.ToolUnavailable, "The binding does not have a social channel target.");
        if (!string.Equals(target.ChannelName, _channelName, StringComparison.OrdinalIgnoreCase))
            return Fail(AppBindingErrorCodes.ProtocolViolation, "The binding target channel does not match this runtime.");
        if (TryFindTargetOverride(arguments, out var overrideName))
            return Fail(AppBindingErrorCodes.ProtocolViolation, $"Argument '{overrideName}' cannot override the bound social target.");
        if (!runtimeRegistry.TryGet(_channelName, out var runtime) || runtime == null || !runtime.IsReady)
            return Fail(AppBindingErrorCodes.Offline, $"Channel '{_channelName}' is not connected.");
        if (!GetChannelToolDescriptors().Any(tool => string.Equals(tool.Name, context.ToolName, StringComparison.Ordinal)))
            return Fail(AppBindingErrorCodes.ToolUnavailable, $"Channel tool '{context.ToolName}' is not supported.");

        var result = await runtime.ExecuteToolAsync(
            new ExtChannelToolCallParams
            {
                ThreadId = context.ThreadId,
                TurnId = context.TurnId,
                CallId = context.CallId,
                Tool = context.ToolName,
                Arguments = arguments,
                Context = new ExtChannelToolCallContext
                {
                    ChannelName = _channelName,
                    ChannelContext = target.DeliveryTarget,
                    SenderId = target.BoundBy?.PlatformUserId,
                    GroupId = string.Equals(target.ConversationKind, "user", StringComparison.OrdinalIgnoreCase)
                        ? null
                        : target.DeliveryTarget
                }
            },
            cancellationToken);

        return new DynamicToolCallResult
        {
            Success = result.Success,
            ContentItems = result.ContentItems,
            StructuredResult = result.StructuredResult,
            ErrorCode = result.ErrorCode,
            ErrorMessage = result.ErrorMessage
        };
    }

    private AppDescriptor BuildDescriptor(string channelName, string displayName, string description) =>
        new()
        {
            AppId = AppIdForChannel(channelName),
            ToolNamespace = channelName,
            DisplayName = displayName,
            DeveloperName = "DotHarness",
            Description = description,
            Category = "Social Channels",
            OriginChannel = channelName,
            Connection = new AppConnectionDescriptor
            {
                HandoffModes =
                [
                    new AppHandoffModeDescriptor { Mode = "bindCode" }
                ]
            },
            NativeApplication = new AppNativeApplicationDescriptor
            {
                DisplayName = displayName,
                Protocol = string.Empty
            },
            Scopes =
            [
                Scope(ConversationReceiveScope, "Receive messages", $"Allow messages from the selected {displayName} conversation to continue this thread.", AppBindingRisks.Read),
                Scope(MessageSendScope, "Send replies", $"Allow DotCraft to send replies to the selected {displayName} conversation.", AppBindingRisks.ExternalWrite)
            ],
            ToolCatalog =
            [
                new AppToolCatalogEntry
                {
                    Name = SendMessageToolName,
                    Scope = MessageSendScope,
                    Risk = AppBindingRisks.ExternalWrite,
                    DefaultExposure = AppBindingExposures.Deferred,
                    Description = $"Send a message to the {displayName} conversation bound to this thread."
                },
                ..GetChannelToolDescriptors().Select(descriptor => new AppToolCatalogEntry
                {
                    Name = descriptor.Name,
                    Scope = MessageSendScope,
                    Risk = AppBindingRisks.ExternalWrite,
                    DefaultExposure = descriptor.DeferLoading == false
                        ? AppBindingExposures.Direct
                        : AppBindingExposures.Deferred,
                    Description = descriptor.Description
                })
            ],
            DynamicToolCatalog = new AppDynamicToolCatalogDescriptor { Enabled = false }
        };

    private static IReadOnlyList<DynamicToolSpec> BuildToolSpecs(
        string channelName,
        IReadOnlyList<ChannelToolDescriptor> channelTools) =>
    [
        new DynamicToolSpec
        {
            Namespace = channelName,
            Name = SendMessageToolName,
            Description = "Send a message to the social conversation bound to this thread.",
            InputSchema = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["text"] = new JsonObject
                    {
                        ["type"] = "string",
                        ["description"] = "Message text to send."
                    }
                },
                ["required"] = new JsonArray("text")
            },
            DeferLoading = true
        },
        ..channelTools.Select(tool => new DynamicToolSpec
        {
            Namespace = channelName,
            Name = tool.Name,
            Description = tool.Description,
            InputSchema = tool.InputSchema?.DeepClone() as JsonObject,
            DeferLoading = tool.DeferLoading ?? true,
            Approval = tool.Approval == null
                ? null
                : new ChannelToolApprovalDescriptor
                {
                    Kind = tool.Approval.Kind,
                    TargetArgument = tool.Approval.TargetArgument,
                    Operation = tool.Approval.Operation,
                    OperationArgument = tool.Approval.OperationArgument
                }
        })
    ];

    private IReadOnlyList<ChannelToolDescriptor> GetChannelToolDescriptors()
    {
        if (!runtimeRegistry.TryGet(_channelName, out var runtime) || runtime == null || !runtime.IsReady)
            return [];

        return runtime.GetChannelTools()
            .Where(IsUsableChannelTool)
            .GroupBy(tool => tool.Name, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToList();
    }

    private static bool IsUsableChannelTool(ChannelToolDescriptor descriptor)
    {
        if (string.Equals(descriptor.Name, SendMessageToolName, StringComparison.Ordinal))
            return false;
        var spec = new DynamicToolSpec
        {
            Namespace = "channel",
            Name = descriptor.Name,
            Description = descriptor.Description,
            InputSchema = descriptor.InputSchema?.DeepClone() as JsonObject,
            DeferLoading = descriptor.DeferLoading,
            Approval = descriptor.Approval
        };
        return WireDynamicToolProxy.TryValidateSpecs([spec], out _);
    }

    private static bool TryFindTargetOverride(JsonObject arguments, out string argumentName)
    {
        foreach (var (name, _) in arguments)
        {
            if (TargetOverrideArgumentNames.Contains(name))
            {
                argumentName = name;
                return true;
            }
        }

        argumentName = string.Empty;
        return false;
    }

    private static readonly HashSet<string> TargetOverrideArgumentNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "target",
        "deliveryTarget",
        "channelContext",
        "chatId",
        "chat_id",
        "groupId",
        "group_id",
        "conversationId",
        "conversation_id",
        "conversationKind",
        "conversation_kind",
        "toUserId",
        "to_user_id"
    };

    public static string AppIdForChannel(string channelName) =>
        $"com.dotharness.channel.{NormalizeChannelName(channelName)}";

    private static string NormalizeChannelName(string channelName) =>
        channelName.Trim().ToLowerInvariant();

    private static AppScopeDescriptor Scope(string id, string displayName, string description, string risk) =>
        new()
        {
            Id = id,
            DisplayName = displayName,
            Description = description,
            Risk = risk,
            DefaultSelected = true
        };

    private static DynamicToolCallResult Ok(string text, object structured) =>
        new()
        {
            Success = true,
            ContentItems = [new ExtChannelToolContentItem { Type = "text", Text = text }],
            StructuredResult = System.Text.Json.JsonSerializer.SerializeToNode(structured, SessionWireJsonOptions.Default)
        };

    private static DynamicToolCallResult Fail(string code, string message) =>
        new()
        {
            Success = false,
            ErrorCode = code,
            ErrorMessage = message,
            ContentItems = [new ExtChannelToolContentItem { Type = "text", Text = $"{code}: {message}" }]
        };
}
