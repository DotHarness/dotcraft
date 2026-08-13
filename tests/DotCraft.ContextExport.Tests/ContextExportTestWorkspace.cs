using System.Text.Json;
using DotCraft.Sessions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.AI;

namespace DotCraft.ContextExport.Tests;

internal sealed class ContextExportTestWorkspace : IDisposable
{
    public ContextExportTestWorkspace()
    {
        Root = Path.Combine(Path.GetTempPath(), "ContextExportTests_" + Guid.NewGuid().ToString("N")[..8]);
        Workspace = Path.Combine(Root, "workspace");
        Craft = Path.Combine(Workspace, ".craft");
        Directory.CreateDirectory(Workspace);
        ThreadStore = new ThreadStore(Craft);
        Persistence = new SessionPersistenceService(ThreadStore);
    }

    public string Root { get; }

    public string Workspace { get; }

    public string Craft { get; }

    public ThreadStore ThreadStore { get; }

    public SessionPersistenceService Persistence { get; }

    public async Task AppendModelHistoryAsync(
        string threadId,
        IReadOnlyList<ChatMessage> messages,
        string turnId)
    {
        var record = new
        {
            kind = "model_history_messages_appended",
            timestamp = DateTimeOffset.UtcNow,
            modelHistoryMessagesAppended = new
            {
                threadId,
                turnId,
                messages = messages.Select(message => EncodeMessage(message, turnId)).ToArray()
            }
        };
        await AppendRolloutRecordAsync(threadId, record);
    }

    public Task AppendTurnStateAsync(SessionThread thread, SessionTurn turn)
    {
        var record = new
        {
            kind = "turn_state_replaced",
            timestamp = DateTimeOffset.UtcNow,
            turnStateReplaced = new
            {
                threadId = thread.Id,
                turn,
                threadStatus = thread.Status,
                lastActiveAt = thread.LastActiveAt,
                displayName = thread.DisplayName
            }
        };
        return AppendRolloutRecordAsync(thread.Id, record);
    }

    public async Task AppendTraceEventAsync(string threadId, string content, string modelId)
    {
        await using var connection = new SqliteConnection($"Data Source={Path.Combine(Craft, "state.db")}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO trace_events (
                event_id, session_key, timestamp, type, model_id, event_json)
            VALUES ($eventId, $sessionKey, $timestamp, 'Error', $modelId, $eventJson)
            """;
        command.Parameters.AddWithValue("$eventId", Guid.NewGuid().ToString("N"));
        command.Parameters.AddWithValue("$sessionKey", threadId);
        command.Parameters.AddWithValue("$timestamp", DateTimeOffset.UtcNow.UtcDateTime.ToString("O"));
        command.Parameters.AddWithValue("$modelId", modelId);
        command.Parameters.AddWithValue("$eventJson", JsonSerializer.Serialize(new { content }));
        await command.ExecuteNonQueryAsync();
    }

    public void Dispose()
    {
        try { Directory.Delete(Root, recursive: true); }
        catch { /* best-effort cleanup */ }
    }

    private static object EncodeMessage(ChatMessage message, string turnId) => new
    {
        schemaVersion = 1,
        turnId,
        role = message.Role.Value,
        messageId = message.MessageId,
        authorName = message.AuthorName,
        createdAt = message.CreatedAt,
        additionalProperties = message.AdditionalProperties,
        contents = message.Contents.Select(EncodeContent).ToArray()
    };

    private static object EncodeContent(AIContent content) => content switch
    {
        TextContent text => new
        {
            kind = "text",
            payload = (object)new { text = text.Text, additionalProperties = text.AdditionalProperties }
        },
        TextReasoningContent reasoning => new
        {
            kind = "reasoning",
            payload = (object)new
            {
                text = reasoning.Text,
                protectedData = reasoning.ProtectedData,
                additionalProperties = reasoning.AdditionalProperties
            }
        },
        FunctionCallContent call => new
        {
            kind = "function_call",
            payload = (object)new
            {
                callId = call.CallId,
                name = call.Name,
                arguments = call.Arguments,
                informationalOnly = call.InformationalOnly,
                @namespace = (string?)null,
                providerFlatName = (string?)null,
                additionalProperties = call.AdditionalProperties
            }
        },
        FunctionResultContent result => new
        {
            kind = "function_result",
            payload = (object)new
            {
                callId = result.CallId,
                result = new { schemaVersion = 1, kind = "json", json = result.Result, contents = (object?)null },
                additionalProperties = result.AdditionalProperties
            }
        },
        _ => throw new NotSupportedException($"Unsupported test model content: {content.GetType().Name}")
    };

    private async Task AppendRolloutRecordAsync(string threadId, object record)
    {
        var path = Path.Combine(Craft, "threads", "active", $"{threadId}.jsonl");
        await File.AppendAllTextAsync(
            path,
            JsonSerializer.Serialize(record, SessionJsonOptions.Default) + Environment.NewLine);
    }
}
