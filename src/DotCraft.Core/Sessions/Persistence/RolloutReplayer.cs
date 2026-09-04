using System.Text.Json;
using Microsoft.Extensions.AI;

namespace DotCraft.Sessions;

internal sealed class RolloutReplayer : IRolloutReplayer
{
    private static readonly JsonSerializerOptions JsonOptions = SessionJsonOptions.Default;

    public async Task<ModelHistoryReplayResult> ReplayModelHistoryAsync(
        string rolloutPath,
        IReadOnlyList<SessionTurn> survivingTurns,
        string? excludedTurnId = null,
        CancellationToken ct = default,
        string? expectedThreadId = null)
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
            if (Utf8JsonlReader.IsWhiteSpace(line.Span))
                continue;

            ThreadRolloutRecord? record;
            try
            {
                record = JsonSerializer.Deserialize<ThreadRolloutRecord>(line.Span, JsonOptions);
            }
            catch (Exception ex) when (ex is JsonException or NotSupportedException or ArgumentException or InvalidOperationException)
            {
                if (!TryReadEnvelope(line, out var failedKind, out var failedTurnId))
                {
                    Reject("malformed_json", "Skipped a malformed rollout record.", null);
                    continue;
                }

                var checkpointRecord = string.Equals(failedKind, "context_compacted", StringComparison.Ordinal);
                Reject(
                    checkpointRecord ? "invalid_checkpoint" : "malformed_record",
                    checkpointRecord
                        ? "Skipped an unreadable compaction checkpoint."
                        : "Skipped an unreadable rollout record.",
                    failedTurnId,
                    markFallback: !checkpointRecord);
                continue;
            }

            var kind = record?.Kind;
            var envelopeTurnId = record == null ? null : TryGetEnvelopeTurnId(record, kind);
            if (record == null)
            {
                Reject("empty_record", "Skipped an empty rollout record.", envelopeTurnId);
                continue;
            }

            if (!TryValidateTargetEnvelope(record, kind, out var envelopeError))
            {
                Reject(
                    string.Equals(kind, "context_compacted", StringComparison.Ordinal)
                        ? "invalid_checkpoint"
                        : "malformed_record",
                    envelopeError!,
                    envelopeTurnId,
                    markFallback: !string.Equals(kind, "context_compacted", StringComparison.Ordinal));
                continue;
            }

