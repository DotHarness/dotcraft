using System.Text.Json.Nodes;
using DotCraft.Abstractions;
using DotCraft.Plugins;
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
    IChannelRuntimeRegistry runtimeRegistry,
    ChannelToolRegistrationService? channelToolRegistration = null) : IManagedAppBindingRuntime
{
    private const string ConversationReceiveScope = "conversation.receive";
    private const string MessageSendScope = "message.send";

    private readonly string _channelName = NormalizeChannelName(channelName);
    private readonly string _displayName = string.IsNullOrWhiteSpace(displayName) ? channelName : displayName.Trim();
    private readonly string _description = string.IsNullOrWhiteSpace(description)
        ? $"Continue this thread in {displayName}."
        : description.Trim();
    private readonly ChannelToolRegistrationService _channelToolRegistration =
        channelToolRegistration ?? new ChannelToolRegistrationService();

    public AppDescriptor Descriptor => BuildDescriptor(_channelName, _displayName, _description);

    public IReadOnlySet<string> CatalogSurfaces { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        AppBindingCatalogSurfaces.ThreadBinding
    };

    public bool RequiresExternalConnection => false;

    public IReadOnlyList<AppBoundToolSpec> ToolSpecs => BuildToolSpecs(_channelName, GetChannelToolDescriptors());

    public bool AllowDirectMutatingToolExposure => true;

    public AppDescriptor GetCatalogDescriptor(string surface) => Descriptor;

    public AppConnectionStatusWire GetConnectionStatus(string appId)
    {
        var state = AppConnectionStates.NotConnected;
        if (runtimeRegistry.TryGet(_channelName, out var runtime) && runtime != null)
        {
            state = runtime.IsReady
                ? AppConnectionStates.Connected
                : AppConnectionStates.Connecting;
        }

        return new AppConnectionStatusWire
        {
            AppId = appId,
            State = state
        };
    }

    public IReadOnlyList<PluginDiagnostic> GetCatalogDiagnostics(string surface)
    {
        GetChannelToolDescriptors(out var diagnostics);
        return diagnostics;
    }

    public IReadOnlyList<AppBoundToolSpec> GetToolSpecsForSurface(string surface) => ToolSpecs;

    public async ValueTask<AppBoundToolCallResult> InvokeToolAsync(
        ManagedAppBindingToolCallContext context,
        JsonObject arguments,
        CancellationToken cancellationToken)
        => await InvokeNativeChannelToolAsync(context, arguments, cancellationToken);

    private async ValueTask<AppBoundToolCallResult> InvokeNativeChannelToolAsync(
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

        return new AppBoundToolCallResult
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
                ..GetChannelToolDescriptors().Select(descriptor => new AppToolCatalogEntry
                {
                    Name = descriptor.Name,
                    Scope = MessageSendScope,
                    Risk = AppBindingRisks.ExternalWrite,
                    DefaultExposure = descriptor.DeferLoading == true
                        ? AppBindingExposures.Deferred
                        : AppBindingExposures.Direct,
                    Description = descriptor.Description
                })
            ],
            DynamicToolCatalog = new AppDynamicToolCatalogDescriptor { Enabled = false }
        };

    private static IReadOnlyList<AppBoundToolSpec> BuildToolSpecs(
        string channelName,
        IReadOnlyList<ChannelToolDescriptor> channelTools) =>
    [
        ..channelTools.Select(tool => new AppBoundToolSpec
        {
            Namespace = channelName,
            Name = tool.Name,
            Description = tool.Description,
            InputSchema = tool.InputSchema?.DeepClone() as JsonObject,
            DeferLoading = tool.DeferLoading,
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

    private IReadOnlyList<ChannelToolDescriptor> GetChannelToolDescriptors() =>
        GetChannelToolDescriptors(out _);

    private IReadOnlyList<ChannelToolDescriptor> GetChannelToolDescriptors(out IReadOnlyList<PluginDiagnostic> diagnostics)
    {
        if (!runtimeRegistry.TryGet(_channelName, out var runtime) || runtime == null || !runtime.IsReady)
        {
            diagnostics = [];
            return [];
        }

        var appId = AppIdForChannel(_channelName);
        var tools = _channelToolRegistration.GetRegisteredTools(runtime, out var channelDiagnostics);
        diagnostics = channelDiagnostics
            .Select(diagnostic => PluginDiagnostic.Warning(
                diagnostic.Code,
                diagnostic.Message,
                appId))
            .ToList();
        return tools;
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

    private static AppBoundToolCallResult Ok(string text, object structured) =>
        new()
        {
            Success = true,
            ContentItems = [new ExtChannelToolContentItem { Type = "text", Text = text }],
            StructuredResult = System.Text.Json.JsonSerializer.SerializeToNode(structured, SessionWireJsonOptions.Default)
        };

    private static AppBoundToolCallResult Fail(string code, string message) =>
        new()
        {
            Success = false,
            ErrorCode = code,
            ErrorMessage = message,
            ContentItems = [new ExtChannelToolContentItem { Type = "text", Text = $"{code}: {message}" }]
        };
}
