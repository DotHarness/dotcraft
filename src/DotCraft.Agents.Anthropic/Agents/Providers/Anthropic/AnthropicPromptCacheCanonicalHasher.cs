using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace DotCraft.Agents;

internal sealed class AnthropicPromptCacheCanonicalHasher : IDisposable
{
    private const int CharChunkSize = 4096;

    private readonly IncrementalHash _hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    private readonly Encoder _encoder = Encoding.UTF8.GetEncoder();
    private readonly byte[] _bytes = new byte[Encoding.UTF8.GetMaxByteCount(CharChunkSize)];

    public string CurrentHash() => Convert.ToHexString(_hash.GetCurrentHash());

    public void AppendMessageBoundary(ChatMessage message)
    {
        Append("\nmessage:");
        Append(message.Role.Value);
        Append(":");
        Append(message.AuthorName ?? string.Empty);
    }

    public void AppendContent(AIContent content)
    {
        Append("\ncontent:");
        Append(content.GetType().FullName ?? string.Empty);
        Append(":");

        switch (content)
        {
            case TextContent text:
                AppendString(text.Text);
                break;
            case FunctionCallContent call:
                AppendString(call.CallId);
                AppendString(call.Name);
                AppendCanonicalObject(call.Arguments);
                break;
            case FunctionResultContent result:
                AppendString(result.CallId);
                if (AnthropicPromptCachingChatClient.TryGetToolResultWireText(result, out var toolResultText))
                    AppendString(toolResultText);
                else
                    AppendCanonicalObject(result.Result);
                if (result.Exception != null)
                    AppendString(result.Exception.GetType().FullName + ":" + result.Exception.Message);
                break;
            case DataContent data:
                AppendString(data.MediaType);
                AppendInt32(data.Data.Length);
                break;
            default:
                AppendString(content.ToString());
                break;
        }
    }

    public void Dispose() => _hash.Dispose();

    private void AppendString(string? value)
    {
        value ??= string.Empty;
        AppendInt32(value.Length);
        Append(":");
        Append(value);
        Append(";");
    }

    private void AppendCanonicalObject(object? value)
    {
        if (value == null)
        {
            Append("null;");
            return;
        }

        if (value is string text)
        {
            AppendString(text);
            return;
        }

        string serialized;
        try
        {
            serialized = JsonSerializer.Serialize(value);
        }
        catch (NotSupportedException)
        {
            AppendString(value.ToString());
            return;
        }

        Append(serialized);
        Append(";");
    }

    private void AppendInt32(int value)
    {
        Span<char> digits = stackalloc char[11];
        value.TryFormat(digits, out var written);
        Append(digits[..written]);
    }

    private void Append(ReadOnlySpan<char> text)
    {
        while (!text.IsEmpty)
        {
            var chunk = text[..Math.Min(text.Length, CharChunkSize)];
            var count = _encoder.GetBytes(chunk, _bytes, flush: false);
            _hash.AppendData(_bytes, 0, count);
            text = text[chunk.Length..];
        }
    }
}
