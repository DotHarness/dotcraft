using System.Net;
using DotCraft.Agents;
using DotCraft.Agents.Remote;
using DotCraft.Auth.OpenAI;
using DotCraft.Configuration;
using DotCraft.Harness;
using DotCraft.Tools;
using DotCraft.Tools.BackgroundTerminals;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using static SmokeAssertions;

internal static class RemoteModelSmoke
{
    internal static async Task RunAsync(string workspace)
    {
        var config = new AppConfig();
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddDotCraftHarness(config, options =>
        {
            options.WorkspacePath = workspace;
            options.ModelService = new(new Uri("https://models.example/model-service/"), "client-test");
            options.InitialModelServiceCatalog = new("consumer",
                [new("primary", "Primary", ModelProviderProtocols.OpenAIChatCompletions,
                    "https://upstream.example/v1", "apiKey", false, new(true))]);
            options.RefreshModelServiceCatalog = false;
        });
        using var host = builder.Build();
        Ensure(host.Services.GetService<IOpenAIAuthService>() is null, "Remote Runtime registered local authentication.");
        Ensure(host.Services.GetServices<IModelProvider>().Count() == 2, "Native providers must retain their unique protocol registrations.");
        using var service = new HttpClient(new Service());
        using var transport = new RemoteProviderTransport(new(new Uri("https://models.example/model-service/"), "client-test"), service);
        var provider = new OpenAIClientProvider(httpTransport: transport);
        using var client = provider.CreateChatClient(ModelProviderResolver.ResolveProvider(config, "primary") with { Model = "test-model" });
        var reply = await client.GetResponseAsync([new ChatMessage(ChatRole.User, "hello")]);
        Ensure(reply.Text == "remote reply", "Native provider failed through the packaged remote transport.");
    }

    internal static async Task VerifyContainerAsync()
    {
        using var transport = new RemoteProviderTransport(new(
            new Uri(Environment.GetEnvironmentVariable("MODEL_SERVICE_URL")!),
            Environment.GetEnvironmentVariable("MODEL_SERVICE_TOKEN")!));
        var catalog = await transport.GetCatalogAsync();
        var config = new AppConfig { ModelService = new() };
        RemoteModelConfiguration.Apply(config, catalog);
        var runtime = ModelProviderResolver.ResolveProvider(config, catalog.Providers.Single().Id) with { Model = "test-model" };
        using var model = new OpenAIClientProvider(httpTransport: transport).CreateChatClient(runtime);
        Ensure((await model.GetResponseAsync([new ChatMessage(ChatRole.User, "hello")])).Text == "remote reply",
            "The isolated Runtime could not call its model service.");
        Directory.CreateDirectory("/workspace");
        File.WriteAllText("/workspace/marker", "workspace-readable");
        var files = new FileTools("/workspace", workspaceRoots: ["/"]);
        var readable = await files.ReadFile("/workspace/marker");
        Ensure(readable.OfType<TextContent>().Any(item => item.Text.Contains("workspace-readable")), "File tool did not run.");
        var missing = await files.ReadFile("/state/credentials/auth.json");
        Ensure(missing.OfType<TextContent>().Any(item => item.Text.Contains("File not found")),
            "The model-service credential volume reached the file tool.");
        await using var terminals = new BackgroundTerminalService("/workspace/.craft", new AppConfig.ShellBackgroundConfig());
        var shell = new ShellTools("/workspace", terminals, workspaceRoots: ["/"]);
        var output = await shell.Exec("cat /workspace/marker; if [ -e /state/credentials/auth.json ]; then echo credential-visible; else echo credential-absent; fi", shell: "/bin/bash");
        Ensure(output.Contains("workspace-readable") && output.Contains("credential-absent") && !output.Contains("credential-visible"),
            "Shell credential isolation failed: " + output);
        Console.WriteLine("Native file and Shell tools can use the workspace and cannot read the model-service credential volume.");
    }

    private sealed class Service : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Ensure(request.RequestUri!.AbsoluteUri == "https://models.example/model-service/providers/primary/http/chat/completions",
                "Provider did not use the model service.");
            Ensure(request.Headers.Authorization?.Parameter == "client-test", "Wrong model-service credential.");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"id":"chat_1","object":"chat.completion","created":1,"model":"test-model","choices":[{"index":0,"message":{"role":"assistant","content":"remote reply"},"finish_reason":"stop"}],"usage":{"prompt_tokens":1,"completion_tokens":2,"total_tokens":3}}""",
                    System.Text.Encoding.UTF8, "application/json")
            });
        }
    }
}
