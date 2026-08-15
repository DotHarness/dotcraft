using System.Runtime.CompilerServices;
using DotCraft.Agents;
using DotCraft.Configuration;
using DotCraft.Harness;
using DotCraft.Sessions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

var testRoot = Path.Combine(Path.GetTempPath(), $"dotcraft-harness-consumer-{Guid.NewGuid():N}");
try
{
    var workspacePath = Path.Combine(testRoot, "session");
    var builder = Host.CreateApplicationBuilder();
    builder.Services.AddDotCraftHarness(
        CreateConfig(),
        options => options.WorkspacePath = workspacePath);
    builder.Services.RemoveAll<IModelProvider>();
    builder.Services.AddSingleton<IModelProvider, FakeModelProvider>();

    using var host = builder.Build();
    await host.StartAsync();

    var sessions = host.Services.GetRequiredService<ISessionService>();

    var thread = await sessions.CreateThreadAsync(new SessionIdentity
    {
        ChannelName = "package-smoke",
        UserId = "consumer",
        WorkspacePath = workspacePath
    });

    var response = new System.Text.StringBuilder();
    var completed = false;
    await foreach (var sessionEvent in sessions.SubmitInputAsync(thread.Id, "Reply from the package smoke test."))
    {
        if (sessionEvent.DeltaPayload?.TextDelta is { } delta)
            response.Append(delta);
        if (sessionEvent.EventType == SessionEventType.TurnCompleted)
            completed = true;
        if (sessionEvent.EventType == SessionEventType.TurnFailed)
            throw new InvalidOperationException(sessionEvent.TurnFailedPayload?.Error ?? "The smoke-test Turn failed.");
    }

    Ensure(completed, "The smoke-test Turn did not complete.");
    Ensure(response.ToString().Contains("package-smoke-ok", StringComparison.Ordinal), "Unexpected model response.");
    await host.StopAsync();
    Console.WriteLine("DotCraft.Harness consumer smoke test passed.");
}
finally
{
    if (Directory.Exists(testRoot))
        Directory.Delete(testRoot, recursive: true);
}

static AppConfig CreateConfig() => new()
{
    ProviderId = "package-smoke",
    ProviderPreferences = new Dictionary<string, ModelPreference>(StringComparer.OrdinalIgnoreCase)
    {
        ["package-smoke"] = new ModelPreference { Model = "fake-model" }
    },
    Providers =
    {
        ["package-smoke"] = new AppConfig.ModelProviderConfig
        {
            DisplayName = "Package smoke provider",
            Protocol = ModelProviderProtocols.OpenAIChatCompletions,
            ApiKey = "not-used",
            EndPoint = "https://example.invalid/v1"
        }
    }
};

static void Ensure(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

sealed class FakeModelProvider : IModelProvider
{
    public IReadOnlyCollection<string> Protocols { get; } = [ModelProviderProtocols.OpenAIChatCompletions];

    public IChatClient CreateChatClient(EffectiveModelRuntime runtime) => new FakeChatClient();
}

sealed class FakeChatClient : IChatClient
{
    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "package-smoke-ok")));

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        yield return new ChatResponseUpdate(ChatRole.Assistant, "package-smoke-ok");
        await Task.CompletedTask;
    }

    public object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceKey is null && serviceType.IsInstanceOfType(this) ? this : null;

    public void Dispose()
    {
    }
}
