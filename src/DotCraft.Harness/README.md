# DotCraft.Harness

`DotCraft.Harness` hosts the DotCraft Agent Harness inside a .NET 10 Generic Host. It includes the
Runtime, Core, Agents, OpenAI, and Anthropic assemblies in one package.

The Harness does not load configuration files or select a user-profile directory. Prepare an
effective `AppConfig`, then register the Harness with paths owned by your application:

```csharp
using DotCraft.Configuration;
using DotCraft.Harness;
using DotCraft.Sessions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var workspacePath = Directory.GetCurrentDirectory();
var providerId = "openai";
var config = new AppConfig
{
    ProviderId = providerId,
    ProviderPreferences = new Dictionary<string, ModelPreference>
    {
        [providerId] = new() { Model = Environment.GetEnvironmentVariable("DOTCRAFT_MODEL")! }
    },
    Providers =
    {
        [providerId] = new AppConfig.ModelProviderConfig
        {
            DisplayName = "OpenAI",
            Protocol = ModelProviderProtocols.OpenAIChatCompletions,
            ApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY")!,
            EndPoint = ModelProviderDefaults.DefaultOpenAIEndpoint
        }
    }
};

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddDotCraftHarness(config, options => options.WorkspacePath = workspacePath);

using var host = builder.Build();
await host.StartAsync();

var sessions = host.Services.GetRequiredService<ISessionService>();
var thread = await sessions.CreateThreadAsync(new SessionIdentity
{
    ChannelName = "embedded",
    UserId = "local-user",
    WorkspacePath = workspacePath
});

await foreach (var sessionEvent in sessions.SubmitInputAsync(thread.Id, "Summarize this workspace."))
{
    if (sessionEvent.DeltaPayload?.TextDelta is { } text)
        Console.Write(text);
}

await host.StopAsync();
```

See the [DotCraft repository](https://github.com/DotHarness/dotcraft) for source and documentation.
