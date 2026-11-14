using System.Runtime.CompilerServices;
using DotCraft.Agents;
using DotCraft.Configuration;
using DotCraft.Sessions;
using Microsoft.Extensions.AI;

namespace DotCraft.CodexGateway.Tests;

internal sealed class GatewayTestModel(string name) : IModelProvider, IModelCatalogProvider
{
    public const string ModelId = "gateway-fixture-model";
    public const int LargeReplyLength = 5 * 1024 * 1024;
    public TaskCompletionSource Blocked { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public IReadOnlyCollection<string> Protocols => [ModelProviderProtocols.OpenAIChatCompletions];
    public IChatClient CreateChatClient(EffectiveModelRuntime runtime) => new Client(this, name);

    public Task<ModelCatalogResult> FetchModelsAsync(EffectiveModelRuntime runtime, CancellationToken cancellationToken) =>
        Task.FromResult(new ModelCatalogResult
        {
            Success = true, Protocol = runtime.Protocol, ProviderId = runtime.ProviderId,
            Models = [new() { Id = ModelId, DisplayName = "Gateway fixture model", InputModalities = ["text"], ContextWindow = 8_000_000 }]
        });

    private sealed class Client(GatewayTestModel owner, string name) : IChatClient
    {
        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var history = messages.ToArray();
            var prompt = history.Last(message => message.Role == ChatRole.User)
                .Contents.OfType<TextContent>().First().Text;
            if (prompt == "hold")
            {
                owner.Blocked.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            var lastInput = Array.FindLastIndex(history, message => message.Role == ChatRole.User);
            var result = history.Skip(lastInput + 1).SelectMany(message => message.Contents)
                .OfType<FunctionResultContent>().LastOrDefault();
            if (prompt is "question" or "large-history" && result is null)
            {
                if (prompt == "large-history")
                    yield return new ChatResponseUpdate(ChatRole.Assistant, new string('x', LargeReplyLength));
                yield return new ChatResponseUpdate
                {
                    Role = ChatRole.Assistant, FinishReason = ChatFinishReason.ToolCalls,
                    Contents = [new FunctionCallContent("question-call", "RequestUserInput",
                        new Dictionary<string, object?>
                        {
                            ["questions"] = new[]
                            {
                                new RequestUserInputQuestion
                                {
                                    Id = "choice", Header = "Choice", Question = "Which output?",
                                    Options =
                                    [
                                        new() { Label = "A", Description = "First output" },
                                        new() { Label = "B", Description = "Second output" }
                                    ]
                                }
                            }
                        })]
                };
                yield break;
            }
            yield return new ChatResponseUpdate(ChatRole.Assistant,
                name + ":" + (result is null ? prompt : JsonResult(result.Result)))
            {
                FinishReason = ChatFinishReason.Stop
            };
        }

        private static string JsonResult(object? result) => result as string ?? System.Text.Json.JsonSerializer.Serialize(result);

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }
}
