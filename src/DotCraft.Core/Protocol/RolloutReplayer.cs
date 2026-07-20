using System.Text.Json;
using Microsoft.Extensions.AI;

namespace DotCraft.Protocol;

internal sealed class RolloutReplayer : IRolloutReplayer
{
    private static readonly JsonSerializerOptions JsonOptions = SessionJsonOptions.Default;

    public async Task<ModelHistoryReplayResult> ReplayModelHistoryAsync(
        string rolloutPath,
        IReadOnlyList<SessionTurn> survivingTurns,
        string? excludedTurnId = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rolloutPath);
        ArgumentNullException.ThrowIfNull(survivingTurns);

        if (!File.Exists(rolloutPath))
            return new ModelHistoryReplayResult([], HasModelHistoryRecords: false);

        var orderedTurns = survivingTurns
            .Where(turn => !string.Equals(turn.Id, excludedTurnId, StringComparison.Ordinal))
            .ToList();
        var survivingTurnIds = orderedTurns.Select(static turn => turn.Id).ToHashSet(StringComparer.Ordinal);
        var reverseBatches = new List<DecodedModelBatch>();
        var fallbackTurnIds = new HashSet<string>(StringComparer.Ordinal);
        var warnings = new List<ModelHistoryReplayWarning>();
        var codec = new ModelHistoryCodec();
        var hasRecords = false;
        var recordsDecoded = 0;
        var rejectedRecords = 0;
        List<ChatMessage>? replacement = null;
        ContextCompactedPayload? selectedCheckpoint = null;
        var reader = new ReverseJsonlReader(rolloutPath);

