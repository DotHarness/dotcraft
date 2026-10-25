using System.Text.Json;
using DotCraft.Agents;
using DotCraft.Configuration;
using DotCraft.Contributions;
using Microsoft.Extensions.AI;
using Xunit;

namespace DotCraft.Tests.Sessions.Protocol.AppServer;

public sealed class ModelBackendCatalogTests
{
    [Fact]
    public async Task ModelListUsesBackendReportedNamesAndReasoning()
    {
        var root = Path.Combine(Path.GetTempPath(), "backend-catalog-" + Guid.NewGuid().ToString("N"));
        var contributions = new ContributionRegistry();
        contributions.Add<IModelBackendSource>(new Source());
        using var resolver = new ModelBackendResolver(new ModelProviderRegistry([]), contributions, root);
        var config = new AppConfig
        {
            ProviderId = "demo",
            Providers = new() { ["demo"] = new() { BackendId = "catalog-demo" } }
        };
        var craft = Path.Combine(root, ".craft");
        Directory.CreateDirectory(craft);
        using var harness = new AppServerTestHarness(workspaceCraftPath: craft,
            appConfigMonitor: new AppConfigMonitor(config), modelRuntimeResolver: resolver);
        await harness.InitializeAsync();
        try
        {
            await harness.ExecuteRequestAsync(harness.BuildRequest("model/list", new { providerId = "demo" }));
            var result = await ReadResultAsync(harness);
            Assert.True(result.GetProperty("success").GetBoolean(), result.GetRawText());
            var models = result.GetProperty("models").EnumerateArray().ToDictionary(m => m.GetProperty("id").GetString()!);

            var opus = models["claude-opus-5-5"];
            Assert.Equal("Opus", opus.GetProperty("displayName").GetString());
            var reasoning = opus.GetProperty("reasoning");
            Assert.False(reasoning.GetProperty("supportsDisable").GetBoolean());
            Assert.Equal(["low", "high"], reasoning.GetProperty("supportedEfforts").EnumerateArray()
                .Select(option => option.GetProperty("effort").GetString()));
            Assert.Equal("high", reasoning.GetProperty("defaultEffort").GetString());
            Assert.False(opus.TryGetProperty("speed", out var speed) && speed.ValueKind != JsonValueKind.Null);

            var haiku = models["claude-haiku-4-5"];
            Assert.False(haiku.TryGetProperty("displayName", out _));
            Assert.False(haiku.TryGetProperty("reasoning", out var none) && none.ValueKind != JsonValueKind.Null);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private static async Task<JsonElement> ReadResultAsync(AppServerTestHarness harness)
    {
        while (true)
        {
            using var message = await harness.Transport.ReadNextSentAsync(TimeSpan.FromSeconds(5));
            if (!message.RootElement.TryGetProperty("id", out _)) continue;
            Assert.False(message.RootElement.TryGetProperty("error", out _), message.RootElement.GetRawText());
            return message.RootElement.GetProperty("result").Clone();
        }
    }

    private sealed class Source : IModelBackendSource, IModelBackend, IModelProvider, IModelCatalogProvider
    {
        public IModelBackend Backend => this;
        public ModelBackendDescriptor Descriptor => new("catalog-demo", "Catalog demo", "test", ["test"], [], new ModelProviderCapabilities());
        public IReadOnlyCollection<string> Protocols => ["test"];
        public IModelProvider CreateProvider(ModelBackendContext context) => this;
        public IChatClient CreateChatClient(EffectiveModelRuntime runtime) => throw new NotSupportedException();
        public object? GetService(Type type, object? key = null) => type.IsInstanceOfType(this) ? this : null;
        public Task<ModelCatalogResult> FetchModelsAsync(EffectiveModelRuntime runtime, CancellationToken ct) => Task.FromResult(new ModelCatalogResult
        {
            Success = true,
            Models =
            [
                new()
                {
                    Id = "claude-opus-5-5",
                    DisplayName = "Opus",
                    Reasoning = new(false, [ProviderReasoningEffort.Low, ProviderReasoningEffort.High], ProviderReasoningEffort.High)
                },
                new() { Id = "claude-haiku-4-5" }
            ]
        });
    }
}
