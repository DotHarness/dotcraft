using System.ClientModel.Primitives;
using System.Text;
using System.Text.Json;
using DotCraft.Agents;
using Microsoft.Extensions.AI;
using OpenAI.Responses;
using Xunit;

#pragma warning disable OPENAI001, MAAI001, MEAI001

namespace DotCraft.Tests.Agents;

public sealed class OpenAIResponsesLiteChatClientTests
{
    [Fact]
    public void RequestMapper_EmitsResponsesLiteContract()
    {
        var options = ResponsesToolSearchMapper.CreateResponseOptions(
            "gpt-test",
            [
                new ChatMessage(ChatRole.System, "developer guidance"),
                new ChatMessage(ChatRole.User, "hello")
            ],
            new ChatOptions
            {
                Reasoning = new ReasoningOptions { Effort = ReasoningEffort.Medium },
                Tools =
                [
                    AIFunctionFactory.Create(
                        (string? value) => value ?? string.Empty,
                        name: "nullable",
                        description: "Accept a nullable value.")
                ],
                ToolMode = ChatToolMode.Auto,
                AllowMultipleToolCalls = true,
                MaxOutputTokens = 123
            });
#pragma warning disable SCME0001
        options.Patch.Set(
            "$.input[0].content[1]"u8,
            BinaryData.FromString("""{"type":"input_image","image_url":"data:image/png;base64,AA==","detail":"high"}"""));
        options.Patch.Set(
            "$.tools"u8,
            BinaryData.FromString("""[{"type":"function","name":"nullable","parameters":{"type":"object","properties":{"value":{"type":["string","null"]}}}}]"""));
#pragma warning restore SCME0001

        var body = OpenAIResponsesLiteRequestMapper.BuildWireBody(options, "install-1");
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;

        Assert.Equal("gpt-test", root.GetProperty("model").GetString());
        Assert.True(root.GetProperty("stream").GetBoolean());
        Assert.False(root.GetProperty("store").GetBoolean());
        Assert.Equal("medium", root.GetProperty("reasoning").GetProperty("effort").GetString());
        Assert.Equal("all_turns", root.GetProperty("reasoning").GetProperty("context").GetString());
        Assert.False(root.GetProperty("parallel_tool_calls").GetBoolean());
        Assert.Equal("auto", root.GetProperty("tool_choice").GetString());
        Assert.False(root.TryGetProperty("instructions", out _));
        Assert.False(root.TryGetProperty("tools", out _));
        Assert.False(root.TryGetProperty("max_output_tokens", out _));
        Assert.Contains(
            root.GetProperty("include").EnumerateArray(),
            value => value.GetString() == "reasoning.encrypted_content");
        var input = root.GetProperty("input");
        Assert.Equal("additional_tools", input[0].GetProperty("type").GetString());
        Assert.Equal("developer", input[0].GetProperty("role").GetString());
        Assert.Equal(1, input[0].GetProperty("tools").GetArrayLength());
        var functions = input[0].GetProperty("tools")[0];
        Assert.Equal("namespace", functions.GetProperty("type").GetString());
        Assert.Equal("functions", functions.GetProperty("name").GetString());
        Assert.Equal(string.Empty, functions.GetProperty("description").GetString());
        Assert.Equal("nullable", functions.GetProperty("tools")[0].GetProperty("name").GetString());
        Assert.Equal("message", input[1].GetProperty("type").GetString());
        Assert.Equal("developer guidance", input[1].GetProperty("content")[0].GetProperty("text").GetString());
        Assert.Equal("message", input[2].GetProperty("type").GetString());
        Assert.False(input[2].GetProperty("content")[1].TryGetProperty("detail", out _));
        Assert.Equal(
            "install-1",
            root.GetProperty("client_metadata").GetProperty("x-codex-installation-id").GetString());
    }

