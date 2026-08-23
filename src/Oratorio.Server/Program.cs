using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Oratorio.Server.Api;
using Oratorio.Server.Data;
using Oratorio.Server.DotCraft;
using Oratorio.Server.GitLab;
using Oratorio.Server.GitHub;
using Oratorio.Server.Realtime;
using Oratorio.Server.Services;
using Oratorio.Server.Sources;

var builder = WebApplication.CreateBuilder(args);
ConfigureDefaultLogging(builder);
ApplyManagedServiceHosting(builder);
var serverConfigurationOverlayPath = ResolveServerConfigurationOverlayPath(builder);
builder.Configuration.AddJsonFile(serverConfigurationOverlayPath, optional: true, reloadOnChange: true);

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
});

builder.Services.AddCors(options =>
{
    options.AddPolicy("DesktopRenderer", policy =>
    {
        policy.SetIsOriginAllowed(IsDesktopRendererOrigin)
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

builder.Services.AddSingleton<IClock, SystemClock>();
builder.Services.Configure<SettingsWriteOptions>(builder.Configuration.GetSection("Oratorio:Settings"));
builder.Services.Configure<GitHubOptions>(builder.Configuration.GetSection("Oratorio:GitHub"));
builder.Services.Configure<GitLabOptions>(builder.Configuration.GetSection("Oratorio:GitLab"));
builder.Services.Configure<DotCraftOptions>(builder.Configuration.GetSection("Oratorio:DotCraft"));
builder.Services.AddSingleton<IPostConfigureOptions<DotCraftOptions>, DotCraftOptionsPostConfigure>();
builder.Services.Configure<OratorioAutomationOptions>(builder.Configuration.GetSection("Oratorio:Automation"));
builder.Services.PostConfigure<OratorioAutomationOptions>(options =>
{
    if (File.Exists(serverConfigurationOverlayPath))
    {
        return;
    }

    if (options.AutoDispatchAllowLabels.Length == 0)
    {
        options.AutoDispatchAllowLabels = ["oratorio:auto"];
    }

    if (options.AutoDispatchBlockLabels.Length == 0)
    {
        options.AutoDispatchBlockLabels = ["blocked", "on hold", "needs-design", "needs-human"];
    }
});
builder.Services.AddHttpClient("GitHub");
builder.Services.AddHttpClient("GitLab");
builder.Services.AddHttpClient("DotCraftHub");
builder.Services.AddSingleton<IConfigurationSecretProtector, ConfigurationSecretProtector>();
builder.Services.AddSingleton<IGitHubCredentialResolver, GitHubCredentialResolver>();
builder.Services.AddSingleton<IGitHubInstallationResolver, GitHubInstallationResolver>();
builder.Services.AddSingleton<IGitHubTokenProvider, GitHubTokenProvider>();
builder.Services.AddSingleton<IGitHubApiClient, GitHubApiClient>();
builder.Services.AddSingleton<GitHubSyncCoordinator>();
builder.Services.AddSingleton<IGitLabCredentialResolver, GitLabCredentialResolver>();
builder.Services.AddSingleton<IGitLabApiClient, GitLabApiClient>();
builder.Services.AddSingleton<GitLabSyncCoordinator>();
builder.Services.AddScoped<ISourceProvider, GitHubSourceProvider>();
builder.Services.AddScoped<ISourceProvider, GitLabSourceProvider>();
builder.Services.AddScoped<SourceProviderRegistry>();
builder.Services.AddScoped<SourceProviderService>();
builder.Services.AddScoped<SourceSyncSchedulerService>();
builder.Services.AddSingleton<IDotCraftWorkspaceResolver, DotCraftWorkspaceResolver>();
builder.Services.AddSingleton<IDotCraftAppServerEndpointResolver, DotCraftAppServerEndpointResolver>();
builder.Services.AddSingleton<IDotCraftAppServerProcessManager, DotCraftAppServerProcessManager>();
builder.Services.AddSingleton<IDotCraftAppServerClientFactory, DotCraftAppServerClientFactory>();
builder.Services.AddSingleton<IGitTransportCredentialProvider, GitTransportCredentialProvider>();
builder.Services.AddSingleton<IWorktreeManager, WorktreeManager>();
builder.Services.AddSingleton<IGitDeliveryClient, GitDeliveryClient>();
builder.Services.AddSingleton<DotCraftStatusService>();
builder.Services.AddSingleton<WorkspaceInventoryService>();
builder.Services.AddScoped<OratorioService>();
builder.Services.AddScoped<GitHubSourceService>();
builder.Services.AddSingleton<GitHubCommentCommandParser>();
builder.Services.AddScoped<GitHubCommentCommandIntakeService>();
builder.Services.AddScoped<GitHubCommentCommandProcessor>();
builder.Services.AddScoped<GitLabSourceService>();
builder.Services.AddScoped<GitHubWriteService>();
builder.Services.AddScoped<GitLabWriteService>();
builder.Services.AddScoped<IReviewLocalDiffProvider, GitReviewLocalDiffProvider>();
builder.Services.AddScoped<IReviewDiffProvider, ReviewDiffProvider>();
builder.Services.AddScoped<ReviewDraftService>();
builder.Services.AddScoped<ReviewFindingResolutionService>();
builder.Services.AddScoped<ImplementationDraftService>();
builder.Services.AddScoped<FollowUpDraftService>();
builder.Services.AddScoped<DiscussionTurnService>();
builder.Services.AddScoped<SettingsDiagnosticsService>();
builder.Services.AddScoped<ServerConfigurationService>();
builder.Services.AddScoped<TaskShortIdAllocator>();
builder.Services.AddScoped<TaskBoardPlacementService>();
builder.Services.AddScoped<AppServerPromptBuilder>();
builder.Services.AddSingleton<OratorioDynamicToolCatalog>();
builder.Services.AddScoped<AutoReviewDispatchService>();
builder.Services.AddScoped<ImplementationFollowUpDispatchService>();
builder.Services.AddSingleton<OratorioAppBindingService>();
builder.Services.AddSingleton<OratorioBindingMcpRuntime>();
builder.Services.AddSingleton<OratorioBoardSurfaceRuntime>();
builder.Services.AddSingleton<BoardEventHub>();
builder.Services.AddSingleton<DrawerStateService>();
builder.Services.AddSingleton<IAppServerRunCoordinator, AppServerRunCoordinator>();
if (builder.Environment.IsEnvironment("Testing"))
{
    builder.Services.AddHostedService<MockRunWorker>();
}
builder.Services.AddHostedService<GitHubSyncWorker>();
builder.Services.AddHostedService<GitHubCommentCommandWorker>();
builder.Services.AddHostedService<GitLabSyncWorker>();
builder.Services.AddHostedService<SourceSyncSchedulerWorker>();
builder.Services.AddHostedService<AppServerRunWorker>();
builder.Services.AddHostedService<DiscussionTurnWorker>();
builder.Services.AddHostedService<ImplementationAutoDispatchWorker>();
builder.Services.AddHostedService<AutoReviewDispatchWorker>();
builder.Services.AddHostedService<ImplementationFollowUpDispatchWorker>();
builder.Services.AddHostedService<WorktreeCleanupWorker>();

var databasePath = ResolveDatabasePath(builder);
Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
builder.Services.AddDbContext<OratorioDbContext>(options => options.UseSqlite($"Data Source={databasePath}"));
builder.Services.AddSingleton(new OratorioDotCraftBindingStore(
    Path.Combine(Path.GetDirectoryName(databasePath)!, "dotcraft-binding.json")));
builder.Services.AddHostedService<OratorioAppBindingReannounceWorker>();

var app = builder.Build();

app.UseWebSockets();
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments(
            OratorioBoardSurfaceRuntime.SurfacePath,
            out var remainingPath))
    {
        var surface = context.RequestServices.GetRequiredService<OratorioBoardSurfaceRuntime>();
        if (!surface.Authorize(context.Request.Headers.Authorization.ToString()))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        context.Items["oratorio.surface-authorized"] = true;
        context.Request.Path = new PathString("/api/v1").Add(remainingPath);
    }

    await next();
});
app.UseRouting();
app.UseCors("DesktopRenderer");
var managedServiceToken = app.Configuration["DOTCRAFT_MANAGED_SERVICE_TOKEN"];
app.Use((context, next) => AuthorizeManagedServiceApiAsync(context, next, managedServiceToken));
app.Use(async (context, next) =>
{
    try
    {
        await next();
    }
    catch (OratorioApiException ex)
    {
        context.Response.StatusCode = ex.StatusCode;
        await context.Response.WriteAsJsonAsync(ex.ToResponse());
    }
});

using (var scope = app.Services.CreateScope())
{
    await scope.ServiceProvider.GetRequiredService<OratorioDbContext>().Database.EnsureCreatedAsync();

}

app.MapGet("/health", () => new
{
    service = "oratorio",
    status = "ok",
    version = typeof(Program).Assembly.GetName().Version?.ToString(),
    time = DateTimeOffset.UtcNow
});

app.MapOratorioApi();
app.MapBoardStream();
app.MapMethods("/dotcraft/bindings/{bindingId}/mcp", ["POST", "DELETE"],
    (HttpContext context, string bindingId, OratorioBindingMcpRuntime runtime) =>
        runtime.HandleAsync(context, bindingId));

RegisterStartupBanner(app);

app.Run();

static void RegisterStartupBanner(WebApplication app)
{
    app.Lifetime.ApplicationStarted.Register(() =>
    {
        var addresses = ResolveDisplayAddresses(app).ToArray();
        var primaryAddress = addresses.FirstOrDefault() ?? "http://localhost:5000";
        var managedServiceId = Environment.GetEnvironmentVariable("DOTCRAFT_MANAGED_SERVICE_ID");
        if (!string.IsNullOrWhiteSpace(managedServiceId))
        {
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                type = "dotcraft.managed-service.ready",
                serviceId = managedServiceId,
                endpoint = primaryAddress.TrimEnd('/'),
                version = typeof(Program).Assembly.GetName().Version?.ToString()
            }));
            return;
        }

        Console.WriteLine();
        Console.WriteLine("=====================================");
        Console.WriteLine(" Oratorio is running");
        Console.WriteLine("=====================================");
        Console.WriteLine($" API:      {AppendPath(primaryAddress, "/api/v1/status")}");
        Console.WriteLine($" Health:   {AppendPath(primaryAddress, "/health")}");
        Console.WriteLine(" Mode:     Headless server");
        if (addresses.Length > 1)
        {
            Console.WriteLine($" Listening: {string.Join(", ", addresses)}");
        }

        Console.WriteLine("=====================================");
        Console.WriteLine();
    });
}

