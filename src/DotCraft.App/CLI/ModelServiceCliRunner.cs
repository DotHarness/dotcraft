using DotCraft.Agents;
using DotCraft.Auth.OpenAI;
using DotCraft.Configuration;
using DotCraft.ModelService;
using DotCraft.Plugins;
using DotCraft.Runtime;
using DotCraft.Workspaces;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace DotCraft.CLI;

internal static partial class ModelServiceCliRunner
{
    public static async Task<int> ServeAsync(
        string stateDirectory, string[] plugins, string listenUrl, CancellationToken cancellationToken)
    {
        stateDirectory = Path.GetFullPath(stateDirectory);
        var config = FileModelServiceAccess.ReadConfiguration(stateDirectory);
        var builder = WebApplication.CreateSlimBuilder();
        ConfigureServices(builder.Services, stateDirectory, config, plugins);
        builder.Services.AddHostedService(sp => new StartupValidation(
            stateDirectory, sp.GetRequiredService<ModelBackendResolver>()));
        builder.Services.AddSingleton<IModelServiceAccess>(sp => new FileModelServiceAccess(stateDirectory,
            sp.GetRequiredService<OpenAIAuthManager>(), sp.GetRequiredService<ModelBackendResolver>()));
        builder.Services.AddDotCraftModelService();
        await using var app = builder.Build();
        app.Urls.Add(listenUrl);
        app.MapDotCraftModelService();
        await app.StartAsync(cancellationToken);
        await app.WaitForShutdownAsync(cancellationToken);
        return 0;
    }

    public static Task<int> CheckAsync(string directory, string[] plugins, TextWriter output, CancellationToken ct) =>
        WithRuntimeAsync(directory, plugins, (_, services) =>
        {
            var config = FileModelServiceAccess.LoadConfiguration(directory, services.GetRequiredService<ModelBackendResolver>());
            var clients = new ModelServiceClientStore(directory).Load();
            foreach (var client in clients)
                foreach (var id in client.Providers)
                    if (!config.Providers.ContainsKey(id))
                        throw new InvalidOperationException($"Client '{client.Name}' grants unknown provider '{id}'.");
            output.WriteLine($"Valid: {config.Providers.Count} providers, {clients.Count} clients.");
            return Task.FromResult(0);
        }, ct);

    public static Task<int> CreateClientAsync(
        string directory, string[] plugins, string name, string[] providers, string outputPath, TextWriter output, CancellationToken ct) =>
        WithRuntimeAsync(directory, plugins, (_, services) =>
        {
            var config = FileModelServiceAccess.LoadConfiguration(directory, services.GetRequiredService<ModelBackendResolver>());
            foreach (var id in providers)
                if (!config.Providers.ContainsKey(id))
                    throw new ArgumentException($"Unknown provider '{id}'.");
            using var file = new FileStream(outputPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(outputPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            var created = new ModelServiceClientStore(directory).Create(name, providers);
            using var writer = new StreamWriter(file);
            writer.Write(created.Token);
            output.WriteLine($"Created client {created.Client.Id}; token saved to {Path.GetFullPath(outputPath)}.");
            return Task.FromResult(0);
        }, ct);

    public static Task<int> TrustPluginAsync(
        string directory, string[] plugins, string pluginId, TextWriter output, CancellationToken ct)
    {
        if (!plugins.Any(id => PluginIds.EqualsCanonical(id, pluginId)))
        {
            output.WriteLine($"Plugin '{pluginId}' must be explicitly allowed with --plugin {pluginId}.");
            return Task.FromResult(1);
        }

        return WithRuntimeAsync(directory, plugins, async (_, services) =>
        {
            var runtime = services.GetRequiredService<IDotNetExtensionRuntimeCoordinator>();
            var result = await runtime.TrustAsync(pluginId, ct);
            foreach (var diagnostic in result.Diagnostics)
                output.WriteLine($"{diagnostic.Code}: {diagnostic.Message}");
            if (result.Outcome == PluginRuntimeMutationOutcome.NotApplied)
                return 1;
            output.WriteLine($"Trusted plugin '{pluginId}'.");
            return 0;
        }, ct, FileModelServiceAccess.ReadStateConfiguration);
    }

    public static int RevokePluginTrust(string directory, string pluginId, TextWriter output)
    {
        var revoked = DotNetExtensionTrust.RevokePersistedGrants(Path.Combine(directory, "config.json"), pluginId);
        output.WriteLine(revoked ? $"Revoked trust for plugin '{pluginId}'." : $"No trust grants for plugin '{pluginId}'.");
        return 0;
    }

    private static void ConfigureServices(IServiceCollection services, string directory, AppConfig config, string[] plugins)
    {
        var paths = DotCraftPaths.Create(directory, directory);
        services.AddSingleton<OpenAIAuthManager>();
        services.AddSingleton<IOpenAIAuthService>(sp => sp.GetRequiredService<OpenAIAuthManager>());
        services.AddOpenAIModelProvider(paths.Data.Resolve("credentials"));
        services.AddAnthropicModelProvider();
        services.AddOpenRouterModelProvider();
        services.AddDotCraftModelPluginRuntime(config, paths,
            plugins.Length == 0 ? [] : [paths.Data.Resolve("plugins")], plugins);
    }

    private static async Task<int> WithRuntimeAsync(string directory, string[] plugins,
        Func<AppConfig, IServiceProvider, Task<int>> action, CancellationToken ct, Func<string, AppConfig>? readConfig = null)
    {
        directory = Path.GetFullPath(directory);
        var config = (readConfig ?? FileModelServiceAccess.ReadConfiguration)(directory);
        var services = new ServiceCollection();
        ConfigureServices(services, directory, config, plugins);
        await using var provider = services.BuildServiceProvider();
        foreach (var service in provider.GetServices<IHostedService>())
            await service.StartAsync(ct);
        return await action(config, provider);
    }

    private sealed class StartupValidation(string directory, ModelBackendResolver resolver) : IHostedService
    {
        public Task StartAsync(CancellationToken cancellationToken)
        {
            FileModelServiceAccess.LoadConfiguration(directory, resolver);
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
