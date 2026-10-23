using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Agents;
using DotCraft.Context.Compaction;
using DotCraft.GenerativeUi;
using DotCraft.Plugins;
using DotCraft.Tools;
using Microsoft.Extensions.AI;

namespace DotCraft.Sessions;

public sealed partial class ThreadStore
{
    private static bool TryDeserializeCheckpointHistory(
        ThreadCompactionCheckpoint checkpoint,
        out List<ChatMessage> history)
    {
        history = [];
        try
        {
            var codec = new ModelHistoryCodec();
            var restored = checkpoint.ReplacementHistory.Select(codec.Decode).ToList();
            history = MessageGrouper.NormalizeFunctionCallArguments(restored).ToList();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static List<ChatMessage> BuildModelVisibleHistoryFromTurns(IEnumerable<SessionTurn> turns)
    {
        var history = new List<ChatMessage>();
        foreach (var turn in OrderTurns(turns))
            history.AddRange(BuildModelVisibleHistoryFromTurn(turn));

        return history;
    }

    private static List<SessionTurn> OrderTurns(IEnumerable<SessionTurn> turns) =>
        turns.OrderBy(t => t.StartedAt).ThenBy(t => t.Id, StringComparer.Ordinal).ToList();

    private static bool IsCheckpointCompatibleForFork(
        SessionThread source,
        List<SessionTurn> orderedForkTurns,
        ThreadCompactionCheckpoint checkpoint)
    {
        var sourceTurn = source.Turns.FirstOrDefault(turn =>
            string.Equals(turn.Id, checkpoint.CoveredThroughTurnId, StringComparison.Ordinal));
        if (sourceTurn == null)
            return false;

        var forkTurnIndex = orderedForkTurns.FindIndex(turn =>
            string.Equals(turn.Id, checkpoint.CoveredThroughTurnId, StringComparison.Ordinal));
        if (forkTurnIndex < 0)
            return false;

        var forkTurn = orderedForkTurns[forkTurnIndex];
        var forkItems = forkTurnIndex == orderedForkTurns.Count - 1
            ? RemoveTrailingForkNotice(forkTurn.Items)
            : forkTurn.Items;
        return SameItemPrefix(sourceTurn.Items, forkItems, requireCompleteSourcePrefix: true);
    }

    private static IReadOnlyList<SessionItem> RemoveTrailingForkNotice(IReadOnlyList<SessionItem> items)
    {
        if (items.Count == 0 || !IsForkNotice(items[^1]))
            return items;

        return items.Take(items.Count - 1).ToList();
    }

    private static bool IsForkNotice(SessionItem item) =>
        item.Type == ItemType.SystemNotice &&
        item.Payload is SystemNoticePayload { Kind: "forked" };

    private static bool SameItemPrefix(
        IReadOnlyList<SessionItem> sourceItems,
        IReadOnlyList<SessionItem> forkItems,
        bool requireCompleteSourcePrefix)
    {
        if (requireCompleteSourcePrefix && sourceItems.Count != forkItems.Count)
            return false;

        if (forkItems.Count > sourceItems.Count)
            return false;

        for (var i = 0; i < forkItems.Count; i++)
        {
            if (!string.Equals(sourceItems[i].Id, forkItems[i].Id, StringComparison.Ordinal))
                return false;
        }

        return true;
    }

    internal static IReadOnlyList<ChatMessage> BuildModelVisibleHistoryFromTurn(SessionTurn turn)
    {
        var history = new List<ChatMessage>();
        var completedItems = turn.Items
            .Where(static item => item.Status == ItemStatus.Completed
                                  && item.InvocationOrigin != ToolInvocationOrigin.CodeModeKind)
            .ToList();
        var pairedToolCalls = CollectPairedToolCalls(completedItems);
        var assistantBuilder = new AssistantSamplingSegmentBuilder();

        foreach (var item in completedItems)
        {
            if (item.Type == ItemType.UserMessage && TryBuildUserMessage(item, out var userMessage))
            {
                FlushAssistantSegment(history, assistantBuilder);
                history.Add(userMessage);
            }
            else if (item.Type == ItemType.ReasoningContent &&
                     item.AsReasoningContent is { Text: { } reasoningText } &&
                     !string.IsNullOrWhiteSpace(reasoningText))
            {
                assistantBuilder.AddReasoning(reasoningText);
            }
            else if (item.Type == ItemType.AgentMessage &&
                     item.AsAgentMessage is { Text: { } agentText } agentMessage &&
                     !string.Equals(agentMessage.DeliveryMode, "async", StringComparison.Ordinal) &&
                     !string.IsNullOrWhiteSpace(agentText))
            {
                assistantBuilder.AddText(agentText.Trim());
            }
            else if (item.Type == ItemType.ToolCall &&
                     TryBuildToolCallContent(item, pairedToolCalls, out var toolCallContent))
            {
                assistantBuilder.AddToolCall(toolCallContent);
            }
            else if (item.Type == ItemType.ToolResult &&
                     TryBuildToolResultMessage(item, pairedToolCalls, out var toolResultMessage))
            {
                FlushAssistantSegment(history, assistantBuilder);
                history.Add(toolResultMessage);
            }
            else if (TryBuildSpecializedToolHistory(
                         item,
                         out var specializedCall,
                         out var specializedResult))
            {
                assistantBuilder.AddToolCall(specializedCall);
                FlushAssistantSegment(history, assistantBuilder);
                history.Add(specializedResult);
            }
            else if (item.Type == ItemType.ImageGeneration &&
                     TryBuildImageGenerationToolHistory(item, out var imageGenerationCall, out var imageGenerationResult))
            {
                assistantBuilder.AddToolCall(imageGenerationCall);
                FlushAssistantSegment(history, assistantBuilder);
                history.Add(imageGenerationResult);
            }
            else if (item.Type == ItemType.WebSearch &&
                     TryBuildWebSearchToolHistory(item, out var webSearchCall, out var webSearchResult))
            {
                assistantBuilder.AddToolCall(webSearchCall);
                FlushAssistantSegment(history, assistantBuilder);
                history.Add(webSearchResult);
            }
        }

        FlushAssistantSegment(history, assistantBuilder);
        return history;
    }

    private static bool TryBuildUserMessage(SessionItem item, out ChatMessage message)
    {
        message = new ChatMessage(ChatRole.User, string.Empty);
        if (item.AsUserMessage is not { } user)
            return false;

        if (string.Equals(user.DeliveryMode, SubAgentMailboxDelivery.DeliveryMode, StringComparison.Ordinal))
            return TryBuildSubAgentMailboxMessage(user, out message);

        var parts =
            user.MaterializedInputParts is { Count: > 0 } materialized ? materialized :
            user.NativeInputParts is { Count: > 0 } native ? native :
            null;

        if (parts is { Count: > 0 })
        {
            var contents = SessionInputPartResolver.ResolvePersisted(parts)
                .Where(c => c is not TextContent tc || !string.IsNullOrWhiteSpace(tc.Text))
                .ToList();
            if (contents.Count > 0)
            {
                message = new ChatMessage(ChatRole.User, (IList<AIContent>)contents);
                return true;
            }
        }

        if (string.IsNullOrWhiteSpace(user.Text))
            return false;

        message = new ChatMessage(ChatRole.User, user.Text.Trim());
        return true;
    }

    private static bool TryBuildSubAgentMailboxMessage(UserMessagePayload user, out ChatMessage message)
    {
        message = new ChatMessage(ChatRole.User, string.Empty);
        var parts = user.MaterializedInputParts is { Count: > 0 } materialized ? materialized : null;
        if (parts is { Count: > 0 })
        {
            var contents = SessionInputPartResolver.ResolvePersisted(parts)
                .Where(c => c is not TextContent tc || !string.IsNullOrWhiteSpace(tc.Text))
                .ToList();
            if (contents.Count > 0)
            {
                message = new ChatMessage(ChatRole.User, (IList<AIContent>)contents);
                return true;
            }
        }

        if (string.IsNullOrWhiteSpace(user.Text))
            return false;

        message = new ChatMessage(ChatRole.User, user.Text.Trim());
        return true;
    }

    private static HashSet<string> CollectPairedToolCalls(IReadOnlyList<SessionItem> items)
    {
        var resultIds = items
            .Select(static item => item.Payload as ToolResultPayload)
            .Where(static payload => !string.IsNullOrWhiteSpace(payload?.CallId))
            .Select(static payload => payload!.CallId)
            .ToHashSet(StringComparer.Ordinal);

        if (resultIds.Count == 0)
            return [];

        var paired = new HashSet<string>(StringComparer.Ordinal);
        foreach (var payload in items.Select(static item => item.Payload as ToolCallPayload))
        {
            if (string.IsNullOrWhiteSpace(payload?.CallId) ||
                string.IsNullOrWhiteSpace(payload.ToolName) ||
                !resultIds.Contains(payload.CallId))
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(payload.ProviderFlatName))
                continue;

            paired.Add(payload.CallId);
        }

        return paired;
    }

    private static void FlushAssistantSegment(
        List<ChatMessage> history,
        AssistantSamplingSegmentBuilder builder)
    {
        if (builder.TryBuild(out var message))
            history.Add(message);
        builder.Clear();
    }

    private static bool TryBuildToolCallContent(
        SessionItem item,
        IReadOnlySet<string> pairedToolCalls,
        out FunctionCallContent content)
    {
        content = new FunctionCallContent(string.Empty, string.Empty);
        if (item.Payload is not ToolCallPayload payload ||
            string.IsNullOrWhiteSpace(payload.CallId) ||
            string.IsNullOrWhiteSpace(payload.ToolName) ||
            string.IsNullOrWhiteSpace(payload.ProviderFlatName) ||
            !pairedToolCalls.Contains(payload.CallId))
        {
            return false;
        }

        content = CreatePersistedFunctionCall(
            payload.CallId,
            payload.Namespace,
            payload.ToolName,
            payload.ProviderFlatName,
            payload.Arguments);
        if (item.FreeformCall)
            ProviderFunctionCallMetadata.MarkCustomToolCall(content);
        return true;
    }

    private static bool TryBuildToolResultMessage(
        SessionItem item,
        IReadOnlySet<string> pairedToolCalls,
        out ChatMessage message)
    {
        message = new ChatMessage(ChatRole.Tool, string.Empty);
        if (item.Payload is not ToolResultPayload payload ||
            string.IsNullOrWhiteSpace(payload.CallId) ||
            !pairedToolCalls.Contains(payload.CallId))
        {
            return false;
        }

        var result = BuildModelToolResult(
            payload.ContentItems,
            payload.Result,
            payload.ErrorCode,
            payload.ErrorMessage);
        message = new ChatMessage(
            ChatRole.Tool,
            (IList<AIContent>)[new FunctionResultContent(payload.CallId, result)]);
        return true;
    }

    private static bool TryBuildSpecializedToolHistory(
        SessionItem item,
        out FunctionCallContent call,
        out ChatMessage resultMessage)
    {
        call = new FunctionCallContent(string.Empty, string.Empty);
        resultMessage = new ChatMessage(ChatRole.Tool, string.Empty);

        string callId;
        string? functionNamespace;
        string toolName;
        string providerFlatName;
        JsonObject? arguments;
        IReadOnlyList<PluginFunctionContentItem>? contentItems;
        string? errorCode;
        string? errorMessage;

        switch (item.Payload)
        {
            case McpToolCallPayload mcp
                when !string.IsNullOrWhiteSpace(mcp.CallId)
                     && !string.IsNullOrWhiteSpace(mcp.ToolName)
                     && !string.Equals(mcp.Status, "inProgress", StringComparison.OrdinalIgnoreCase):
                callId = mcp.CallId;
                if (string.IsNullOrWhiteSpace(mcp.ProviderFlatName))
                    return false;
                toolName = mcp.ToolName;
                functionNamespace = mcp.Namespace;
                providerFlatName = mcp.ProviderFlatName;
                arguments = mcp.Arguments;
                contentItems = mcp.ModelContentItems;
                errorCode = mcp.ErrorCode;
                errorMessage = mcp.ErrorMessage;
                break;

            case DynamicToolCallPayload dynamic
                when !string.IsNullOrWhiteSpace(dynamic.CallId)
                     && !string.IsNullOrWhiteSpace(dynamic.ToolName)
                     && !string.Equals(dynamic.Status, "inProgress", StringComparison.OrdinalIgnoreCase):
                callId = dynamic.CallId;
                if (string.IsNullOrWhiteSpace(dynamic.ProviderFlatName))
                    return false;
                toolName = dynamic.ToolName;
                functionNamespace = dynamic.Namespace;
                providerFlatName = dynamic.ProviderFlatName;
                arguments = dynamic.Arguments;
                contentItems = dynamic.ContentItems;
                errorCode = dynamic.ErrorCode;
                errorMessage = dynamic.ErrorMessage;
                break;

            default:
                return false;
        }

        call = CreatePersistedFunctionCall(
            callId,
            functionNamespace,
            toolName,
            providerFlatName,
            arguments);
        var result = BuildModelToolResult(contentItems, null, errorCode, errorMessage);
        resultMessage = new ChatMessage(
            ChatRole.Tool,
            (IList<AIContent>)[new FunctionResultContent(callId, result)]);
        return true;
    }

    private static object BuildModelToolResult(
        IReadOnlyList<PluginFunctionContentItem>? contentItems,
        string? fallback,
        string? errorCode,
        string? errorMessage)
    {
        var contents = new List<AIContent>();
        if (contentItems != null)
        {
            foreach (var item in contentItems)
            {
                if (string.Equals(item.Type, "text", StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrWhiteSpace(item.Text))
                {
                    contents.Add(new TextContent(item.Text));
                }
                else if (string.Equals(item.Type, "image", StringComparison.OrdinalIgnoreCase)
                         && !string.IsNullOrWhiteSpace(item.DataBase64)
                         && !string.IsNullOrWhiteSpace(item.MediaType))
                {
                    try
                    {
                        contents.Add(new DataContent(Convert.FromBase64String(item.DataBase64), item.MediaType));
                    }
                    catch (FormatException)
                    {
                        // Invalid persisted image data is omitted; textual fallback remains usable.
                    }
                }
            }
        }

        if (contents.Count == 1 && contents[0] is TextContent text)
            return text.Text;
        if (contents.Count > 0)
            return contents;
        if (!string.IsNullOrWhiteSpace(fallback))
            return fallback;
        if (!string.IsNullOrWhiteSpace(errorMessage))
            return string.IsNullOrWhiteSpace(errorCode) ? errorMessage : $"{errorCode}: {errorMessage}";

        return "Tool completed without model-visible content.";
    }

    private static FunctionCallContent CreatePersistedFunctionCall(
        string callId,
        string? functionNamespace,
        string localName,
        string providerFlatName,
        JsonObject? arguments)
    {
        var call = new FunctionCallContent(callId, localName, BuildToolArguments(arguments))
        {
            AdditionalProperties = new AdditionalPropertiesDictionary
            {
                ["dotcraft.tool.provider_flat_name"] = providerFlatName
            }
        };
        if (!string.IsNullOrWhiteSpace(functionNamespace))
        {
            call.AdditionalProperties["openai.responses.function_call.namespace"] = functionNamespace;
            call.AdditionalProperties["namespace"] = functionNamespace;
        }

        return call;
    }

    private static bool TryBuildImageGenerationToolHistory(
        SessionItem item,
        out FunctionCallContent call,
        out ChatMessage resultMessage)
    {
        call = new FunctionCallContent(string.Empty, string.Empty);
        resultMessage = new ChatMessage(ChatRole.Tool, string.Empty);
        if (item.AsImageGeneration is not { } payload ||
            string.IsNullOrWhiteSpace(payload.CallId) ||
            string.Equals(payload.Status, "inProgress", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        byte[]? imageBytes = null;
        if (string.Equals(payload.Status, "completed", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(payload.Result))
        {
            try
            {
                imageBytes = Convert.FromBase64String(payload.Result);
            }
            catch (FormatException)
            {
                imageBytes = null;
            }
        }

        call = CreatePersistedFunctionCall(
            payload.CallId,
            ImageGenerationToolSource.ToolNamespace,
            ImageGenerationToolSource.ToolName,
            ImageGenerationToolSource.ToolNamespace + "__" + ImageGenerationToolSource.ToolName,
            string.IsNullOrWhiteSpace(payload.RevisedPrompt)
                ? null
                : new JsonObject { ["prompt"] = payload.RevisedPrompt });

        object result;
        if (imageBytes is { Length: > 0 })
        {
            var contents = new List<AIContent>
            {
                new DataContent(imageBytes, string.IsNullOrWhiteSpace(payload.MediaType) ? "image/png" : payload.MediaType)
            };
            if (string.Equals(payload.SaveStatus, "saved", StringComparison.Ordinal)
                && !string.IsNullOrWhiteSpace(payload.SavedPath)
                && ImageGenerationToolRuntime.CreateOutputHint(payload.SavedPath) is { } hint)
            {
                contents.Add(new TextContent(hint));
            }

            result = contents;
        }
        else
        {
            result = $"image generation failed: {payload.ErrorMessage ?? "Image generation returned no image data."}";
        }

        resultMessage = new ChatMessage(
            ChatRole.Tool,
            (IList<AIContent>)[new FunctionResultContent(payload.CallId, result)]);
        return true;
    }

    private static IDictionary<string, object?>? BuildToolArguments(JsonObject? arguments)
    {
        if (arguments is null || arguments.Count == 0)
            return new Dictionary<string, object?>();

        return JsonSerializer.Deserialize<Dictionary<string, object?>>(
            arguments.ToJsonString(),
            SessionPersistenceJsonOptions.Default);
    }

    private sealed class AssistantSamplingSegmentBuilder
    {
        private readonly List<AIContent> _reasoning = [];
        private readonly List<AIContent> _visible = [];
        private readonly List<AIContent> _toolCalls = [];

        public void AddReasoning(string text) => _reasoning.Add(new TextReasoningContent(text));

        public void AddText(string text) => _visible.Add(new TextContent(text));

        public void AddToolCall(FunctionCallContent content) => _toolCalls.Add(content);

        public bool TryBuild(out ChatMessage message)
        {
            message = new ChatMessage(ChatRole.Assistant, string.Empty);
            if (_visible.Count == 0 && _toolCalls.Count == 0)
                return false;

            var contents = new List<AIContent>();
            if (_toolCalls.Count > 0)
                contents.AddRange(_reasoning);
            contents.AddRange(_visible);
            contents.AddRange(_toolCalls);

            message = new ChatMessage(ChatRole.Assistant, contents);
            return true;
        }

        public void Clear()
        {
            _reasoning.Clear();
            _visible.Clear();
            _toolCalls.Clear();
        }
    }
}