static IEnumerable<string> ResolveDisplayAddresses(WebApplication app)
{
    var serverAddresses = app.Services
        .GetService<IServer>()?
        .Features
        .Get<IServerAddressesFeature>()?
        .Addresses;

    var addresses = serverAddresses is { Count: > 0 } ? serverAddresses : app.Urls;
    return addresses
        .Select(NormalizeDisplayAddress)
        .Where(address => !string.IsNullOrWhiteSpace(address))
        .Distinct(StringComparer.OrdinalIgnoreCase);
}

static string NormalizeDisplayAddress(string address)
{
    if (!Uri.TryCreate(address, UriKind.Absolute, out var uri))
    {
        return address
            .Replace("://0.0.0.0", "://localhost", StringComparison.OrdinalIgnoreCase)
            .Replace("://[::]", "://localhost", StringComparison.OrdinalIgnoreCase)
            .Replace("://*", "://localhost", StringComparison.OrdinalIgnoreCase)
            .Replace("://+", "://localhost", StringComparison.OrdinalIgnoreCase);
    }

    var host = uri.Host is "0.0.0.0" or "::" or "*" or "+"
        ? "localhost"
        : uri.Host;
    return new UriBuilder(uri) { Host = host }.Uri.GetLeftPart(UriPartial.Authority);
}

