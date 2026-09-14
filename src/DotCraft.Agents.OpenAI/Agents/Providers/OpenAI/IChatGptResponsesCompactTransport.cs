using System.ClientModel;
using System.ClientModel.Primitives;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpenAI.Responses;

#pragma warning disable OPENAI001

namespace DotCraft.Agents;

internal interface IChatGptResponsesCompactTransport
{
    Task<ChatGptResponsesCompactResponse> CompactAsync(
        ChatGptResponsesCompactRequest requestBody,
        CancellationToken cancellationToken);
}

internal sealed class SdkChatGptResponsesCompactTransport(ResponsesClient responsesClient)
    : IChatGptResponsesCompactTransport
{
    public async Task<ChatGptResponsesCompactResponse> CompactAsync(
        ChatGptResponsesCompactRequest requestBody,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(requestBody);
        var started = Stopwatch.GetTimestamp();
        var status = "failed";
        string? failureReason = "provider_compaction_failed";
        int? statusCode = null;
        ChatGptResponsesCompactResponse? completed = null;
        try
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(requestBody, ChatGptResponsesCompactJson.Options);
            using var content = BinaryContent.Create(BinaryData.FromBytes(bytes));
            var result = await responsesClient.CreateResponseAsync(content, new RequestOptions
            {
                BufferResponse = false,
                CancellationToken = cancellationToken
            }).ConfigureAwait(false);
            using var response = result.GetRawResponse();
            statusCode = response.Status;
            if (response.ContentStream == null)
                throw Invalid("Responses SSE response did not contain a content stream.");
            completed = await ReadResponseAsync(response.ContentStream, cancellationToken).ConfigureAwait(false);
            status = "completed";
            failureReason = null;
            return completed;
        }
        catch (ClientResultException ex)
        {
            statusCode = ex.Status;
            throw;
        }
        catch (OperationCanceledException)
        {
            status = "cancelled";
            failureReason = "cancelled";
            throw;
        }
        catch (InvalidDataException)
        {
            failureReason = "provider_compaction_invalid_response";
            throw;
        }
        finally
        {
            var context = ProviderRequestContextScope.Current;
            context?.Diagnostics?.Record(new ModelRuntimeDiagnostic("provider.compaction", new Dictionary<string, object?>
            {
                ["sessionKey"] = context.CurrentIdentity.CurrentThreadId,
                ["providerProtocol"] = "openai-responses",
                ["eventType"] = "compaction.v2",
                ["protocolVersion"] = 2,
                ["requestKind"] = "compaction",
                ["turnId"] = context.CurrentIdentity.TurnId,
                ["modelId"] = requestBody.Model,
                ["status"] = status,
                ["statusCode"] = statusCode,
                ["failureReason"] = failureReason,
                ["inputItemCount"] = requestBody.Input.Count,
                ["responseId"] = completed?.ResponseId,
                ["usagePresent"] = completed?.InputTokens != null,
                ["inputTokens"] = completed?.InputTokens,
                ["outputTokens"] = completed?.OutputTokens,
                ["durationMs"] = Stopwatch.GetElapsedTime(started).TotalMilliseconds
            }));
        }
    }

    internal static async Task<ChatGptResponsesCompactResponse> ReadResponseAsync(
        Stream stream, CancellationToken cancellationToken)
    {
        var output = new List<JsonElement>();
        try
        {
            await foreach (var data in ResponsesSseReader.ReadDataAsync(stream, cancellationToken).ConfigureAwait(false))
            {
                if (data == "[DONE]")
                    break;
                var update = JsonSerializer.Deserialize<CompactStreamEvent>(data, ChatGptResponsesCompactJson.Options)
                    ?? throw Invalid("Empty stream event.");
                switch (update.Type)
                {
                    case "response.output_item.done":
                        if (update.Item is not { ValueKind: JsonValueKind.Object } item)
                            throw Invalid("Output event must contain an object item.");
                        if (item.TryGetProperty("type", out var type) && type.ValueKind == JsonValueKind.String
                            && type.GetString() == "compaction")
                            output.Add(item.Clone());
                        break;
                    case "response.completed":
                        if (update.Response is not { Id.Length: > 0 } response
                            || response.Status is not (null or "completed"))
                            throw Invalid("Completed event must contain a successful response identity.");
                        var result = new ChatGptResponsesCompactResponse
                        {
                            ResponseId = response.Id,
                            InputTokens = response.Usage?.InputTokens,
                            OutputTokens = response.Usage?.OutputTokens,
                            Output = output
                        };
                        OpenAIResponsesCompactor.ValidateOutput(result);
                        return result;
                    case "response.failed":
                    case "response.incomplete":
                    case "error":
                    case "response.error":
                        throw Invalid($"Stream ended with {update.Type}.");
                    case null:
                        throw Invalid("Stream event type is required.");
                }
            }
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("provider_compaction_invalid_response: Malformed Responses SSE event.", ex);
        }
        throw Invalid("Stream ended before response.completed.");
    }

    private static InvalidDataException Invalid(string message) =>
        new($"provider_compaction_invalid_response: {message}");

    private sealed record CompactStreamEvent(
        string? Type,
        JsonElement? Item,
        CompactCompletion? Response);

    private sealed record CompactCompletion(string? Id, string? Status, CompactUsage? Usage);

    private sealed record CompactUsage(
        [property: JsonPropertyName("input_tokens")] long? InputTokens,
        [property: JsonPropertyName("output_tokens")] long? OutputTokens);
}