    [Fact]
    public async Task LiteClient_PreservesToolChoiceModes()
    {
        var cases = new (ChatToolMode Mode, string ExpectedJson)[]
        {
            (ChatToolMode.None, "\"none\""),
            (ChatToolMode.Auto, "\"auto\""),
            (ChatToolMode.RequireAny, "\"required\""),
            (ChatToolMode.RequireSpecific("lookup"), """{"type":"function","name":"lookup"}""")
        };

        foreach (var (mode, expectedJson) in cases)
        {
            var transport = new EmptyTransport();
            using var client = new OpenAIResponsesLiteChatClient(
                new ResponsesClient("sk-test"),
                "gpt-test",
                new TestChatClient(),
                transport,
                "install-1");

            await foreach (var _ in client.GetStreamingResponseAsync(
                               [new ChatMessage(ChatRole.User, "hello")],
                               new ChatOptions
                               {
                                   Tools = [CreateLookupTool()],
                                   ToolMode = mode
                               }))
            {
            }

            using var document = JsonDocument.Parse(Assert.Single(transport.WireBodies));
            Assert.Equal(expectedJson, document.RootElement.GetProperty("tool_choice").GetRawText());
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LiteClient_ForcesParallelToolCallsOff(bool allowMultipleToolCalls)
    {
        var transport = new EmptyTransport();
        using var client = new OpenAIResponsesLiteChatClient(
            new ResponsesClient("sk-test"),
            "gpt-test",
            new TestChatClient(),
            transport,
            "install-1");

        await foreach (var _ in client.GetStreamingResponseAsync(
                           [new ChatMessage(ChatRole.User, "hello")],
                           new ChatOptions
                           {
                               Tools = [CreateLookupTool()],
                               AllowMultipleToolCalls = allowMultipleToolCalls
                           }))
        {
        }

        using var document = JsonDocument.Parse(Assert.Single(transport.WireBodies));
        Assert.False(document.RootElement.GetProperty("parallel_tool_calls").GetBoolean());
    }

    [Fact]
    public async Task LiteClient_DoesNotAddToolControlsWithoutTools()
    {
        var transport = new EmptyTransport();
        using var client = new OpenAIResponsesLiteChatClient(
            new ResponsesClient("sk-test"),
            "gpt-test",
            new TestChatClient(),
            transport,
            "install-1");

        await foreach (var _ in client.GetStreamingResponseAsync(
                           [new ChatMessage(ChatRole.User, "hello")],
                           new ChatOptions
                           {
                               ToolMode = ChatToolMode.None,
                               AllowMultipleToolCalls = true
                           }))
        {
        }

        using var document = JsonDocument.Parse(Assert.Single(transport.WireBodies));
        Assert.False(document.RootElement.TryGetProperty("tool_choice", out _));
        Assert.False(document.RootElement.TryGetProperty("parallel_tool_calls", out _));
    }

    [Fact]
    public void RequestMapper_PreservesDeveloperGuidanceAsInputItem()
    {
        var options = ResponsesToolSearchMapper.CreateResponseOptions(
            "gpt-test",
            [
                new ChatMessage(ChatRole.System, "stable base"),
                new ChatMessage(new ChatRole("developer"), "subagent guidance"),
                new ChatMessage(ChatRole.User, "task")
            ],
            new ChatOptions());

        using var document = JsonDocument.Parse(
            OpenAIResponsesLiteRequestMapper.BuildWireBody(options, "install-1"));
        var input = document.RootElement.GetProperty("input");

        Assert.Equal("stable base", input[1].GetProperty("content")[0].GetProperty("text").GetString());
        Assert.Equal("developer", input[2].GetProperty("role").GetString());
        Assert.Equal("subagent guidance", input[2].GetProperty("content")[0].GetProperty("text").GetString());
        Assert.Equal("user", input[3].GetProperty("role").GetString());
    }

    [Fact]
    public async Task SseParser_HandlesCommentsFragmentedReadsAndTerminalError()
    {
        var payload = """
            : heartbeat

            data: {"type":"response.output_text.delta","sequence_number":1,"item_id":"msg_1","output_index":0,"content_index":0,"delta":"hé","logprobs":[]}

            data: {"type":"error","sequence_number":2,"code":"server_error","message":"boom","param":null}

            """;
        await using var stream = new FragmentedReadStream(Encoding.UTF8.GetBytes(payload), maxReadSize: 3);

        var updates = await CollectAsync(OpenAIResponsesLiteTransport.ParseSseUpdatesAsync(stream));

        var delta = Assert.IsType<StreamingResponseOutputTextDeltaUpdate>(updates[0]);
        Assert.Equal("hé", delta.Delta);
        var error = Assert.IsType<StreamingResponseErrorUpdate>(updates[1]);
        Assert.Equal("server_error", error.Code);
        Assert.Equal("boom", error.Message);
    }

    [Fact]
    public async Task StreamingResponse_TreatsProviderErrorAsRetryableStreamFailure()
    {
        var payload = """
            data: {"type":"error","sequence_number":1,"code":"server_error","message":"boom","param":null}

            """;
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(payload));

        var exception = await Assert.ThrowsAsync<IOException>(async () =>
            await CollectAsync(OpenAIResponsesLiteTransport.ReadStreamingResponseAsync(stream)));

        Assert.Contains("server_error", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StreamingResponse_TreatsIncompleteAsRetryableStreamFailure()
    {
        var payload = """
            data: {"type":"response.incomplete","sequence_number":1,"response":{"id":"resp-incomplete","object":"response","created_at":0,"model":"gpt-test","status":"incomplete","incomplete_details":{"reason":"max_output_tokens"},"usage":{"input_tokens":10,"output_tokens":4,"total_tokens":14}}}

            """;
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(payload));

        var exception = await Assert.ThrowsAsync<IOException>(async () =>
            await CollectAsync(OpenAIResponsesLiteTransport.ReadStreamingResponseAsync(stream)));

        Assert.Contains("response.incomplete", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StreamingResponse_RejectsEofBeforeTerminalEvent()
    {
        var payload = """
            data: {"type":"response.output_text.delta","sequence_number":1,"item_id":"msg_1","output_index":0,"content_index":0,"delta":"partial","logprobs":[]}

            """;
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(payload));

        var exception = await Assert.ThrowsAsync<IOException>(async () =>
            await CollectAsync(OpenAIResponsesLiteTransport.ReadStreamingResponseAsync(stream)));

        Assert.Contains("before a terminal event", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void LiteClient_ExposesProviderHistoryBridgeThroughExistingAdapter()
    {
        var responsesClient = new ResponsesClient("sk-test");
        using var client = new OpenAIResponsesLiteChatClient(
            responsesClient,
            "gpt-test",
            new TestChatClient(),
            new EmptyTransport(),
            "install-1");

        Assert.Same(client, client.GetService(typeof(OpenAIResponsesLiteChatClient)));
        Assert.Same(
            OpenAIResponsesProviderHistoryBridge.Instance,
            client.GetService(typeof(IProviderConversationHistoryBridge)));
    }

    [Fact]
    public async Task LiteClient_SendsOnlyTheMappedWireBody()
    {
        var responsesClient = new ResponsesClient("sk-test");
        var transport = new EmptyTransport();
        using var client = new OpenAIResponsesLiteChatClient(
            responsesClient,
            "gpt-test",
            new TestChatClient(),
            transport,
            "install-1");

        await foreach (var _ in client.GetStreamingResponseAsync([
                           new ChatMessage(ChatRole.User, "hello")
                       ]))
        {
        }

        var wireBody = Assert.Single(transport.WireBodies);
        using var document = JsonDocument.Parse(wireBody);
        Assert.Equal("additional_tools", document.RootElement.GetProperty("input")[0].GetProperty("type").GetString());
        Assert.False(document.RootElement.TryGetProperty("tools", out _));
    }

    private static async Task<List<StreamingResponseUpdate>> CollectAsync(
        IAsyncEnumerable<StreamingResponseUpdate> updates)
    {
        var result = new List<StreamingResponseUpdate>();
        await foreach (var update in updates)
            result.Add(update);
        return result;
    }

    private static AIFunction CreateLookupTool() =>
        AIFunctionFactory.Create(
            (string value) => value,
            name: "lookup",
            description: "Look up a value.");

    private sealed class EmptyTransport : IResponsesLiteTransport
    {
        public List<BinaryData> WireBodies { get; } = [];

        public async IAsyncEnumerable<StreamingResponseUpdate> CreateResponseStreamingAsync(
            BinaryData wireBody,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            Assert.False(wireBody.ToMemory().IsEmpty);
            WireBodies.Add(wireBody);
            await Task.CompletedTask;
            yield break;
        }
    }

    private sealed class TestChatClient : IChatClient
    {
        public ChatClientMetadata Metadata { get; } = new("test");

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ChatResponse([]));

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield break;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) =>
            serviceType == typeof(ChatClientMetadata) ? Metadata : null;

        public void Dispose()
        {
        }
    }

    private sealed class FragmentedReadStream(byte[] data, int maxReadSize) : Stream
    {
        private readonly MemoryStream _inner = new(data);

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => _inner.Length;
        public override long Position { get => _inner.Position; set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count) =>
            _inner.Read(buffer, offset, Math.Min(count, maxReadSize));

        public override int Read(Span<byte> buffer) => _inner.Read(buffer[..Math.Min(buffer.Length, maxReadSize)]);

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default) =>
            _inner.ReadAsync(buffer[..Math.Min(buffer.Length, maxReadSize)], cancellationToken);

        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _inner.Dispose();
            base.Dispose(disposing);
        }

        public override async ValueTask DisposeAsync()
        {
            await _inner.DisposeAsync();
            await base.DisposeAsync();
        }
    }
}
