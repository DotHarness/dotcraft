using DotCraft.Oratorio.Hosting;
using DotCraft.Oratorio.GitHub;
using DotCraft.Oratorio.Integrations;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DotCraft.Oratorio.Tests;

internal sealed class TestOratorioApp : IAsyncDisposable
{
    private readonly string _databasePath;
    private readonly Action<IServiceCollection>? _configureServices;
    private readonly IReadOnlyDictionary<string, string?> _settings;
    private readonly Lazy<Task<WebApplication>> _application;
    private int? _kestrelPort;

    public TestOratorioApp(
        Action<IServiceCollection>? configureServices = null,
        IReadOnlyDictionary<string, string?>? settings = null,
        string? databasePath = null)
    {
        _databasePath = databasePath ?? Path.Combine(Path.GetTempPath(), "oratorio-tests", $"{Guid.NewGuid():n}.db");
        _configureServices = configureServices;
        _settings = settings ?? new Dictionary<string, string?>();
        _application = new(() => Task.Run(StartAsync));
    }

    private WebApplication Application => _application.Value.GetAwaiter().GetResult();
    public IServiceProvider Services => Application.Services;
    public TestServer Server => Application.GetTestServer();
    public HttpClient CreateClient() => _kestrelPort.HasValue
        ? new HttpClient { BaseAddress = new Uri(Application.Urls.Single()) }
        : Application.GetTestClient();

    public void UseKestrel(int port)
    {
        if (_application.IsValueCreated)
            throw new InvalidOperationException("The test server has already started.");
        _kestrelPort = port;
    }

    private async Task<WebApplication> StartAsync()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_databasePath)!);
        var defaultWorkspace = Path.ChangeExtension(_databasePath, ".workspace");
        Directory.CreateDirectory(defaultWorkspace);
        var builder = OratorioApplication.CreateBuilder(new OratorioHostOptions { EnvironmentName = "Testing" });
        if (_kestrelPort is { } port)
            builder.WebHost.UseUrls($"http://127.0.0.1:{port}");
        else
            builder.WebHost.UseTestServer();
        builder.Configuration["Oratorio:DatabasePath"] = _databasePath;
        builder.Configuration["Oratorio:Settings:SecretKeyPath"] = Path.ChangeExtension(_databasePath, ".key");
        builder.Configuration["Oratorio:DotCraft:MaxRunAttempts"] = "1";
        builder.Configuration["Oratorio:DotCraft:RetryBackoffSeconds"] = "1";
        builder.Configuration["Oratorio:DotCraft:MaxRetryBackoffSeconds"] = "1";
        builder.Configuration["Oratorio:DotCraft:RepositoryWorkspaceRoutes:0:Project"] = "github:github.com/example-owner/oratorio";
        builder.Configuration["Oratorio:DotCraft:RepositoryWorkspaceRoutes:0:WorkspacePath"] = defaultWorkspace;
        builder.Configuration["Oratorio:GitHub:Token"] = "test-token";
        builder.Configuration["Oratorio:GitHub:WritesEnabled"] = "true";
        builder.Configuration["Oratorio:GitHub:AppId"] = "12345";
        builder.Configuration["Oratorio:GitHub:InstallationProfiles:0:Instance"] = "github.com";
        builder.Configuration["Oratorio:GitHub:InstallationProfiles:0:Owner"] = "example-owner";
        builder.Configuration["Oratorio:GitHub:InstallationProfiles:0:InstallationId"] = "98765";
        builder.Configuration["Oratorio:GitHub:InstallationProfiles:0:Source"] = "manual";
        builder.Configuration["Oratorio:GitHub:PrivateKey"] = "test-private-key";
        builder.Configuration["Oratorio:GitHub:WebhookSecret"] = "test-secret";
        builder.Configuration["Oratorio:GitHub:Repositories:0"] = "example-owner/oratorio";
        builder.Configuration["Oratorio:Settings:ConfigPath"] = Path.ChangeExtension(_databasePath, ".json");
        builder.Configuration.AddInMemoryCollection(_settings);
        var app = await OratorioApplication.BuildAsync(builder, services =>
        {
            services.RemoveAll<IWorktreeManager>();
            services.AddSingleton<IWorktreeManager, FakeWorktreeManager>();
            services.RemoveAll<IGitDeliveryClient>();
            services.AddSingleton<IGitDeliveryClient, FakeGitDeliveryClient>();
            _configureServices?.Invoke(services);
        });
        try
        {
            await app.StartAsync();
            return app;
        }
        catch
        {
            await app.DisposeAsync();
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (!_application.IsValueCreated)
            return;
        var app = await _application.Value;
        try
        {
            await app.StopAsync();
        }
        finally
        {
            await app.DisposeAsync();
        }
    }
}
