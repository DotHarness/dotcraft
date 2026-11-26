using System.Diagnostics;
using DotCraft.Agents;
using DotCraft.Agents.Remote;
using DotCraft.CLI;
using DotCraft.Configuration;
using DotCraft.ModelService;
using DotCraft.Plugins;
using DotCraft.Runtime;
using DotCraft.Tests.Runtime.Plugins;
using DotCraft.Workspaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace DotCraft.Tests.CLI;

public sealed class ModelServicePluginCliTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("dotcraft-model-service-cli-").FullName;
    private const string PluginId = "service-fixture";
    private const string ProviderId = "gateway";

    [Fact]
    public async Task InstalledBackendRequiresServiceTrustAndExplicitAllowList()
    {
        PrepareService();
        Assert.NotEqual(0, (await RunAsync("plugin", "trust", PluginId)).Code);
        Assert.NotEqual(0, (await RunAsync("--plugin", PluginId, "check")).Code);

        var trust = await RunAsync("--plugin", PluginId, "plugin", "trust", PluginId);
        Assert.True(trust.Code == 0, trust.Output + trust.Error);
        Assert.NotEqual(0, (await RunAsync("check")).Code);
        var check = await RunAsync("--plugin", PluginId, "--plugin", "not-installed", "check");
        Assert.True(check.Code == 0, check.Error);

        var tokenPath = Path.Combine(_root, "worker.token");
        var create = await RunAsync("client", "create", "--plugin", PluginId, "--name", "worker",
            "--provider", ProviderId, "--output", tokenPath);
        Assert.True(create.Code == 0, create.Error);
        var token = await File.ReadAllTextAsync(tokenPath);
        var client = new ModelServiceClientStore(_root).Authenticate(token);
        Assert.Equal([ProviderId], Assert.IsType<ModelServiceClient>(client).Providers);
        Assert.DoesNotContain(token, create.Output, StringComparison.Ordinal);

        var revoke = await RunAsync("--plugin", PluginId, "plugin", "revoke", PluginId);
        Assert.True(revoke.Code == 0, revoke.Output + revoke.Error);
        Assert.NotEqual(0, (await RunAsync("--plugin", PluginId, "check")).Code);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RevokeRemovesAllStoredGrantsAfterInstalledBundleChanges(bool removeBundle)
    {
        PrepareService();
        var trust = await RunAsync("--plugin", PluginId, "plugin", "trust", PluginId);
        Assert.Equal(0, trust.Code);

        var config = FileModelServiceAccess.ReadStateConfiguration(_root);
        using var authority = new DotNetExtensionTrust(config);
        Assert.True(authority.Grant(PluginId, "another-fixture-fingerprint"));
        Assert.True(authority.Grant("other-plugin", "other-fixture-fingerprint"));

        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var services = new ServiceCollection();
        services.AddDotCraftModelPluginRuntime(config, DotCraftPaths.Create(_root, _root),
            [Path.Combine(_root, "plugins")], [PluginId]);
        await using var service = services.BuildServiceProvider();
        foreach (var hosted in service.GetServices<IHostedService>())
            await hosted.StartAsync(deadline.Token);
        var resolver = service.GetRequiredService<ModelBackendResolver>();
        var provider = ModelProviderResolver.ResolveProvider(config, ProviderId);
        var route = Assert.IsType<ModelServiceProvider>(ModelServiceProvider.ResolveBackend(provider, resolver));

        var bundle = Path.Combine(_root, "plugins", PluginId);
        if (removeBundle)
            Directory.Delete(bundle, recursive: true);
        else
            DotNetExtensionTestBundle.WritePlugin(bundle, PluginId, "Fixture.Plugin",
                PluginSource.Replace("fixture-model", "replacement-model", StringComparison.Ordinal));

        Assert.False(route.Stopping.IsCancellationRequested);
        var withdrawn = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = route.Stopping.Register(() => withdrawn.TrySetResult());

        var revoke = await RunAsync("plugin", "revoke", PluginId);
        Assert.Equal(0, revoke.Code);
        await withdrawn.Task.WaitAsync(deadline.Token);
        Assert.Null(ModelServiceProvider.ResolveBackend(provider, resolver));

        using var reloaded = new DotNetExtensionTrust(config);
        Assert.Null(reloaded.TrustedFingerprint(PluginId));
        Assert.Equal(DotNetExtensionTrustStatus.Trusted, reloaded.ResolveStatus("other-plugin", "other-fixture-fingerprint"));
    }

    [Fact]
    public async Task TrustAdministrationDoesNotRequireOtherProvidersToBeReady()
    {
        PrepareService();
        File.WriteAllText(Path.Combine(_root, "config.json"), """
            {
                "Providers": {
                    "gateway": { "BackendId": "service-fixture" },
                    "unconfigured": { "Protocol": "openai-responses", "EndPoint": "https://provider.invalid/v1" }
                }
            }
            """);

        var trust = await RunAsync("--plugin", PluginId, "plugin", "trust", PluginId);
        Assert.Equal(0, trust.Code);
        using var authority = new DotNetExtensionTrust(FileModelServiceAccess.ReadStateConfiguration(_root));
        Assert.NotNull(authority.TrustedFingerprint(PluginId));

        Assert.NotEqual(0, (await RunAsync("--plugin", PluginId, "check")).Code);

        var revoke = await RunAsync("plugin", "revoke", PluginId);
        Assert.Equal(0, revoke.Code);
        Assert.Null(authority.TrustedFingerprint(PluginId));
    }

    [Fact]
    public async Task ExplicitProviderAuthPersistsCredentialsAndShowsAccountUsage()
    {
        PrepareService();
        var trust = await RunAsync("--plugin", PluginId, "plugin", "trust", PluginId);
        Assert.True(trust.Code == 0, trust.Output + trust.Error);

        const string token = "fixture-local-key";
        using var input = new StringReader(token);
        using var output = new StringWriter();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        Assert.Equal(0, await ModelServiceCliRunner.LoginAsync(_root, [PluginId], ProviderId, "token",
            noBrowser: true, input, output, deadline.Token));
        Assert.DoesNotContain(token, output.ToString(), StringComparison.Ordinal);

        var status = await RunAsync("--plugin", PluginId, "auth", "status", "--provider", ProviderId);
        Assert.True(status.Code == 0, status.Error);
        Assert.Contains("limit_remaining: 7", status.Output, StringComparison.Ordinal);
        Assert.DoesNotContain(token, status.Output, StringComparison.Ordinal);

        var logout = await RunAsync("--plugin", PluginId, "auth", "logout", "--provider", ProviderId);
        Assert.True(logout.Code == 0, logout.Error);
        var after = await RunAsync("--plugin", PluginId, "auth", "status", "--provider", ProviderId);
        Assert.True(after.Code == 0, after.Error);
        Assert.DoesNotContain("limit_remaining", after.Output, StringComparison.Ordinal);
        Assert.Null(await new ProviderCredentialStore(_root, PluginId, ProviderId).ReadAsync(deadline.Token));
    }

    [Fact]
    public async Task StandaloneServePublishesTrustedBackendFromServiceState()
    {
        PrepareService();
        var trust = await RunAsync("--plugin", PluginId, "plugin", "trust", PluginId);
        Assert.True(trust.Code == 0, trust.Output + trust.Error);
        var serviceToken = new ModelServiceClientStore(_root).Create("worker", [ProviderId]).Token;
        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = _root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        string[] args = [typeof(DotCraftCommandLine).Assembly.Location, "model-service", "--state", _root,
            "--plugin", PluginId, "serve", "--listen", "http://127.0.0.1:0"];
        foreach (var argument in args)
            start.ArgumentList.Add(argument);

        using var process = Process.Start(start)!;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var errors = process.StandardError.ReadToEndAsync(deadline.Token);
        try
        {
            const string listening = "Now listening on:";
            string? line;
            do { line = await process.StandardOutput.ReadLineAsync(deadline.Token); }
            while (line is not null && !line.Contains(listening, StringComparison.Ordinal));
            var readiness = line ?? throw new InvalidOperationException(await errors);
            var address = readiness[(readiness.IndexOf(listening, StringComparison.Ordinal) + listening.Length)..].Trim();
            using var transport = new RemoteProviderTransport(new(new Uri(address + "/model-service"), serviceToken));
            var provider = Assert.Single((await transport.GetCatalogAsync(deadline.Token)).Providers);
            Assert.Equal(PluginId, provider.BackendId);
            Assert.Contains("fixture-model", provider.Models!.Keys);
        }
        finally
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
        }
    }

    private async Task<(int Code, string Output, string Error)> RunAsync(params string[] arguments)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var code = await DotCraftCommandLine.RunAsync(["model-service", "--state", _root, .. arguments], output, error, deadline.Token);
        return (code, output.ToString(), error.ToString());
    }

    private void PrepareService()
    {
        File.WriteAllText(Path.Combine(_root, "config.json"),
            """{ "Providers": { "gateway": { "BackendId": "service-fixture" } } }""");
        DotNetExtensionTestBundle.WritePlugin(Path.Combine(_root, "plugins", PluginId), PluginId, "Fixture.Plugin", PluginSource);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private const string PluginSource = """
        #nullable enable
        using System;
        using System.Collections.Generic;
        using System.Text.Json;
        using System.Threading;
        using System.Threading.Tasks;
        using DotCraft.Agents;
        using DotCraft.Configuration;
        using DotCraft.Contributions;
        using DotCraft.Plugins;
        using Microsoft.Extensions.AI;
        namespace Fixture;
        public sealed class Plugin : IDotCraftPlugin
        {
            public ValueTask ActivateAsync(IPluginActivationContext context, CancellationToken ct)
            {
                context.Contributions.Add<IModelBackendSource>(new Backend());
                return ValueTask.CompletedTask;
            }
        }
        internal sealed class Backend : IModelBackend, IModelBackendSource
        {
            IModelBackend IModelBackendSource.Backend => this;
            public ModelBackendDescriptor Descriptor => new("service-fixture", "Fixture", "openai-chat-completions",
                new[] { "openai-chat-completions" }, new[] { new ProviderAuthenticationMethod("token", "Token", "apiKey") }, new());

            public IModelProvider CreateProvider(ModelBackendContext context) => new Provider(context.Credentials);
        }
        internal sealed class Provider(IProviderCredentialStore credentials) :
            IModelProvider, IProviderNativeHttpRouter, IProviderAuthenticator, IProviderUsageReader, IModelCatalogProvider
        {
            public IReadOnlyCollection<string> Protocols => new[] { "openai-chat-completions" };
            public Task<ModelCatalogResult> FetchModelsAsync(EffectiveModelRuntime runtime, CancellationToken ct) =>
                Task.FromResult(new ModelCatalogResult { Success = true, Models = new() { new() { Id = "fixture-model" } } });
            public IChatClient CreateChatClient(EffectiveModelRuntime runtime) => throw new NotSupportedException();
            public ProviderHttpRoute ResolveRoute(EffectiveModelRuntime runtime) =>
                new("openai-chat-completions", "https://fixture.invalid/v1", runtime.Model);
            public Task<IReadOnlyDictionary<string, string>> AuthenticateAsync(EffectiveModelRuntime runtime, CancellationToken ct) =>
                throw new NotSupportedException();

            public async Task<ProviderAuthenticationStatus> GetStatusAsync(CancellationToken ct) =>
                new((await credentials.ReadAsync(ct))?.ValueKind == JsonValueKind.String);

            public async Task<ProviderAuthenticationStatus> LoginAsync(string method, IProviderLoginInteraction interaction, CancellationToken ct)
            {
                if (method != "token") throw new ArgumentException(nameof(method));
                var token = await interaction.PromptAsync(new("secret", "Enter the fixture token"), ct);
                await credentials.ModifyAsync((_, _) => Task.FromResult<JsonElement?>(JsonSerializer.SerializeToElement(token)), ct);
                return new(true);
            }

            public Task LogoutAsync(CancellationToken ct) => credentials.DeleteAsync(ct);
            public Task<ProviderUsageSnapshot?> ReadAsync(CancellationToken ct) =>
                Task.FromResult<ProviderUsageSnapshot?>(new(DateTimeOffset.UtcNow, new Dictionary<string, double> { ["limit_remaining"] = 7 }));
        }
        """;
}
