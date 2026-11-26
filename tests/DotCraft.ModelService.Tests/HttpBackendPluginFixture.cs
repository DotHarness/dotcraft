namespace DotCraft.ModelService.Tests;

internal static class HttpBackendPluginFixture
{
    public const string Id = "test-model-http";

    public const string Settings = """
        {"fields":[{"key":"model","type":"text","defaultValue":"default-model"}]}
        """;

    public const string Source = """
        using System;
        using System.Collections.Generic;
        using System.Text.Json;
        using System.Threading;
        using System.Threading.Tasks;
        using DotCraft.Agents;
        using DotCraft.Configuration;
        using DotCraft.Contributions;
        using DotCraft.Plugins;
        using DotCraft.Tools;
        using Microsoft.Extensions.AI;

        namespace HttpFixture;

        public sealed class Plugin : IDotCraftPlugin
        {
            public ValueTask ActivateAsync(IPluginActivationContext context, CancellationToken cancellationToken)
            {
                context.Contributions.Add<IModelBackendSource>(new ModelBackend(context.Settings.GetProperty("model").GetString()!));
                context.Contributions.Add<IProviderUsageSource>(new NativeUsage());
                context.Contributions.Add<IToolSource>(new UnusedToolSource());
                return ValueTask.CompletedTask;
            }
        }

        internal sealed class ModelBackend(string model) : IModelBackendSource, IModelBackend
        {
            public IModelBackend Backend => this;
            public ModelBackendDescriptor Descriptor { get; } = new("test-model-http", "HTTP fixture", "openai-chat-completions",
                ["openai-chat-completions"], [new("vault", "Fixture credential", "apiKey")], new());
            public IModelProvider CreateProvider(ModelBackendContext context) => new Provider(context.Credentials, model);
        }

        internal sealed class Provider(IProviderCredentialStore credentials, string model) :
            IModelProvider, IProviderNativeHttpRouter, IModelCatalogProvider, IProviderAuthenticator, IProviderUsageReader
        {
            public IReadOnlyCollection<string> Protocols => ["openai-chat-completions"];
            public IChatClient CreateChatClient(EffectiveModelRuntime runtime) => throw new NotSupportedException();

            public ProviderHttpRoute ResolveRoute(EffectiveModelRuntime runtime) =>
                runtime.Model == model ? new("openai-chat-completions", "https://fixture.invalid/native/v1", model)
                    : throw new ArgumentException("Unknown model.");

            public async Task<IReadOnlyDictionary<string, string>> AuthenticateAsync(EffectiveModelRuntime runtime, CancellationToken ct) =>
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["x-fixture-key"] = await ReadKeyAsync(ct) ??
                        throw new ProviderAuthenticationException("FixtureLoginRequired", "The fixture requires login.")
                };

            public Task<ModelCatalogResult> FetchModelsAsync(EffectiveModelRuntime runtime, CancellationToken ct) =>
                Task.FromResult(new ModelCatalogResult
                {
                    Success = true,
                    Models = [new ModelCatalogEntry { Id = model, SupportedProtocols = new[] { "openai-chat-completions" } }]
                });

            public async Task<ProviderAuthenticationStatus> GetStatusAsync(CancellationToken ct) =>
                new(await ReadKeyAsync(ct) != null);

            public async Task<ProviderAuthenticationStatus> LoginAsync(string method, IProviderLoginInteraction interaction, CancellationToken ct)
            {
                var key = await interaction.PromptAsync(new("secret", "Fixture key"), ct);
                await credentials.ModifyAsync((_, _) => Task.FromResult<JsonElement?>(JsonSerializer.SerializeToElement(key)), ct);
                return new(true);
            }

            public Task LogoutAsync(CancellationToken ct) => credentials.DeleteAsync(ct);

            public async Task<ProviderUsageSnapshot?> ReadAsync(CancellationToken ct) =>
                await ReadKeyAsync(ct) is { } key ? new(DateTimeOffset.UtcNow, new Dictionary<string, double>
                {
                    ["balance"] = key.Length
                }) : null;

            private async Task<string?> ReadKeyAsync(CancellationToken ct) =>
                (await credentials.ReadAsync(ct))?.GetString();
        }

        internal sealed class UnusedToolSource : IToolSource
        {
            public string SourceId => "fixture-tool";
            public ValueTask<IReadOnlyList<ToolRegistration>> GetRegistrationsAsync(ToolPlanningContext context, CancellationToken ct) =>
                throw new InvalidOperationException("The model service cannot call this contribution.");
        }

        internal sealed class NativeUsage : IProviderUsageSource
        {
            public Task<ProviderUsageSnapshot?> ReadUsageAsync(ProviderUsageRequest request, CancellationToken ct) =>
                Task.FromResult<ProviderUsageSnapshot?>(request.ProviderId == "native" && request.ApiKey == "native-fixture-key"
                    ? new(DateTimeOffset.UtcNow, new Dictionary<string, double> { ["balance"] = 17 })
                    : null);
        }
        """;
}
