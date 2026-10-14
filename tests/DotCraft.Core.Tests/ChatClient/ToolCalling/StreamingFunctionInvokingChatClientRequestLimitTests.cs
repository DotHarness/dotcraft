using DotCraft.Agents;
using Microsoft.Extensions.AI;
using Xunit;

namespace DotCraft.Tests.Agents;

public sealed class StreamingFunctionInvokingChatClientRequestLimitTests
{
    [Fact]
    public async Task ToolLoop_StopsAtTheModelRequestLimitAndEndsNormally()
    {
        var inner = new ToolCallingChatClient();
        var client = new StreamingFunctionInvokingChatClient(inner);
        var tool = AIFunctionFactory.Create(() => "still going", name: "GetStatus");

        var results = 0;
        using (ModelRequestLimitScope.Set(3))
        {
            await foreach (var update in client.GetStreamingResponseAsync(
                               [new ChatMessage(ChatRole.User, "start")],
                               new ChatOptions { Tools = [tool] }))
            {
                results += update.Contents.OfType<FunctionResultContent>().Count();
            }
        }

        Assert.Equal(3, inner.Requests);
        Assert.Equal(3, results);
    }

    private sealed class ToolCallingChatClient : IChatClient
    {
        public int Requests { get; private set; }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Requests++;
            yield return new ChatResponseUpdate(ChatRole.Assistant,
                [new FunctionCallContent($"call-{Requests}", "GetStatus", new Dictionary<string, object?>())]);
            await Task.CompletedTask;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
