using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Agents;
using DotCraft.Logging;
using DotCraft.Tools;
using Microsoft.Extensions.AI;

namespace DotCraft.Sessions;

public sealed partial class SessionService
{
    private sealed class TurnItemProjector(
        string threadId,
        SessionTurn turn,
        SessionEventChannel channel,
        Func<int> nextItemSequence,
        SessionStreamDebugLogger? debugLogger,
        TurnExecutionState turnRuntime)
    {
        private SessionItem? _agentMessage;
        private SessionItem? _reasoning;
        private string _agentText = string.Empty;
        private string _reasoningText = string.Empty;
        private string? _reasoningGroup;
        private int _agentDeltaIndex;
        private Dictionary<int, string>? _previewNamesByIndex;
        private Dictionary<int, SessionItem>? _previewItemsByIndex;
        private Dictionary<string, SessionItem>? _previewItemsByCallId;

        public string AgentText => _agentText;

        public void AppendAgentText(string? text)
        {
            FinalizeReasoning();
            var chunk = text ?? string.Empty;
            if (_agentMessage == null)
            {
                _agentMessage = new SessionItem
                {
                    Id = SessionIdGenerator.NewItemId(nextItemSequence()),
                    TurnId = turn.Id,
                    Type = ItemType.AgentMessage,
                    Status = ItemStatus.Streaming,
                    CreatedAt = DateTimeOffset.UtcNow,
                    Payload = new AgentMessagePayload { Text = string.Empty }
                };
                turn.Items.Add(_agentMessage);
                channel.EmitItemStarted(_agentMessage);
            }

            _agentText += chunk;
            _agentDeltaIndex += 1;
            if (debugLogger?.ShouldCapture(threadId, turn.Id) == true)
            {
                debugLogger.Log(
                    "agent_delta_source",
                    threadId,
                    turn.Id,
                    new
                    {
                        itemId = _agentMessage.Id,
                        deltaIndex = _agentDeltaIndex,
                        chunkChars = chunk.Length,
                        chunkText = debugLogger.IncludeFullText ? chunk : null,
                        cumulativeChars = _agentText.Length,
                        cumulativeText = debugLogger.IncludeFullText ? _agentText : null
                    });
            }

            channel.EmitItemDelta(_agentMessage, new AgentMessageDelta { TextDelta = chunk });
        }

        public void AppendReasoning(string text, string? group = null)
        {
            FinalizeAgentMessage();
            if (_reasoning == null)
            {
                _reasoning = new SessionItem
                {
                    Id = SessionIdGenerator.NewItemId(nextItemSequence()),
                    TurnId = turn.Id,
                    Type = ItemType.ReasoningContent,
                    Status = ItemStatus.Streaming,
                    CreatedAt = DateTimeOffset.UtcNow,
                    Payload = new ReasoningContentPayload { Text = string.Empty }
                };
                turn.Items.Add(_reasoning);
                channel.EmitItemStarted(_reasoning);
            }

            // Provider reasoning items stream into one item, so their texts are separated like parts within one.
            if (group != null && _reasoningGroup != null && group != _reasoningGroup && _reasoningText.Length > 0)
                text = "\n\n" + text;
            _reasoningGroup = group ?? _reasoningGroup;
            _reasoningText += text;
            channel.EmitItemDelta(_reasoning, new ReasoningContentDelta { TextDelta = text });
        }

        public void AppendToolArguments(ToolCallArgumentsDeltaContent toolArgsDelta)
        {
            if (string.IsNullOrEmpty(toolArgsDelta.ArgumentsDelta))
                return;

            var toolCallIndex = toolArgsDelta.ToolCallIndex;
            if (!string.IsNullOrWhiteSpace(toolArgsDelta.ToolName))
            {
                _previewNamesByIndex ??= [];
                _previewNamesByIndex[toolCallIndex] = toolArgsDelta.ToolName;
            }

            if (_previewNamesByIndex?.GetValueOrDefault(toolCallIndex) is not { } resolvedToolName)
                return;

            var snapshot = turnRuntime.ToolSnapshot;
            var canonicalName = default(ToolName);
            ToolRegistration? registration = null;
            if (snapshot?.TryResolveProviderFlatName(resolvedToolName, out canonicalName) == true)
                snapshot.Registrations.TryGetValue(canonicalName, out registration);

            var awaitingQualifiedName = registration is null && snapshot?.Registrations.Keys.Any(
                name => string.Equals(name.Name, resolvedToolName, StringComparison.Ordinal)) == true;
            if (awaitingQualifiedName || registration?.ProjectionShape == ToolProjectionShape.DynamicLifecycle)
                return;

            FinalizeReasoning();
            FinalizeAgentMessage();
            _previewItemsByIndex ??= [];
            if (!_previewItemsByIndex.TryGetValue(toolCallIndex, out var streamingToolCallItem))
            {
                streamingToolCallItem = new SessionItem
                {
                    Id = SessionIdGenerator.NewItemId(nextItemSequence()),
                    TurnId = turn.Id,
                    CreatedAt = DateTimeOffset.UtcNow
                };
                var callId = toolArgsDelta.CallId ?? string.Empty;
                if (registration is not null)
                {
                    ApplyStartedProjection(
                        streamingToolCallItem, registration, snapshot!.ProviderFlatNames[canonicalName],
                        snapshot.Revision, callId, null);
                }
                else
                {
                    streamingToolCallItem.Type = ItemType.ToolCall;
                    streamingToolCallItem.Payload = new ToolCallPayload
                    {
                        ToolName = resolvedToolName,
                        ProviderFlatName = resolvedToolName,
                        CallId = callId
                    };
                }
                streamingToolCallItem.Status = ItemStatus.Streaming;
                _previewItemsByIndex[toolCallIndex] = streamingToolCallItem;
                turn.Items.Add(streamingToolCallItem);
                channel.EmitItemStarted(streamingToolCallItem);
            }

            if (!string.IsNullOrWhiteSpace(toolArgsDelta.CallId))
            {
                _previewItemsByCallId ??= new Dictionary<string, SessionItem>(StringComparer.Ordinal);
                _previewItemsByCallId.TryAdd(toolArgsDelta.CallId, streamingToolCallItem);
            }

            channel.EmitItemDelta(streamingToolCallItem, new ToolCallArgumentsDelta
            {
                ToolName = resolvedToolName,
                CallId = toolArgsDelta.CallId,
                Delta = toolArgsDelta.ArgumentsDelta
            });
        }

        public bool TryProjectToolCall(
            FunctionCallContent call,
            bool supportsCommandExecutionStreaming,
            string workspacePath,
            bool supportsToolExecutionLifecycle)
        {
            FinalizeReasoning();
            FinalizeAgentMessage();
            var hasCallId = !string.IsNullOrWhiteSpace(call.CallId);
            if (hasCallId && turnRuntime.ToolInvocationItems.ContainsKey(call.CallId))
                return false;

            RegisterCommandExecutionIfNeeded(
                call, turn, nextItemSequence, channel, supportsCommandExecutionStreaming, workspacePath);
            RegisterToolExecutionIfNeeded(
                call, turn, nextItemSequence, channel, supportsToolExecutionLifecycle);

            SessionItem? preview = null;
            if (hasCallId)
                _previewItemsByCallId?.TryGetValue(call.CallId, out preview);

            var item = preview ?? new SessionItem
            {
                Id = SessionIdGenerator.NewItemId(nextItemSequence()),
                TurnId = turn.Id,
                CreatedAt = DateTimeOffset.UtcNow
            };
            if (turnRuntime.ToolSnapshot is { } snapshot
                && TryResolveProviderFunctionCall(snapshot, call, out var canonicalName)
                && snapshot.Registrations.TryGetValue(canonicalName, out var registration))
            {
                var emitStarted = preview is null || !HasTrustedProjection(preview);
                var arguments = ReadArguments(call);
                ApplyStartedProjection(
                    item,
                    registration,
                    snapshot.ProviderFlatNames.GetValueOrDefault(canonicalName)
                        ?? throw new InvalidOperationException(
                            $"Missing flat provider alias for tool '{canonicalName}'."),
                    snapshot.Revision,
                    call.CallId,
                    arguments ?? []);
                if (preview is null)
                    turn.Items.Add(item);
                if (emitStarted)
                    channel.EmitItemStarted(item);
            }
            else
            {
                if (preview is null)
                    item.Type = ItemType.ToolCall;
                item.Status = ItemStatus.Completed;
                item.CompletedAt = DateTimeOffset.UtcNow;
                item.Payload = new ToolCallPayload
                {
                    ToolName = call.Name,
                    ProviderFlatName = call.Name,
                    Arguments = ReadArguments(call),
                    CallId = call.CallId
                };
                if (preview is null)
                {
                    turn.Items.Add(item);
                    channel.EmitItemStarted(item);
                }

                channel.EmitItemCompleted(item);
            }

            if (preview is not null)
                RemoveToolPreview(call.CallId, preview);
            return true;
        }

        public void AppendToolResult(FunctionResultContent result)
        {
            FinalizeReasoning();
            if (!string.IsNullOrWhiteSpace(result.CallId)
                && turnRuntime.ToolInvocationItems.ContainsKey(result.CallId))
                return;

            var resultText = ImageContentSanitizingChatClient.DescribeResult(result.Result);
            var toolResultErrorCode = StreamingFunctionInvokingChatClient.GetToolResultErrorCode(result);
            var contentItems = ExtractToolResultContentItems(result.Result);
            var persistedCall = turn.Items
                .Select(static item => item.Payload as ToolCallPayload)
                .LastOrDefault(payload => string.Equals(
                    payload?.CallId,
                    result.CallId,
                    StringComparison.Ordinal));
            var toolResultItem = new SessionItem
            {
                Id = SessionIdGenerator.NewItemId(nextItemSequence()),
                TurnId = turn.Id,
                Type = ItemType.ToolResult,
                Status = ItemStatus.Completed,
                CreatedAt = DateTimeOffset.UtcNow,
                CompletedAt = DateTimeOffset.UtcNow,
                Payload = new ToolResultPayload
                {
                    CallId = result.CallId,
                    Namespace = persistedCall?.Namespace,
                    ToolName = persistedCall?.ToolName ?? string.Empty,
                    ProviderFlatName = persistedCall?.ProviderFlatName ?? string.Empty,
                    Result = resultText,
                    ContentItems = contentItems,
                    Success = result.Exception == null
                        && toolResultErrorCode == null
                        && !StreamingFunctionInvokingChatClient.IsInvalidToolArgumentsResult(result),
                    ErrorCode = toolResultErrorCode,
                    ErrorMessage = toolResultErrorCode == null ? null : resultText
                }
            };
            turn.Items.Add(toolResultItem);
            channel.EmitItemStarted(toolResultItem);
            channel.EmitItemCompleted(toolResultItem);
        }

        private void RemoveToolPreview(string callId, SessionItem item)
        {
            _previewItemsByCallId?.Remove(callId);
            if (_previewItemsByIndex is null)
                return;

            foreach (var (index, preview) in _previewItemsByIndex)
            {
                if (!ReferenceEquals(preview, item))
                    continue;

                _previewItemsByIndex.Remove(index);
                return;
            }
        }

        private static JsonObject? ReadArguments(FunctionCallContent call) =>
            call.Arguments is null ? null : JsonNode.Parse(JsonSerializer.Serialize(call.Arguments)) as JsonObject;

        public void FinalizeAgentMessage(string phase = AgentMessagePhases.Commentary)
        {
            if (_agentMessage == null)
                return;

            var message = (AgentMessagePayload)_agentMessage.Payload!;
            _agentMessage.Payload = message with { Text = _agentText, Phase = phase };
            _agentMessage.Status = ItemStatus.Completed;
            _agentMessage.CompletedAt = DateTimeOffset.UtcNow;
            channel.EmitItemCompleted(_agentMessage);
            _agentMessage = null;
            _agentText = string.Empty;
        }

        public void FinalizeReasoning()
        {
            if (_reasoning == null)
                return;

            _reasoning.Payload = new ReasoningContentPayload { Text = _reasoningText };
            _reasoning.Status = ItemStatus.Completed;
            _reasoning.CompletedAt = DateTimeOffset.UtcNow;
            channel.EmitItemCompleted(_reasoning);
            _reasoning = null;
            _reasoningText = string.Empty;
            _reasoningGroup = null;
        }
    }
}
