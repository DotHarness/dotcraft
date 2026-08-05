using System.ClientModel.Primitives;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Tracing;

namespace DotCraft.Agents;

/// <summary>
/// Opt-in local capture for final OpenAI-compatible HTTP payloads.
/// </summary>
internal sealed class LlmHttpCapturePipelinePolicy : PipelinePolicy
{
    internal const string EnabledEnvironmentVariable = "DOTCRAFT_LLM_HTTP_CAPTURE";
    internal const string DirectoryEnvironmentVariable = "DOTCRAFT_LLM_HTTP_CAPTURE_DIR";
    private const int MaxInlineBodyLength = 16 * 1024 * 1024;

    private static int _sequence;

    public override void Process(PipelineMessage message, IReadOnlyList<PipelinePolicy> pipeline, int currentIndex)
    {
        if (!IsEnabled())
        {
            ProcessNext(message, pipeline, currentIndex);
            return;
        }

        var startedAt = DateTimeOffset.UtcNow;
        var capture = CreateCapture(message, startedAt);
        try
        {
            ProcessNext(message, pipeline, currentIndex);
            AddResponse(capture, message);
            WriteCapture(capture, startedAt);
        }
        catch (Exception ex)
        {
            capture["exception"] = new JsonObject
            {
                ["type"] = ex.GetType().FullName,
                ["message"] = ex.Message
            };
            WriteCapture(capture, startedAt);
            throw;
        }
    }

    public override async ValueTask ProcessAsync(
        PipelineMessage message,
        IReadOnlyList<PipelinePolicy> pipeline,
        int currentIndex)
    {
        if (!IsEnabled())
        {
            await ProcessNextAsync(message, pipeline, currentIndex).ConfigureAwait(false);
            return;
        }

        var startedAt = DateTimeOffset.UtcNow;
        var capture = await CreateCaptureAsync(message, startedAt).ConfigureAwait(false);
        try
        {
            await ProcessNextAsync(message, pipeline, currentIndex).ConfigureAwait(false);
            AddResponse(capture, message);
            WriteCapture(capture, startedAt);
        }
        catch (Exception ex)
        {
            capture["exception"] = new JsonObject
            {
                ["type"] = ex.GetType().FullName,
                ["message"] = ex.Message
            };
            WriteCapture(capture, startedAt);
            throw;
        }
    }

    internal static bool IsEnabled()
    {
        var value = Environment.GetEnvironmentVariable(EnabledEnvironmentVariable);
        return !string.IsNullOrWhiteSpace(value) &&
               !string.Equals(value, "0", StringComparison.OrdinalIgnoreCase) &&
               !string.Equals(value, "false", StringComparison.OrdinalIgnoreCase) &&
               !string.Equals(value, "off", StringComparison.OrdinalIgnoreCase);
    }

    private static JsonObject CreateCapture(PipelineMessage message, DateTimeOffset startedAt) =>
        CreateCapture(message, startedAt, ReadRequestBody(message));

    private static async ValueTask<JsonObject> CreateCaptureAsync(PipelineMessage message, DateTimeOffset startedAt) =>
        CreateCapture(message, startedAt, await ReadRequestBodyAsync(message).ConfigureAwait(false));

    private static JsonObject CreateCapture(PipelineMessage message, DateTimeOffset startedAt, string? requestBody)
    {
        var request = message.Request;
        var capture = new JsonObject
        {
            ["capturedAt"] = startedAt.ToString("O"),
            ["sessionKey"] = TracingChatClient.GetActiveSessionKey(),
            ["request"] = new JsonObject
            {
                ["method"] = request.Method,
                ["uri"] = request.Uri?.ToString(),
                ["headers"] = CaptureHeaders(request.Headers)
            }
        };

        var requestObject = capture["request"]!.AsObject();
        AddBody(requestObject, requestBody);
        if (OpenAIResponsesRequestCompressionPipelinePolicy.TryGetOriginalBody(
                message,
                out _,
                out var semanticBodySha256))
        {
            requestObject["semanticBodySha256"] = semanticBodySha256;
            requestObject["contentEncoding"] = "zstd";
            requestObject["wireBodyLength"] = ReadWireBodyLength(message);
        }
        return capture;
    }

    private static void AddResponse(JsonObject capture, PipelineMessage message)
    {
        if (message.Response == null)
            return;

        var response = message.Response;
        var responseObject = new JsonObject
        {
            ["status"] = response.Status,
            ["reasonPhrase"] = response.ReasonPhrase,
            ["headers"] = CaptureHeaders(response.Headers)
        };

        AddBody(responseObject, TryReadBufferedResponseBody(response));
        capture["response"] = responseObject;
    }

