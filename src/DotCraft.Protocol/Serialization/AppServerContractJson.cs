using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using DotCraft.Protocol.AppServer;

namespace DotCraft.Protocol;

/// <summary>Canonical JSON settings and source-generated metadata for AppServer contracts.</summary>
public static class AppServerContractJson
{
    /// <summary>Serializer options shared by contract tooling and parity tests.</summary>
    public static JsonSerializerOptions Options { get; } = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            TypeInfoResolver = JsonTypeInfoResolver.Combine(
                AppServerContractJsonContext.Default,
                CoreAppServerContractJsonContext.Default,
                ExtensionAppServerContractJsonContext.Default,
                new DefaultJsonTypeInfoResolver())
        };
        options.Converters.Add(new OptionalJsonConverterFactory());
        return options;
    }
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(RpcEmpty))]
[JsonSerializable(typeof(InitializeParams))]
[JsonSerializable(typeof(InitializeResult))]
[JsonSerializable(typeof(ThreadStartParams))]
[JsonSerializable(typeof(RuntimeDynamicToolDeclaration))]
[JsonSerializable(typeof(RuntimeDynamicToolFunction))]
[JsonSerializable(typeof(RuntimeDynamicToolNamespace))]
[JsonSerializable(typeof(ToolApprovalDescriptor))]
[JsonSerializable(typeof(ThreadStartResult))]
[JsonSerializable(typeof(ThreadResumeParams))]
[JsonSerializable(typeof(ThreadResumeResult))]
[JsonSerializable(typeof(ThreadListParams))]
[JsonSerializable(typeof(ThreadListResult))]
[JsonSerializable(typeof(ThreadReadParams))]
[JsonSerializable(typeof(ThreadReadResult))]
[JsonSerializable(typeof(ThreadTurnsListParams))]
[JsonSerializable(typeof(ThreadTurnsListResult))]
[JsonSerializable(typeof(ThreadItemsListParams))]
[JsonSerializable(typeof(ThreadItemsListResult))]
[JsonSerializable(typeof(TurnStartParams))]
[JsonSerializable(typeof(TurnStartResult))]
[JsonSerializable(typeof(TurnEnqueueParams))]
[JsonSerializable(typeof(TurnEnqueueResult))]
[JsonSerializable(typeof(TurnSteerParams))]
[JsonSerializable(typeof(TurnSteerResult))]
[JsonSerializable(typeof(TurnInterruptParams))]
[JsonSerializable(typeof(ThreadNotification))]
[JsonSerializable(typeof(ThreadDeletedNotification))]
[JsonSerializable(typeof(TurnNotification))]
[JsonSerializable(typeof(ItemNotification))]
[JsonSerializable(typeof(ItemDeltaNotification))]
[JsonSerializable(typeof(UserMessageImage))]
[JsonSerializable(typeof(UserMessagePayload))]
[JsonSerializable(typeof(AgentMessagePayload))]
[JsonSerializable(typeof(SleepPayload))]
[JsonSerializable(typeof(ReasoningContentPayload))]
[JsonSerializable(typeof(CommandExecutionPayload))]
[JsonSerializable(typeof(ToolExecutionPayload))]
[JsonSerializable(typeof(ImageGenerationPayload))]
[JsonSerializable(typeof(ToolSourceProvenancePayload))]
[JsonSerializable(typeof(ToolPresentationPayload))]
[JsonSerializable(typeof(ToolCallPayload))]
[JsonSerializable(typeof(DynamicToolCallPayload))]
[JsonSerializable(typeof(McpToolCallPayload))]
[JsonSerializable(typeof(ToolResultPayload))]
[JsonSerializable(typeof(ApprovalRequestPayload))]
[JsonSerializable(typeof(ApprovalResponsePayload))]
[JsonSerializable(typeof(UserInputRequestPayload))]
[JsonSerializable(typeof(UserInputResponsePayload))]
[JsonSerializable(typeof(ErrorPayload))]
[JsonSerializable(typeof(SystemNoticePayload))]
[JsonSerializable(typeof(ApprovalRequestParams))]
[JsonSerializable(typeof(ApprovalResponseResult))]
[JsonSerializable(typeof(UserInputRequestParams))]
[JsonSerializable(typeof(UserInputResponseResult))]
[JsonSerializable(typeof(DynamicToolCallParams))]
[JsonSerializable(typeof(DynamicToolCallResult))]
[JsonSerializable(typeof(RpcError))]
public sealed partial class AppServerContractJsonContext : JsonSerializerContext;