static string AppendPath(string baseAddress, string path) =>
    $"{baseAddress.TrimEnd('/')}/{path.TrimStart('/')}";

static void ConfigureDefaultLogging(WebApplicationBuilder builder)
{
    // Defaults live in code (no appsettings.json) so the published server is a single
    // self-contained file, matching DotCraft. These keep the headless backend quiet by
    // default; runtime Oratorio:* options come from the configuration overlay.
    builder.Logging.SetMinimumLevel(LogLevel.Warning);
    builder.Logging.AddFilter("Microsoft.Hosting.Lifetime", LogLevel.Information);
    builder.Logging.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);
    builder.Logging.AddFilter("Microsoft.EntityFrameworkCore.Database.Command", LogLevel.Warning);
    builder.Logging.AddFilter("Oratorio.Server", LogLevel.Information);
}

static string ResolveServerConfigurationOverlayPath(WebApplicationBuilder builder)
{
    var managedStateRoot = Environment.GetEnvironmentVariable("DOTCRAFT_MANAGED_SERVICE_STATE_ROOT");
    if (!string.IsNullOrWhiteSpace(managedStateRoot))
    {
        return OratorioStatePaths.ResolveDefaultConfigurationOverlayPath(
            builder.Environment.ContentRootPath,
            managedStateRoot);
    }

    var configured = builder.Configuration["Oratorio:Settings:ConfigPath"];
    return string.IsNullOrWhiteSpace(configured)
        ? OratorioStatePaths.ResolveDefaultConfigurationOverlayPath(
            builder.Environment.ContentRootPath)
        : Path.GetFullPath(configured);
}

