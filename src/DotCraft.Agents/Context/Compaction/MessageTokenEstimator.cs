using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace DotCraft.Context.Compaction;

/// <summary>
/// Rough token estimator for <see cref="ChatMessage"/> sequences.
/// The estimate pads by 4/3 so callers can use it as a conservative upper bound.
/// </summary>
public static class MessageTokenEstimator
{
    private static readonly JsonSerializerOptions ContentJsonOptions = new(JsonSerializerDefaults.Web);
    /// <summary>
    /// Approximate UTF-8 bytes-per-token ratio used by the rough estimator.
    /// The 4/3 pad applied in <see cref="Estimate"/> offsets the
    /// typical underestimate for tokenizer-heavy payloads.
    /// </summary>
    private const double BytesPerToken = 4.0;

    /// <summary>
    /// Fixed token cost for an image or document content part.
    /// </summary>
    private const int ImageTokenCost = 2000;

    private const int MediaReplacementBytes = ImageTokenCost * 4;

    /// <summary>
    /// Returns the estimated token count for a single content block.
    /// </summary>
    public static int EstimateContent(AIContent content)
    {
        return content switch
        {
            DataContent dc when IsImageOrDocument(dc.MediaType) => ImageTokenCost,
            UriContent uc when IsImageOrDocument(uc.MediaType) => ImageTokenCost,
            _ => TokensFromBytes(EstimateContentModelVisibleBytes(content)),
        };
    }

    /// <summary>
    /// Estimates the token cost of a single <see cref="ChatMessage"/>.
    /// </summary>
    public static int EstimateMessage(ChatMessage message)
    {
        return TokensFromBytes(EstimateModelVisibleBytes(message));
    }

    /// <summary>
    /// Estimates the token cost of a message sequence with a 4/3 safety pad.
    /// </summary>
    public static int Estimate(IReadOnlyList<ChatMessage> messages)
    {
        var total = 0L;
        foreach (var message in messages)
            total += EstimateModelVisibleBytes(message);

        return ApplySafetyPad(TokensFromBytes(total));
    }

    /// <summary>
    /// Estimates only the messages appended after a provider usage anchor. The
    /// provider already counted the prefix, so avoid re-padding the delta.
    /// </summary>
    public static int EstimateDelta(IReadOnlyList<ChatMessage> messages)
        => EstimateDelta(messages, startIndex: 0, count: messages.Count);

    /// <summary>
    /// Estimates a contiguous message range without materializing a sliced list.
    /// </summary>
    internal static int EstimateDelta(IReadOnlyList<ChatMessage> messages, int startIndex, int count)
    {
        ArgumentNullException.ThrowIfNull(messages);
        if (startIndex < 0 || startIndex > messages.Count)
            throw new ArgumentOutOfRangeException(nameof(startIndex));
        if (count < 0 || count > messages.Count - startIndex)
            throw new ArgumentOutOfRangeException(nameof(count));

        var total = 0L;
        var endIndex = startIndex + count;
        for (var i = startIndex; i < endIndex; i++)
            total += EstimateModelVisibleBytes(messages[i]);

        return TokensFromBytes(total);
    }

    internal static long EstimateModelVisibleBytes(ChatMessage message)
    {
        long total = Utf8ByteCount("role") + Utf8ByteCount(message.Role.ToString()) + 8;
        if (!string.IsNullOrEmpty(message.AuthorName))
            total += Utf8ByteCount("author") + Utf8ByteCount(message.AuthorName) + 8;
        if (!string.IsNullOrEmpty(message.MessageId))
            total += Utf8ByteCount("message_id") + Utf8ByteCount(message.MessageId) + 8;

        total += Utf8ByteCount("content") + 4;
        foreach (var content in message.Contents)
            total += EstimateContentModelVisibleBytes(content) + 1;

        return total;
    }

    internal static string ComputePrefixFingerprint(
        IReadOnlyList<ChatMessage> messages,
        int messageCount)
    {
        if (messageCount < 0 || messageCount > messages.Count)
            throw new ArgumentOutOfRangeException(nameof(messageCount));

        using var sha = SHA256.Create();
        for (var i = 0; i < messageCount; i++)
        {
            var canonical = CanonicalizeMessage(messages[i]);
            var bytes = JsonSerializer.SerializeToUtf8Bytes(canonical);
            sha.TransformBlock(bytes, 0, bytes.Length, null, 0);
            sha.TransformBlock([0], 0, 1, null, 0);
        }

        sha.TransformFinalBlock([], 0, 0);
        return Convert.ToHexString(sha.Hash!).ToLowerInvariant();
    }

    /// <summary>
    /// Unpadded byte-based token estimate for a raw string. Exposed so the
    /// microcompact path can compare content deltas without the 4/3 pad.
    /// </summary>
    public static int RoughTokenCount(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return 0;

        return (int)Math.Ceiling(Encoding.UTF8.GetByteCount(text) / BytesPerToken);
    }

    private static int TokensFromBytes(long bytes)
    {
        if (bytes <= 0)
            return 0;

        return (int)Math.Min(int.MaxValue, (bytes + 3) / 4);
    }

