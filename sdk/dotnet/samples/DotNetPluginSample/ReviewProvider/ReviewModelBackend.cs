using System.Runtime.CompilerServices;
using System.Text.Json;
using DotCraft.Agents;
using DotCraft.Configuration;
using DotCraft.Contributions;
using Microsoft.Extensions.AI;

namespace Acme.ReviewCore;

internal sealed class ReviewModelBackend : IModelBackendSource, IModelBackend
{
    public IModelBackend Backend => this;
    public ModelBackendDescriptor Descriptor => new(
        "acme.review-backend", "Local review demo", "review-native", ["review-native"],
        [new("demo", "Local demo credential", "apiKey")], new ModelProviderCapabilities());

    public IModelProvider CreateProvider(ModelBackendContext context) => new Provider(context.Credentials);

    private sealed class Provider(IProviderCredentialStore credentials)
        : IModelProvider, IModelCatalogProvider, IProviderAuthenticator
    {
        public IReadOnlyCollection<string> Protocols => ["review-native"];
        public IChatClient CreateChatClient(EffectiveModelRuntime runtime) => new Client();
        public object? GetService(Type type, object? key = null) => key is null && type.IsInstanceOfType(this) ? this : null;
        public Task<ModelCatalogResult> FetchModelsAsync(EffectiveModelRuntime runtime, CancellationToken ct) =>
            Task.FromResult(new ModelCatalogResult { Success = true, Models = [new() { Id = "review-echo" }] });
        public async Task<ProviderAuthenticationStatus> GetStatusAsync(CancellationToken ct) =>
            new(await credentials.ReadAsync(ct) is not null);
        public async Task<ProviderAuthenticationStatus> LoginAsync(string method, IProviderLoginInteraction interaction, CancellationToken ct)
        {
            await interaction.NotifyAsync(new("info", "This offline demo stores a test credential and contacts no service."), ct);
            var value = await interaction.PromptAsync(new("secret", "Enter any demo credential"), ct);
            await credentials.ModifyAsync((_, _) => Task.FromResult<JsonElement?>(JsonSerializer.SerializeToElement(value)), ct);
            return new ProviderAuthenticationStatus(true, DisplayName: "Local demo");
        }
        public Task LogoutAsync(CancellationToken ct) => credentials.DeleteAsync(ct);
    }

    private sealed class Client : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages,
            ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, Reply(messages))));

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Yield();
            yield return new ChatResponseUpdate(ChatRole.Assistant, Reply(messages)) { FinishReason = ChatFinishReason.Stop };
        }
        private static string Reply(IEnumerable<ChatMessage> messages) =>
            "review-backend:" + messages.LastOrDefault(message => message.Role == ChatRole.User)?.Text;
        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }
}
