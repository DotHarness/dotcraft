using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using DotCraft.Oratorio.Data;
using DotCraft.Oratorio.Hosting;
using DotCraft.Oratorio.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace DotCraft.Oratorio.Tests;

public sealed class OratorioHostingTests : IDisposable
{
    private readonly List<string> _roots = [];

    private string CreateRoot(string prefix)
    {
        var root = Directory.CreateTempSubdirectory(prefix).FullName;
        _roots.Add(root);
        return root;
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        foreach (var root in _roots)
            Directory.Delete(root, recursive: true);
    }

    [Fact]
    public async Task HostInputsOverrideOverlayAndPreserveStateAcrossRestart()
    {
        var root = CreateRoot("oratorio-host-state-");
        var state = Path.Combine(root, "state");
        var rejected = Path.Combine(root, "rejected");
        var options = new OratorioHostOptions
        {
            StateRoot = state,
            ServiceToken = "host-token",
            ListenUrl = "http://127.0.0.1:0"
        };
        var builder = OratorioApplication.CreateBuilder(options);
        Assert.False(Directory.Exists(state));
        Directory.CreateDirectory(state);
        await File.WriteAllTextAsync(Path.Combine(state, "config.json"), JsonSerializer.Serialize(new
        {
            urls = "http://192.0.2.1:5087",
            Oratorio = new
            {
                DatabasePath = Path.Combine(rejected, "wrong.db"),
                Settings = new { ConfigPath = Path.Combine(rejected, "config.json") },
                Hosting = new { StateRoot = rejected, ServiceToken = "overlay-token" }
            }
        }));
        string protectedSecret;
        await using (var app = await OratorioApplication.BuildAsync(builder, RemoveBackgroundServices))
        {
            var settings = app.Services.GetRequiredService<IOptionsMonitor<SettingsWriteOptions>>().CurrentValue;
            Assert.Equal(Path.Combine(state, "config.json"), settings.ConfigPath);
            Assert.Equal(Path.Combine(state, "secrets.key"), settings.SecretKeyPath);
            await using (var scope = app.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<OratorioDbContext>();
                Assert.Equal(Path.Combine(state, "oratorio.db"), db.Database.GetDbConnection().DataSource);
                db.Items.Add(new OratorioItem { ItemId = "host-item", Source = "local", ExternalId = "host-item", Title = "Persisted item" });
                await db.SaveChangesAsync();
            }
            protectedSecret = app.Services.GetRequiredService<IConfigurationSecretProtector>().Protect("persistent secret");
            await app.StartAsync();
            using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/status")).StatusCode);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "host-token");
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/status")).StatusCode);
            await app.StopAsync();
        }
        await using (var reopened = await BuildAsync(options))
        {
            await using var scope = reopened.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<OratorioDbContext>();
            Assert.Equal("Persisted item", (await db.Items.SingleAsync(x => x.ItemId == "host-item")).Title);
            var protector = reopened.Services.GetRequiredService<IConfigurationSecretProtector>();
            Assert.Equal("persistent secret", protector.Unprotect(protectedSecret));
        }
        Assert.False(Directory.Exists(rejected));
        Assert.True(File.Exists(Path.Combine(state, "secrets.key")));
    }

    [Fact]
    public async Task ConfiguredPathsAndSecretKeyOverride_AreUsedWithoutStateOverride()
    {
        var root = CreateRoot("oratorio-host-config-");
        var overlay = Path.Combine(root, "configuration.json");
        var database = Path.Combine(root, "database", "custom.db");
        var secretKey = Path.Combine(root, "keys", "custom.key");
        await File.WriteAllTextAsync(overlay, "{}");
        var builder = CreateBuilder(new OratorioHostOptions());
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Oratorio:Settings:ConfigPath"] = overlay,
            ["Oratorio:DatabasePath"] = database,
            ["Oratorio:Settings:SecretKeyPath"] = secretKey
        });
        await using var app = await OratorioApplication.BuildAsync(builder, RemoveBackgroundServices);
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OratorioDbContext>();
        Assert.Equal(database, db.Database.GetDbConnection().DataSource);
        Assert.Equal(overlay, app.Services.GetRequiredService<IOptionsMonitor<SettingsWriteOptions>>().CurrentValue.ConfigPath);
        app.Services.GetRequiredService<IConfigurationSecretProtector>().Protect("custom path secret");
        Assert.True(File.Exists(secretKey));
    }

    [Fact]
    public async Task BuildFailure_DisposesAlreadyCreatedResources()
    {
        var root = CreateRoot("oratorio-host-failure-");
        var builder = CreateBuilder(new OratorioHostOptions { StateRoot = Path.Combine(root, "state") });
        var resource = new DisposalProbe();
        await Assert.ThrowsAsync<InvalidOperationException>(() => OratorioApplication.BuildAsync(builder, services =>
        {
            RemoveBackgroundServices(services);
            services.AddSingleton(_ => resource);
            services.RemoveAll<OratorioDbContext>();
            services.AddScoped<OratorioDbContext>(provider =>
            {
                provider.GetRequiredService<DisposalProbe>();
                throw new InvalidOperationException("Database initialization failed.");
            });
        }));
        Assert.True(resource.Disposed);
    }

    private static WebApplicationBuilder CreateBuilder(OratorioHostOptions options)
    {
        var builder = OratorioApplication.CreateBuilder(options);
        builder.WebHost.UseTestServer();
        return builder;
    }

    private static Task<WebApplication> BuildAsync(OratorioHostOptions options) =>
        OratorioApplication.BuildAsync(CreateBuilder(options), RemoveBackgroundServices);

    private static void RemoveBackgroundServices(IServiceCollection services) => services.RemoveAll<IHostedService>();

    private sealed class DisposalProbe : IDisposable
    {
        public bool Disposed { get; private set; }
        public void Dispose() => Disposed = true;
    }
}