    private static void AddBody(JsonObject parent, string? body)
    {
        if (string.IsNullOrEmpty(body))
            return;

        if (body.Length > MaxInlineBodyLength)
        {
            parent["bodyTruncated"] = true;
            parent["bodyLength"] = body.Length;
            parent["body"] = body[..MaxInlineBodyLength];
            return;
        }

        parent["body"] = TryParseJson(body) ?? body;
    }

    private static JsonObject CaptureHeaders(PipelineRequestHeaders headers)
    {
        var obj = new JsonObject();
        foreach (var header in headers)
            obj[header.Key] = IsSensitiveHeader(header.Key) ? Redact(header.Value) : header.Value;
        return obj;
    }

    private static JsonObject CaptureHeaders(PipelineResponseHeaders headers)
    {
        var obj = new JsonObject();
        foreach (var header in headers)
            obj[header.Key] = IsSensitiveHeader(header.Key) ? Redact(header.Value) : header.Value;
        return obj;
    }

    private static bool IsSensitiveHeader(string name) =>
        string.Equals(name, "authorization", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(name, "api-key", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(name, "x-api-key", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(name, "anthropic-api-key", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(name, "chatgpt-account-id", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(name, "x-codex-installation-id", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(name, "x-codex-turn-state", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(name, "x-codex-turn-metadata", StringComparison.OrdinalIgnoreCase);

    private static string Redact(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        return $"<redacted length={bytes.Length} sha256={hash}>";
    }

    private static string? ReadRequestBody(PipelineMessage message)
    {
        if (OpenAIResponsesRequestCompressionPipelinePolicy.TryGetOriginalBody(message, out var original, out _))
            return original?.ToString();
        if (message.Request.Content == null)
            return null;

        using var stream = new MemoryStream();
        message.Request.Content.WriteTo(stream, message.CancellationToken);
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static async ValueTask<string?> ReadRequestBodyAsync(PipelineMessage message)
    {
        if (OpenAIResponsesRequestCompressionPipelinePolicy.TryGetOriginalBody(message, out var original, out _))
            return original?.ToString();
        if (message.Request.Content == null)
            return null;

        await using var stream = new MemoryStream();
        await message.Request.Content.WriteToAsync(stream, message.CancellationToken).ConfigureAwait(false);
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static int? ReadWireBodyLength(PipelineMessage message)
    {
        if (message.Request.Content == null)
            return null;

        try
        {
            using var stream = new MemoryStream();
            message.Request.Content.WriteTo(stream, message.CancellationToken);
            return checked((int)stream.Length);
        }
        catch
        {
            return null;
        }
    }

    private static string? TryReadBufferedResponseBody(PipelineResponse response)
    {
        try
        {
            return response.Content?.ToString();
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static JsonNode? TryParseJson(string body)
    {
        try
        {
            return JsonNode.Parse(body);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static void WriteCapture(JsonObject capture, DateTimeOffset timestamp)
    {
        try
        {
            var directory = ResolveCaptureDirectory();
            Directory.CreateDirectory(directory);

            var sequence = Interlocked.Increment(ref _sequence);
            var sessionKey = SanitizeFileSegment(TracingChatClient.GetActiveSessionKey() ?? "session");
            var path = Path.Combine(
                directory,
                $"{timestamp:yyyyMMdd_HHmmss_fffffff}_{sequence:D5}_{sessionKey}.json");

            File.WriteAllText(
                path,
                capture.ToJsonString(new JsonSerializerOptions { WriteIndented = true }),
                Encoding.UTF8);
        }
        catch
        {
            // Capture must never perturb provider requests.
        }
    }

    private static string ResolveCaptureDirectory()
    {
        var configured = Environment.GetEnvironmentVariable(DirectoryEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(configured))
            return configured.Trim();

        return Path.Combine(Environment.CurrentDirectory, ".craft", "llm-http-capture");
    }

    internal static string SanitizeFileSegment(string value)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        foreach (var ch in new[] { '<', '>', ':', '"', '/', '\\', '|', '?', '*' })
            invalid.Add(ch);

        var builder = new StringBuilder(value.Length);
        foreach (var ch in value)
            builder.Append(invalid.Contains(ch) ? '_' : ch);

        return builder.Length == 0 ? "session" : builder.ToString();
    }
}