        await foreach (var line in reader.ReadLinesAsync(ct))
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(line);
            }
            catch (JsonException)
            {
                Reject("malformed_json", "Skipped a malformed rollout record.", null);
                continue;
            }

            using (document)
            {
                var root = document.RootElement;
                var kind = TryGetString(root, "kind");
                var envelopeTurnId = TryGetEnvelopeTurnId(root, kind);
                ThreadRolloutRecord? record;
                try
                {
                    record = root.Deserialize<ThreadRolloutRecord>(JsonOptions);
                }
                catch (Exception ex) when (ex is JsonException or NotSupportedException or ArgumentException)
                {
                    var checkpointRecord = string.Equals(kind, "context_compacted", StringComparison.Ordinal);
                    Reject(
                        checkpointRecord ? "invalid_checkpoint" : "malformed_record",
                        checkpointRecord
                            ? "Skipped an unreadable compaction checkpoint."
                            : "Skipped an unreadable rollout record.",
                        envelopeTurnId,
                        markFallback: !checkpointRecord);
                    continue;
                }

                if (record == null)
                {
                    Reject("empty_record", "Skipped an empty rollout record.", envelopeTurnId);
                    continue;
                }

                recordsDecoded++;
                if (string.Equals(kind, "model_history_messages_appended", StringComparison.Ordinal))
                {
                    hasRecords = true;
                    var batch = record.ModelHistoryMessagesAppended;
                    if (batch == null || string.IsNullOrWhiteSpace(batch.TurnId))
                    {
                        Reject("invalid_model_batch", "Skipped an invalid model-history batch.", envelopeTurnId);
                        continue;
                    }
                    if (!survivingTurnIds.Contains(batch.TurnId))
                        continue;

                    try
                    {
                        var decodedMessages = batch.Messages
                            .Select(message => codec.Decode(WithTurnId(message, batch.TurnId)))
                            .ToList();
                        reverseBatches.Add(new DecodedModelBatch(batch.TurnId, decodedMessages));
                    }
                    catch (Exception ex) when (ex is JsonException or NotSupportedException or ArgumentException)
                    {
                        Reject("invalid_model_batch", "Skipped an undecodable model-history batch.", batch.TurnId);
                    }
                }
                else if (string.Equals(kind, "context_compacted", StringComparison.Ordinal))
                {
                    hasRecords = true;
                    var checkpoint = record.ContextCompacted;
                    if (checkpoint == null || string.IsNullOrWhiteSpace(checkpoint.CoveredThroughTurnId))
                    {
                        Reject(
                            "invalid_checkpoint",
                            "Skipped an invalid compaction checkpoint.",
                            envelopeTurnId,
                            markFallback: false);
                        continue;
                    }
                    if (!survivingTurnIds.Contains(checkpoint.CoveredThroughTurnId)
                        || fallbackTurnIds.Contains(checkpoint.CoveredThroughTurnId))
                    {
                        continue;
                    }

                    try
                    {
                        replacement = checkpoint.ReplacementHistory.Select(codec.Decode).ToList();
                        selectedCheckpoint = checkpoint;
                        break;
                    }
                    catch (Exception ex) when (ex is JsonException or NotSupportedException or ArgumentException)
                    {
                        replacement = null;
                        Reject(
                            "invalid_checkpoint",
                            "Skipped an undecodable compaction checkpoint.",
                            checkpoint.CoveredThroughTurnId,
                            markFallback: false);
                    }
                }
            }
        }

        reverseBatches.Reverse();
        var messages = replacement ?? [];
        var firstTailTurnIndex = 0;
        if (selectedCheckpoint != null)
        {
            firstTailTurnIndex = orderedTurns.FindIndex(turn =>
                string.Equals(turn.Id, selectedCheckpoint.CoveredThroughTurnId, StringComparison.Ordinal));
        }

        for (var turnIndex = Math.Max(0, firstTailTurnIndex); turnIndex < orderedTurns.Count; turnIndex++)
        {
            var turn = orderedTurns[turnIndex];
            var turnBatches = reverseBatches
                .Where(batch => string.Equals(batch.TurnId, turn.Id, StringComparison.Ordinal))
                .ToList();
            if (fallbackTurnIds.Contains(turn.Id))
            {
                messages.AddRange(ThreadStore.BuildModelVisibleHistoryFromTurn(turn));
                continue;
            }
            if (turnBatches.Count == 0)
            {
                if (selectedCheckpoint != null && turnIndex == firstTailTurnIndex)
                    continue;
                fallbackTurnIds.Add(turn.Id);
                messages.AddRange(ThreadStore.BuildModelVisibleHistoryFromTurn(turn));
                continue;
            }

            foreach (var batch in turnBatches)
                messages.AddRange(batch.Messages);
        }

        RolloutTelemetry.RecordResume(reader.BytesRead, recordsDecoded, rejectedRecords);
        return new ModelHistoryReplayResult(
            messages,
            hasRecords,
            warnings,
            rejectedRecords,
            fallbackTurnIds,
            reader.BytesRead,
            recordsDecoded);

        void Reject(
            string code,
            string message,
            string? turnId,
            bool markFallback = true)
        {
            rejectedRecords++;
            if (markFallback && !string.IsNullOrWhiteSpace(turnId) && survivingTurnIds.Contains(turnId))
                fallbackTurnIds.Add(turnId);
            warnings.Add(new ModelHistoryReplayWarning(code, message, turnId));
        }
    }

    private static string? TryGetString(JsonElement value, string propertyName) =>
        value.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static string? TryGetEnvelopeTurnId(JsonElement root, string? kind)
    {
        var payloadName = kind switch
        {
            "model_history_messages_appended" => "modelHistoryMessagesAppended",
            "context_compacted" => "contextCompacted",
            _ => null
        };
        if (payloadName == null
            || !root.TryGetProperty(payloadName, out var payload)
            || payload.ValueKind != JsonValueKind.Object)
        {
            return null;
        }
        return TryGetString(payload, kind == "context_compacted" ? "coveredThroughTurnId" : "turnId");
    }

    private static ModelHistoryMessage WithTurnId(ModelHistoryMessage message, string turnId)
    {
        if (!string.IsNullOrWhiteSpace(message.TurnId))
            return message;

        return new ModelHistoryMessage
        {
            SchemaVersion = message.SchemaVersion,
            TurnId = turnId,
            Role = message.Role,
            MessageId = message.MessageId,
            AuthorName = message.AuthorName,
            CreatedAt = message.CreatedAt,
            AdditionalProperties = message.AdditionalProperties,
            Contents = message.Contents
        };
    }

    private sealed record DecodedModelBatch(string TurnId, IReadOnlyList<ChatMessage> Messages);
}