            recordsDecoded++;
            if (string.Equals(kind, "model_history_messages_appended", StringComparison.Ordinal))
            {
                hasRecords = true;
                var batch = record.ModelHistoryMessagesAppended;
                if (!TryValidateModelBatch(batch, out var batchError))
                {
                    Reject("invalid_model_batch", batchError!, envelopeTurnId);
                    continue;
                }
                var validBatch = batch!;
                if (expectedThreadId != null
                    && !string.Equals(validBatch.ThreadId, expectedThreadId, StringComparison.Ordinal))
                {
                    Reject("cross_thread_record", "Skipped a model-history batch belonging to another thread.", validBatch.TurnId);
                    continue;
                }
                if (!survivingTurnIds.Contains(validBatch.TurnId))
                    continue;

                try
                {
                    var decodedMessages = validBatch.Messages
                        .Select(message => codec.Decode(WithTurnId(message, validBatch.TurnId)))
                        .ToList();
                    reverseBatches.Add(new DecodedModelBatch(validBatch.TurnId, decodedMessages));
                }
                catch (Exception ex) when (ex is JsonException or NotSupportedException or ArgumentException or InvalidOperationException)
                {
                    Reject("invalid_model_batch", "Skipped an undecodable model-history batch.", validBatch.TurnId);
                }
            }
            else if (string.Equals(kind, "context_compacted", StringComparison.Ordinal))
            {
                hasRecords = true;
                var checkpoint = record.ContextCompacted;
                if (!TryValidateCheckpoint(checkpoint, out var checkpointError))
                {
                    Reject(
                        "invalid_checkpoint",
                        checkpointError!,
                        envelopeTurnId,
                        markFallback: false);
                    continue;
                }
                var validCheckpoint = checkpoint!;
                if (expectedThreadId != null
                    && !string.Equals(validCheckpoint.ThreadId, expectedThreadId, StringComparison.Ordinal))
                {
                    Reject(
                        "cross_thread_record",
                        "Skipped a compaction checkpoint belonging to another thread.",
                        validCheckpoint.CoveredThroughTurnId,
                        markFallback: false);
                    continue;
                }
                if (!survivingTurnIds.Contains(validCheckpoint.CoveredThroughTurnId)
                    || fallbackTurnIds.Contains(validCheckpoint.CoveredThroughTurnId))
                {
                    continue;
                }

                try
                {
                    replacement = validCheckpoint.ReplacementHistory.Select(codec.Decode).ToList();
                    selectedCheckpoint = validCheckpoint;
                    break;
                }
                catch (Exception ex) when (ex is JsonException or NotSupportedException or ArgumentException or InvalidOperationException)
                {
                    replacement = null;
                    Reject(
                        "invalid_checkpoint",
                        "Skipped an undecodable compaction checkpoint.",
                        validCheckpoint.CoveredThroughTurnId,
                        markFallback: false);
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

    private static bool TryValidateTargetEnvelope(
        ThreadRolloutRecord record,
        string? kind,
        out string? error)
    {
        error = null;
        var populatedPayloads = (record.ContextCompacted == null ? 0 : 1)
            + (record.ModelHistoryMessagesAppended == null ? 0 : 1);

        if (kind is not ("context_compacted" or "model_history_messages_appended"))
            return true;

        var hasExpectedPayload = kind == "context_compacted"
            ? record.ContextCompacted != null
            : record.ModelHistoryMessagesAppended != null;
        if (!hasExpectedPayload)
        {
            error = $"Rollout record '{kind}' is missing its object payload.";
            return false;
        }

        if (populatedPayloads != 1)
        {
            error = $"Rollout record '{kind}' contains inconsistent payload fields.";
            return false;
        }

        return true;
    }

    private static bool TryValidateModelBatch(
        ModelHistoryMessagesAppendedPayload? batch,
        out string? error)
    {
        error = null;
        if (batch is null || string.IsNullOrWhiteSpace(batch.ThreadId) || string.IsNullOrWhiteSpace(batch.TurnId))
        {
            error = "Skipped an invalid model-history batch with missing identity fields.";
            return false;
        }
        if (batch.Messages is null)
        {
            error = "Skipped an invalid model-history batch with missing messages.";
            return false;
        }
        if (batch.Messages.Any(static message => message is null))
        {
            error = "Skipped an invalid model-history batch containing a null message.";
            return false;
        }
        return true;
    }

    private static bool TryValidateCheckpoint(
        ContextCompactedPayload? checkpoint,
        out string? error)
    {
        error = null;
        if (checkpoint is null
            || string.IsNullOrWhiteSpace(checkpoint.ThreadId)
            || string.IsNullOrWhiteSpace(checkpoint.CoveredThroughTurnId))
        {
            error = "Skipped an invalid compaction checkpoint with missing identity fields.";
            return false;
        }
        if (checkpoint.ReplacementHistory is null)
        {
            error = "Skipped an invalid compaction checkpoint with missing replacement history.";
            return false;
        }
        if (checkpoint.ReplacementHistory.Any(static message => message is null))
        {
            error = "Skipped an invalid compaction checkpoint containing a null message.";
            return false;
        }
        return true;
    }

    private static string? TryGetString(JsonElement value, string propertyName) =>
        value.ValueKind == JsonValueKind.Object
        && value.TryGetProperty(propertyName, out var property)
        && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static bool TryReadEnvelope(ReadOnlyMemory<byte> line, out string? kind, out string? turnId)
    {
        try
        {
            using var document = JsonDocument.Parse(line);
            kind = TryGetString(document.RootElement, "kind");
            turnId = TryGetEnvelopeTurnId(document.RootElement, kind);
            return true;
        }
        catch (JsonException)
        {
            kind = null;
            turnId = null;
            return false;
        }
    }

    private static string? TryGetEnvelopeTurnId(ThreadRolloutRecord record, string? kind) =>
        kind switch
        {
            "model_history_messages_appended" => record.ModelHistoryMessagesAppended?.TurnId,
            "context_compacted" => record.ContextCompacted?.CoveredThroughTurnId,
            _ => null
        };

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
