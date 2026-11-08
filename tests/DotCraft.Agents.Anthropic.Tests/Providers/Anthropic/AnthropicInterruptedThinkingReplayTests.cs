using System.Net;
using System.Text;
using System.Text.Json;
using Anthropic.Models.Beta.Messages;
using DotCraft.Agents;
using DotCraft.Configuration;
using DotCraft.Sessions;
using Microsoft.Extensions.AI;
using Xunit;

namespace DotCraft.Tests.Agents;

public sealed class AnthropicInterruptedThinkingReplayTests
{
    [Fact]
    public async Task Steer_during_unsigned_thinking_preserves_display_but_replays_only_valid_content()
    {
        using var handler = new CaptureHandler(streaming: true, interruptFirst: true);
        using var http = new HttpClient(handler);
        using var client = new StreamingFunctionInvokingChatClient(CreateClient(http));
        var steer = new InstantSteer();
        using var guidance = StreamingGuidanceRuntimeScope.Set(steer.Context);
        var visible = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var updates = new List<ChatResponseUpdate>();

        async Task ConsumeAsync()
        {
            await foreach (var update in client.GetStreamingResponseAsync(
                               [new ChatMessage(ChatRole.User, "start")], cancellationToken: timeout.Token))
            {
                updates.Add(update);
                if (update.Contents.OfType<TextReasoningContent>().Any(content => content.Text == "partial thinking"))
                    visible.TrySetResult();
            }
        }

        var run = ConsumeAsync();
        var first = await Task.WhenAny(visible.Task, run).WaitAsync(timeout.Token);
        await first;
        steer.Submit();
        await run.WaitAsync(timeout.Token);

        Assert.True(handler.InterruptedStream!.Cancelled);
        Assert.Equal(2, handler.Requests.Count);
        var replay = Blocks(handler.Requests[1]);
        Assert.DoesNotContain(replay, block => Type(block) == "thinking");
        Assert.Contains(replay, block => Type(block) == "text" && block.GetProperty("text").GetString() == "steer");
        Assert.Contains(updates.SelectMany(update => update.Contents).OfType<TextReasoningContent>(),
            content => content.Text == "partial thinking" && string.IsNullOrEmpty(content.ProtectedData));
        Assert.Equal("done", string.Concat(updates.Select(update => update.Text)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Replayed_history_omits_unsigned_blocks_without_mutating_complete_content(bool streaming)
    {
        List<ChatMessage> history =
        [
            new(ChatRole.User, "start"),
            new(ChatRole.Assistant, [new TextReasoningContent("interrupted only")]),
            new(ChatRole.User, "continue"),
            new(ChatRole.Assistant,
            [
                new TextReasoningContent("complete") { ProtectedData = "signed" },
                new TextReasoningContent("interrupted mixed"),
                new TextContent("checking"),
                new FunctionCallContent("call_probe", "Probe", new Dictionary<string, object?>())
            ]),
            new(ChatRole.Tool, [new FunctionResultContent("call_probe", "observed")]),
            new(ChatRole.User, "finish")
        ];
        history = history.Select(message => ModelHistoryJson.Decode(ModelHistoryJson.Encode(message))).ToList();
        var mixed = history[3];
        mixed.Contents.Insert(1, new TextReasoningContent("")
        {
            ProtectedData = "redacted",
            RawRepresentation = new BetaRedactedThinkingBlock { Data = "redacted" }
        });
        mixed.Contents.Insert(2, new TextReasoningContent("raw overrides signature")
        {
            ProtectedData = "not-the-wire-signature",
            RawRepresentation = RawThinking("unfinished raw", "")
        });
        mixed.Contents.Insert(3, new AIContent { RawRepresentation = RawThinking("finished raw", "raw-signed") });
        var originals = history.Select(message => message.Contents.ToArray()).ToArray();
        using var handler = new CaptureHandler(streaming);
        using var http = new HttpClient(handler);
        using var client = CreateClient(http);

        await SendAsync(client, history, streaming);

        var request = Assert.Single(handler.Requests);
        var assistant = Assert.Single(request.GetProperty("messages").EnumerateArray(),
            message => message.GetProperty("role").GetString() == "assistant");
        var content = assistant.GetProperty("content").EnumerateArray().ToArray();
        Assert.Equal(["thinking", "redacted_thinking", "thinking", "text", "tool_use"], content.Select(Type));
        Assert.Equal("complete", content[0].GetProperty("thinking").GetString());
        Assert.Equal("signed", content[0].GetProperty("signature").GetString());
        Assert.Equal("redacted", content[1].GetProperty("data").GetString());
        Assert.Equal("finished raw", content[2].GetProperty("thinking").GetString());
        Assert.Equal("raw-signed", content[2].GetProperty("signature").GetString());
        Assert.Equal("checking", content[3].GetProperty("text").GetString());
        Assert.Equal("call_probe", content[4].GetProperty("id").GetString());
        var result = Assert.Single(Blocks(request), block => Type(block) == "tool_result");
        Assert.Equal("call_probe", result.GetProperty("tool_use_id").GetString());
        Assert.Contains("observed", result.GetProperty("content").ToString(), StringComparison.Ordinal);
        for (var i = 0; i < history.Count; i++)
            Assert.Equal(originals[i], history[i].Contents);
    }

    [Fact]
    public async Task Resolved_remote_reasoning_history_adapter_keeps_unsigned_thinking()
    {
        using var handler = new CaptureHandler(streaming: false);
        using var http = new HttpClient(handler);
        var adapter = new ModelThinkingAdapterCatalog.AnthropicMessageContentAdapterData
        {
            ReasoningHistoryBlockType = "thinking"
        };
        var adaptation = new ProviderRequestAdaptation(false, false, null, adapter);
        var runtime = Runtime() with
        {
            IsRemote = true,
            RequestAdaptation = JsonSerializer.Deserialize<ProviderRequestAdaptation>(JsonSerializer.Serialize(adaptation))
        };
        using var client = new AnthropicClientProvider(new Transport(http)).CreateChatClient(runtime);
        await SendAsync(client,
        [
            new(ChatRole.User, "start"),
            new(ChatRole.Assistant, [new TextReasoningContent("compatible reasoning"), new TextContent("answer")]),
            new(ChatRole.User, "continue")
        ], streaming: false);

        var thinking = Assert.Single(Blocks(Assert.Single(handler.Requests)), block => Type(block) == "thinking");
        Assert.Equal("compatible reasoning", thinking.GetProperty("thinking").GetString());
        Assert.Equal("", thinking.GetProperty("signature").GetString());
    }

    private static BetaContentBlockParam RawThinking(string text, string signature) => new(
        JsonSerializer.SerializeToElement(new { type = "thinking", thinking = text, signature }));

    private static EffectiveModelRuntime Runtime() => new(
        "test", "test-model", ModelProviderProtocols.Anthropic, "Test", "key", "https://provider.example", 30, 1024,
        ModelProviderCapabilities.ForProtocol(ModelProviderProtocols.Anthropic));

    private static IChatClient CreateClient(HttpClient http) => new AnthropicClientProvider(new Transport(http)).CreateChatClient(Runtime());

    private static async Task SendAsync(IChatClient client, IEnumerable<ChatMessage> history, bool streaming)
    {
        if (streaming)
        {
            await foreach (var _ in client.GetStreamingResponseAsync(history)) { }
        }
        else
            await client.GetResponseAsync(history);
    }

    private static JsonElement[] Blocks(JsonElement request) => request.GetProperty("messages").EnumerateArray()
        .SelectMany(message => message.GetProperty("content").EnumerateArray()).ToArray();

    private static string? Type(JsonElement block) => block.GetProperty("type").GetString();

    private static string Event(string type, string data) => $"event: {type}\ndata: {data}\n\n";

    private const string Message = """
        {"id":"msg_test","type":"message","role":"assistant","model":"test-model","content":[],"stop_reason":null,"usage":{"input_tokens":10,"output_tokens":0}}
        """;

    private static string CompletedStream() =>
        Event("message_start", $"{{\"type\":\"message_start\",\"message\":{Message}}}") +
        Event("content_block_start", """{"type":"content_block_start","index":0,"content_block":{"type":"text","text":""}}""") +
        Event("content_block_delta", """{"type":"content_block_delta","index":0,"delta":{"type":"text_delta","text":"done"}}""") +
        Event("content_block_stop", """{"type":"content_block_stop","index":0}""") +
        Event("message_delta", """{"type":"message_delta","delta":{"stop_reason":"end_turn","stop_sequence":null},"usage":{"output_tokens":1}}""") +
        Event("message_stop", """{"type":"message_stop"}""");

    private static string InterruptedPrefix() =>
        Event("message_start", $"{{\"type\":\"message_start\",\"message\":{Message}}}") +
        Event("content_block_start", """{"type":"content_block_start","index":0,"content_block":{"type":"thinking","thinking":"","signature":""}}""") +
        Event("content_block_delta", """{"type":"content_block_delta","index":0,"delta":{"type":"thinking_delta","thinking":"partial thinking"}}""");

    private sealed class Transport(HttpClient client) : IProviderHttpTransport
    {
        public HttpClient CreateClient(string providerId, Uri providerEndpoint) => client;
    }

    private sealed class CaptureHandler(bool streaming, bool interruptFirst = false) : HttpMessageHandler
    {
        public List<JsonElement> Requests { get; } = [];
        public InterruptedStream? InterruptedStream { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(JsonSerializer.Deserialize<JsonElement>(await request.Content!.ReadAsStringAsync(cancellationToken)));
            HttpContent content;
            if (interruptFirst && Requests.Count == 1)
            {
                InterruptedStream = new InterruptedStream(InterruptedPrefix());
                content = new StreamContent(InterruptedStream);
                content.Headers.ContentType = new("text/event-stream");
            }
            else
            {
                var response = streaming ? CompletedStream() : """
                    {"id":"msg_test","type":"message","role":"assistant","model":"test-model","content":[{"type":"text","text":"done"}],"stop_reason":"end_turn","usage":{"input_tokens":10,"output_tokens":1}}
                    """;
                content = new StringContent(response, Encoding.UTF8, streaming ? "text/event-stream" : "application/json");
            }
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
        }
    }

    private sealed class InterruptedStream(string prefix) : Stream
    {
        private readonly byte[] _prefix = Encoding.UTF8.GetBytes(prefix);
        private readonly TaskCompletionSource _pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _position;
        public bool Cancelled { get; private set; }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => _prefix.Length;
        public override long Position { get => _position; set => throw new NotSupportedException(); }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_position < _prefix.Length)
            {
                var count = Math.Min(buffer.Length, _prefix.Length - _position);
                _prefix.AsMemory(_position, count).CopyTo(buffer);
                _position += count;
                return count;
            }
            try
            {
                await _pending.Task.WaitAsync(cancellationToken);
                return 0;
            }
            catch (OperationCanceledException)
            {
                Cancelled = true;
                throw;
            }
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class InstantSteer
    {
        private TaskCompletionSource _pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private bool _submitted;

        public StreamingGuidanceRuntimeContext Context => new()
        {
            DrainAsync = (boundary, _) =>
            {
                IReadOnlyList<ChatMessage> messages = [];
                if (boundary != StreamingGuidanceBoundary.TurnStart && _submitted)
                {
                    messages = [new ChatMessage(ChatRole.User, "steer")];
                    _submitted = false;
                    _pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
                }
                return Task.FromResult(messages);
            },
            HasPendingGuidanceAsync = _ => Task.FromResult(_submitted),
            WaitForInstantInterruptAsync = ct => _pending.Task.WaitAsync(ct)
        };

        public void Submit()
        {
            _submitted = true;
            _pending.TrySetResult();
        }
    }
}
