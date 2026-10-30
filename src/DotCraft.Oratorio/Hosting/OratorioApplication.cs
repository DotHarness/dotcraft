using DotCraft.Oratorio.Data;
using DotCraft.Oratorio.Services;
using Microsoft.EntityFrameworkCore;

namespace DotCraft.Oratorio.Hosting;

public static partial class OratorioApplication
{
    private const string StateRootKey = "Oratorio:Hosting:StateRoot";
    private const string ServiceIdKey = "Oratorio:Hosting:ServiceId";
    private const string ServiceTokenKey = "Oratorio:Hosting:ServiceToken";

    public static WebApplicationBuilder CreateBuilder(OratorioHostOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ApplicationName = typeof(OratorioApplication).Assembly.GetName().Name,
            ContentRootPath = AppContext.BaseDirectory,
            EnvironmentName = options.EnvironmentName,
            Args = []
        });
        var inputs = new Dictionary<string, string?>();
        if (!string.IsNullOrWhiteSpace(options.StateRoot))
            inputs[StateRootKey] = Path.GetFullPath(options.StateRoot);
        if (!string.IsNullOrWhiteSpace(options.ServiceId))
            inputs[ServiceIdKey] = options.ServiceId;
        if (!string.IsNullOrWhiteSpace(options.ServiceToken))
            inputs[ServiceTokenKey] = options.ServiceToken;
        builder.Configuration.AddInMemoryCollection(inputs);
        if (!string.IsNullOrWhiteSpace(options.ListenUrl))
            builder.WebHost.UseUrls(options.ListenUrl);
        else if (string.IsNullOrWhiteSpace(builder.Configuration["urls"]))
            builder.WebHost.UseUrls("http://127.0.0.1:5087");
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        builder.Logging.AddFilter("Microsoft.Hosting.Lifetime", LogLevel.Information);
        builder.Logging.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);
        builder.Logging.AddFilter("Microsoft.EntityFrameworkCore.Database.Command", LogLevel.Warning);
        builder.Logging.AddFilter("DotCraft.Oratorio", LogLevel.Information);
        return builder;
    }

    public static async Task<WebApplication> BuildAsync(
        WebApplicationBuilder builder,
        Action<IServiceCollection>? configureServices = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(builder);
        cancellationToken.ThrowIfCancellationRequested();
        var stateRoot = builder.Configuration[StateRootKey];
        var configuredOverlay = builder.Configuration["Oratorio:Settings:ConfigPath"];
        var overlayPath = !string.IsNullOrWhiteSpace(stateRoot)
            ? OratorioStatePaths.ResolveDefaultConfigurationOverlayPath(stateRoot)
            : !string.IsNullOrWhiteSpace(configuredOverlay)
                ? Path.GetFullPath(configuredOverlay)
                : OratorioStatePaths.ResolveDefaultConfigurationOverlayPath();
        var hostingInputs = new Dictionary<string, string?>
        {
            [StateRootKey] = stateRoot,
            [ServiceIdKey] = builder.Configuration[ServiceIdKey],
            [ServiceTokenKey] = builder.Configuration[ServiceTokenKey],
            [WebHostDefaults.ServerUrlsKey] = builder.Configuration[WebHostDefaults.ServerUrlsKey]
        };
        builder.Configuration.AddJsonFile(overlayPath, optional: true, reloadOnChange: true);
        builder.Configuration.AddInMemoryCollection(hostingInputs);
        RegisterServices(builder, overlayPath);
        builder.Services.PostConfigure<SettingsWriteOptions>(settings =>
        {
            settings.ConfigPath = overlayPath;
            if (string.IsNullOrWhiteSpace(settings.SecretKeyPath))
                settings.SecretKeyPath = Path.Combine(OratorioStatePaths.ResolveDefaultStateRoot(stateRoot), "secrets.key");
        });
        configureServices?.Invoke(builder.Services);
        var app = builder.Build();
        try
        {
            await using var scope = app.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<OratorioDbContext>();
            var path = db.Database.GetDbConnection().DataSource;
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);
            await db.Database.EnsureCreatedAsync(cancellationToken);
            MapApplication(app);
            RegisterStartupBanner(app);
            return app;
        }
        catch
        {
            await app.DisposeAsync();
            throw;
        }
    }

    private static string ResolveDatabasePath(WebApplicationBuilder builder)
    {
        var stateRoot = builder.Configuration[StateRootKey];
        if (!string.IsNullOrWhiteSpace(stateRoot))
            return OratorioStatePaths.ResolveDefaultDatabasePath(stateRoot);
        var configured = builder.Configuration["Oratorio:DatabasePath"];
        return string.IsNullOrWhiteSpace(configured)
            ? OratorioStatePaths.ResolveDefaultDatabasePath()
            : Path.GetFullPath(configured);
    }
}