static string ResolveDatabasePath(WebApplicationBuilder builder)
{
    var managedStateRoot = Environment.GetEnvironmentVariable("DOTCRAFT_MANAGED_SERVICE_STATE_ROOT");
    if (!string.IsNullOrWhiteSpace(managedStateRoot))
    {
        return OratorioStatePaths.ResolveDefaultDatabasePath(
            builder.Environment.ContentRootPath,
            managedStateRoot);
    }

    var configured = builder.Configuration["Oratorio:DatabasePath"];
    if (!string.IsNullOrWhiteSpace(configured))
    {
        return Path.GetFullPath(configured);
    }

    return OratorioStatePaths.ResolveDefaultDatabasePath(builder.Environment.ContentRootPath);
}

static void ApplyManagedServiceHosting(WebApplicationBuilder builder)
{
    var url = Environment.GetEnvironmentVariable("DOTCRAFT_MANAGED_SERVICE_URL");
    if (!string.IsNullOrWhiteSpace(url))
        builder.WebHost.UseUrls(url);
}

static async Task AuthorizeManagedServiceApiAsync(HttpContext context, RequestDelegate next, string? expectedToken)
{
    if (string.IsNullOrWhiteSpace(expectedToken)
        || !RequiresManagedServiceBearer(context.Request.Path)
        || context.Items.ContainsKey("oratorio.surface-authorized"))
    {
        await next(context);
        return;
    }

    var authorization = context.Request.Headers.Authorization.ToString();
    const string prefix = "Bearer ";
    var authorized = authorization.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
        && string.Equals(authorization[prefix.Length..], expectedToken, StringComparison.Ordinal);
    if (authorized)
    {
        await next(context);
        return;
    }

    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
    await context.Response.WriteAsJsonAsync(new
    {
        error = new
        {
            code = "oratorio.serviceUnauthorized",
            message = "Missing or invalid Oratorio service token."
        }
    });
}

static bool RequiresManagedServiceBearer(PathString path)
{
    if (!path.StartsWithSegments("/api/v1"))
        return false;

    return !path.StartsWithSegments("/api/v1/sources/github/webhook")
        && !path.StartsWithSegments("/api/v1/sources/gitlab/webhook");
}

static bool IsDesktopRendererOrigin(string origin)
{
    if (string.Equals(origin, "null", StringComparison.OrdinalIgnoreCase) ||
        origin.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
    {
        return true;
    }

    if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri))
    {
        return false;
    }

    if (uri.Scheme is not ("http" or "https"))
    {
        return false;
    }

    return uri.IsLoopback && uri.Port is 5173 or 5174 or 5177;
}

public partial class Program;
