using DotCraft.Logging;

namespace DotCraft.Sessions;

public sealed partial class SessionService
{
    /// <summary>
    /// Projects streamed assistant content into canonical Session Items. It owns the
    /// interleaving rule: switching between answer text and reasoning closes the prior item.
    /// </summary>
    private sealed class TurnItemProjector(
        string threadId,
        SessionTurn turn,
        SessionEventChannel channel,
        Func<int> nextItemSequence,
        SessionStreamDebugLogger? debugLogger)
    {
        private SessionItem? _agentMessage;
        private SessionItem? _reasoning;
        private string _agentText = string.Empty;
        private string _reasoningText = string.Empty;
        private int _agentDeltaIndex;

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

        public void AppendReasoning(string text)
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

            _reasoningText += text;
            channel.EmitItemDelta(_reasoning, new ReasoningContentDelta { TextDelta = text });
        }

        public void FinalizeAgentMessage()
        {
            if (_agentMessage == null)
                return;

            _agentMessage.Payload = new AgentMessagePayload { Text = _agentText };
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
        }
    }
}