    private static int ApplySafetyPad(int tokens) =>
        (int)Math.Min(int.MaxValue, Math.Ceiling(tokens * 4.0 / 3.0));

    private static long EstimateContentModelVisibleBytes(AIContent content)
    {
        return content switch
        {
            TextContent tc => Utf8ByteCount(tc.Text),
            DataContent dc when IsImageOrDocument(dc.MediaType) => EstimateMediaBytes(dc.MediaType),
            UriContent uc when IsImageOrDocument(uc.MediaType) => EstimateMediaBytes(uc.MediaType),
            FunctionCallContent fc => Utf8ByteCount(SerializeFunctionCall(fc)),
            FunctionResultContent fr => EstimateFunctionResultBytes(fr),
            _ => Utf8ByteCount(SerializeUnknownContent(content)),
        };
    }

    private static long EstimateMediaBytes(string? mediaType)
    {
        return Utf8ByteCount("media")
               + Utf8ByteCount(mediaType)
               + MediaReplacementBytes;
    }

    private static int Utf8ByteCount(string? text)
    {
        return string.IsNullOrEmpty(text) ? 0 : Encoding.UTF8.GetByteCount(text);
    }

    private static bool IsImageOrDocument(string? mediaType)
    {
        if (string.IsNullOrEmpty(mediaType))
            return false;

        return mediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)
               || mediaType.Equals("application/pdf", StringComparison.OrdinalIgnoreCase);
    }

    private static string SerializeFunctionCall(FunctionCallContent content)
    {
        return SerializeForEstimate(new
        {
            type = "tool_use",
            id = content.CallId,
            name = content.Name,
            input = content.Arguments
        });
    }

    private static long EstimateFunctionResultBytes(FunctionResultContent content)
    {
        long total = Utf8ByteCount("tool_result")
                     + Utf8ByteCount("tool_use_id")
                     + Utf8ByteCount(content.CallId)
                     + Utf8ByteCount("content")
                     + 24;

        if (content.Result is string text)
            return total + Utf8ByteCount(text);

        if (TryGetResultContentItems(content.Result, out var items))
        {
            foreach (var item in items)
                total += EstimateContentModelVisibleBytes(item) + 1;
            return total;
        }

        return total + Utf8ByteCount(SerializeForEstimate(content.Result));
    }

    private static string SerializeUnknownContent(AIContent content)
    {
        try
        {
            return JsonSerializer.Serialize(content);
        }
        catch
        {
            return content.ToString() ?? string.Empty;
        }
    }

    private static string SerializeForEstimate(object? value)
    {
        if (value is null)
            return string.Empty;

        try
        {
            return JsonSerializer.Serialize(value);
        }
        catch
        {
            return value.ToString() ?? string.Empty;
        }
    }

    private static object CanonicalizeMessage(ChatMessage message)
    {
        return new
        {
            role = message.Role.ToString(),
            author = message.AuthorName,
            message_id = message.MessageId,
            content = message.Contents.Select(CanonicalizeContent).ToArray()
        };
    }

    private static object CanonicalizeContent(AIContent content)
    {
        return content switch
        {
            TextContent tc => new { type = "text", text = tc.Text },
            DataContent dc => new
            {
                type = IsImageOrDocument(dc.MediaType) ? "media" : "data",
                media_type = dc.MediaType,
                length = dc.Data.Length,
                hash = Convert.ToHexString(SHA256.HashData(dc.Data.ToArray())).ToLowerInvariant()
            },
            UriContent uc => new
            {
                type = IsImageOrDocument(uc.MediaType) ? "media_uri" : "uri",
                media_type = uc.MediaType,
                uri = uc.Uri?.ToString()
            },
            FunctionCallContent fc => new
            {
                type = "tool_use",
                id = fc.CallId,
                name = fc.Name,
                input = SerializeForEstimate(fc.Arguments)
            },
            FunctionResultContent fr => new
            {
                type = "tool_result",
                tool_use_id = fr.CallId,
                content = CanonicalizeFunctionResultValue(fr.Result)
            },
            _ => new { type = content.GetType().FullName, content = SerializeUnknownContent(content) }
        };
    }

    private static object? CanonicalizeFunctionResultValue(object? value)
    {
        if (value is null)
            return null;

        if (value is string text)
            return text;

        if (TryGetResultContentItems(value, out var items))
            return items.Select(CanonicalizeContent).ToArray();

        return SerializeForEstimate(value);
    }

    private static bool TryGetResultContentItems(
        object? value,
        out IReadOnlyList<AIContent> items)
    {
        if (value is IReadOnlyList<AIContent> readOnlyItems)
        {
            items = readOnlyItems;
            return true;
        }

        if (value is IEnumerable<AIContent> enumerableItems)
        {
            items = enumerableItems.ToArray();
            return true;
        }

        if (value is JsonElement { ValueKind: JsonValueKind.Array } json)
        {
            try
            {
                items = json.Deserialize<List<AIContent>>(ContentJsonOptions) ?? [];
                return true;
            }
            catch (JsonException)
            {
            }
            catch (NotSupportedException)
            {
            }
        }

        items = [];
        return false;
    }
}
