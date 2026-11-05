using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Spectre.Console;
using DotCraft.Common;
using DotCraft.Hosting;
using DotCraft.Logging;

namespace DotCraft.Hub;

/// <summary>
/// Workspace-independent local Hub host.
/// </summary>
public sealed class HubHost : IDotCraftHost
{
    private static readonly TimeSpan LockHandoffTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan LockRetryInterval = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan OwnerProbeTimeout = TimeSpan.FromSeconds(2);

    private readonly HubConfig _config;
    private readonly HubPaths _paths;
    private readonly string? _dotcraftBin;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<HubHost> _logger;
    private readonly CancellationTokenSource _shutdownCts = new();
    private WebApplication? _app;
    private HubLockFile? _lockFile;
    private HubEventBus? _eventBus;
    private ManagedAppServerRegistry? _registry;
    private ProjectRegistry? _projects;
    private ManagedLocalServiceRegistry? _serviceRegistry;
    private SatelliteConnectionManager? _satellites;
    private MobileGateway? _mobile;
    private int _cleanupStarted;
    private Task? _optionalListenersTask;

    internal Func<CancellationToken, Task>? OptionalListenersStartup { get; init; }

    /// <summary>
    /// Creates a new Hub host.
    /// </summary>
    public HubHost(
        HubConfig config,
        HubPaths paths,
        string? dotcraftBin = null,
        ILoggerFactory? loggerFactory = null)
    {
        _config = config;
        _paths = paths;
        _dotcraftBin = dotcraftBin;
        _loggerFactory = loggerFactory ?? NullLoggerFactory.Instance;
        _logger = _loggerFactory.CreateLogger<HubHost>();
    }

    /// <inheritdoc />
    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        using var moduleScope = _logger.BeginScope(new Dictionary<string, object?>
        {
            ["Module"] = "Hub"
        });
        if (!await AcquireLockAsync(cancellationToken))
            return;

        var lockFile = _lockFile;
        if (lockFile is null)
            throw new InvalidOperationException("Hub lock acquisition returned no lock file.");

        var port = _config.Port == 0 ? HubPortAllocator.AllocateLoopbackPort() : _config.Port;
        var host = NormalizeHost(_config.Host);
        var apiBaseUrl = $"http://{host}:{port}";
        var token = CreateToken();
        using var currentProcess = Process.GetCurrentProcess();
        var startedAt = new DateTimeOffset(currentProcess.StartTime.ToUniversalTime());
        var binaryPath = ResolveHubBinaryPath();

        try
        {
            lockFile.Publish(new HubLockInfo(
                Environment.ProcessId, apiBaseUrl, token, startedAt, AppVersion.Informational, binaryPath));
            _eventBus = new HubEventBus();
            _registry = new ManagedAppServerRegistry(
                _eventBus,
                apiBaseUrl,
                token,
                _dotcraftBin,
                _paths.AppServersRegistryPath,
                _paths.RuntimeToolsPath,
                _loggerFactory.CreateLogger<ManagedAppServerRegistry>());
            _projects = new ProjectRegistry(_paths);
            _serviceRegistry = new ManagedLocalServiceRegistry(
                ManagedLocalServiceDefinitions.CreateBuiltIns(_paths),
                _loggerFactory.CreateLogger<ManagedLocalServiceRegistry>());
            using var stopping = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _shutdownCts.Token);
            using var stopRegistration = stopping.Token.Register(() =>
            {
                _registry.BeginShutdown();
                _serviceRegistry.BeginShutdown();
            });
            _satellites = new SatelliteConnectionManager(
                new SatelliteRegistry(_paths.SatellitesPath),
                _config,
                _eventBus,
                _loggerFactory);
            _mobile = new MobileGateway(
                new MobileRegistry(_paths.MobilePath),
                _config,
                _paths,
                _eventBus,
                _registry,
                _projects,
                _loggerFactory);
            _app = BuildApp(apiBaseUrl, token, startedAt, binaryPath, _registry, _serviceRegistry, _eventBus);
            _app.Urls.Add(apiBaseUrl);
            await _app.StartAsync(cancellationToken);
            _optionalListenersTask = Task.Run(() => InitializeOptionalListenersAsync(_shutdownCts.Token));
            _eventBus.Publish("hub.started", data: new { apiBaseUrl, pid = Environment.ProcessId });

            AnsiConsole.MarkupLine($"[green][[Hub]][/] DotCraft Hub started at {Markup.Escape(apiBaseUrl)}");
            _logger.LogInformation("Hub started at {ApiBaseUrl}", apiBaseUrl);

            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, stopping.Token);
            }
            catch (OperationCanceledException) when (stopping.IsCancellationRequested)
            {
            }
        }
        finally
        {
            await CleanupAsync();
        }
    }

    private async Task<bool> AcquireLockAsync(CancellationToken cancellationToken)
    {
        if (HubLockFile.TryAcquire(_paths, out _lockFile, out var owner))
        {
            if (owner is not null)
            {
                AnsiConsole.MarkupLine("[grey][[Hub]][/] Recovered stale hub.lock");
                _logger.LogInformation("Recovered stale Hub lock");
            }

            return true;
        }

        if (owner is not null && await OwnerAnswersAsync(owner, cancellationToken))
        {
            var hint = $"Hub already running at {owner.ApiBaseUrl} (pid {owner.Pid}).";
            AnsiConsole.MarkupLine($"[yellow][[Hub]][/] {Markup.Escape(hint)}");
            _logger.LogWarning("Hub lock is already held: {Hint}", hint);
            return false;
        }

        _logger.LogInformation("Waiting for the Hub lock owner {Pid} to become ready or release ownership", owner?.Pid);
        var deadline = DateTimeOffset.UtcNow + LockHandoffTimeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(LockRetryInterval, cancellationToken);
            if (HubLockFile.TryAcquire(_paths, out _lockFile, out var current))
                return true;

            if (current is not null && await OwnerAnswersAsync(current, cancellationToken))
                return false;
        }

        _logger.LogWarning("Hub ownership did not become available within {Timeout}", LockHandoffTimeout);
        return false;
    }

    private async Task InitializeOptionalListenersAsync(CancellationToken cancellationToken)
    {
        if (OptionalListenersStartup is { } initialize)
        {
            await initialize(cancellationToken);
            return;
        }
        try
        {
            await _satellites!.StartForExistingPeersAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception ex) { _logger.LogWarning(ex, "Satellite listener startup failed"); }
        try
        {
            await _mobile!.StartIfEnabledAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception ex) { _logger.LogWarning(ex, "Mobile listener startup failed"); }
    }

    private static bool RequiresRunningHub(PathString path) =>
        path == "/v1/appservers/ensure" || path == "/v1/appservers/restart"
        || path == "/v1/services/ensure" || path == "/v1/services/restart";

    private static IResult StoppingResult() => Results.Json(
        new HubErrorResponse(new HubError("hubStopping", "DotCraft Hub is stopping.", null)),
        HubJson.Options, statusCode: StatusCodes.Status503ServiceUnavailable);

    private static async Task<bool> OwnerAnswersAsync(HubLockInfo owner, CancellationToken cancellationToken)
    {
        try
        {
            using var http = new HttpClient { Timeout = OwnerProbeTimeout };
            using var response = await http.GetAsync($"{owner.ApiBaseUrl}/v1/status", cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }

    private WebApplication BuildApp(
        string apiBaseUrl,
        string token,
        DateTimeOffset startedAt,
        string? binaryPath,
        ManagedAppServerRegistry registry,
        ManagedLocalServiceRegistry serviceRegistry,
        HubEventBus events)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.Logging.AddProvider(new NonOwningLoggerProvider(_loggerFactory));
        var app = builder.Build();
        app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(30) });
        app.Use(async (context, next) =>
        {
            if (!RequiresRunningHub(context.Request.Path))
            {
                await next(context);
                return;
            }
            if (_shutdownCts.IsCancellationRequested)
            {
                await StoppingResult().ExecuteAsync(context);
                return;
            }
            using var work = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted, _shutdownCts.Token);
            context.RequestAborted = work.Token;
            await next(context);
        });

        app.MapGet("/v1/status", () => Results.Json(CreateStatus(apiBaseUrl, startedAt, binaryPath), HubJson.Options));
        app.MapPost("/v1/shutdown", async (HttpRequest request) =>
        {
            if (Unauthorized(request, token) is { } unauthorized)
                return unauthorized;

            if (request.HttpContext.Features.Get<IHttpRequestBodyDetectionFeature>()?.CanHaveBody == true)
            {
                using var body = await JsonDocument.ParseAsync(request.Body, cancellationToken: request.HttpContext.RequestAborted);
                if (body.RootElement.TryGetProperty("expectedPid", out var expectedPid)
                    && (!expectedPid.TryGetInt32(out var pid) || pid != Environment.ProcessId))
                    return Results.Json(new HubErrorResponse(new HubError("hubIdentityMismatch", "Hub process identity changed.", null)),
                        HubJson.Options, statusCode: StatusCodes.Status409Conflict);
            }
            _shutdownCts.Cancel();
            return Results.Json(new { ok = true }, HubJson.Options);
        });
        app.MapPost("/v1/appservers/ensure", async (HttpRequest request, EnsureAppServerRequest body, CancellationToken ct) =>
        {
            if (Unauthorized(request, token) is { } unauthorized)
                return unauthorized;

            return await ProtectedAsync(async () =>
                Results.Json(await registry.EnsureAsync(body, ct), HubJson.Options));
        });
        app.MapGet("/v1/appservers", (HttpRequest request) =>
        {
            if (Unauthorized(request, token) is { } unauthorized)
                return unauthorized;

            return Protected(() => Results.Json(registry.List(), HubJson.Options));
        });
        app.MapGet("/v1/appservers/by-workspace", (HttpRequest request) =>
        {
            if (Unauthorized(request, token) is { } unauthorized)
                return unauthorized;

            return Protected(() =>
            {
                var workspacePath = request.Query["path"].FirstOrDefault();
                return Results.Json(registry.GetByWorkspace(workspacePath ?? string.Empty), HubJson.Options);
            });
        });
        app.MapPost("/v1/appservers/stop", async (HttpRequest request, WorkspacePathRequest body, CancellationToken ct) =>
        {
            if (Unauthorized(request, token) is { } unauthorized)
                return unauthorized;

            return await ProtectedAsync(async () =>
                Results.Json(await registry.StopAsync(body.WorkspacePath, ct), HubJson.Options));
        });
        app.MapPost("/v1/appservers/restart", async (HttpRequest request, WorkspacePathRequest body, CancellationToken ct) =>
        {
            if (Unauthorized(request, token) is { } unauthorized)
                return unauthorized;

            return await ProtectedAsync(async () =>
                Results.Json(await registry.RestartAsync(body.WorkspacePath, body.RuntimeTools, ct), HubJson.Options));
        });
        app.MapPost("/v1/services/ensure", async (HttpRequest request, EnsureManagedServiceRequest body, CancellationToken ct) =>
        {
            if (Unauthorized(request, token) is { } unauthorized)
                return unauthorized;

            return await ProtectedAsync(async () =>
                Results.Json(await serviceRegistry.EnsureAsync(body, ct), HubJson.Options));
        });
        app.MapGet("/v1/services/by-id", (HttpRequest request) =>
        {
            if (Unauthorized(request, token) is { } unauthorized)
                return unauthorized;

            return Protected(() =>
                Results.Json(serviceRegistry.Get(request.Query["id"].FirstOrDefault() ?? string.Empty), HubJson.Options));
        });
        app.MapPost("/v1/services/stop", async (HttpRequest request, ManagedServiceRequest body, CancellationToken ct) =>
        {
            if (Unauthorized(request, token) is { } unauthorized)
                return unauthorized;

            return await ProtectedAsync(async () =>
                Results.Json(await serviceRegistry.StopAsync(body.ServiceId, ct), HubJson.Options));
        });
        app.MapPost("/v1/services/restart", async (HttpRequest request, ManagedServiceRequest body, CancellationToken ct) =>
        {
            if (Unauthorized(request, token) is { } unauthorized)
                return unauthorized;

            return await ProtectedAsync(async () =>
                Results.Json(await serviceRegistry.RestartAsync(body, ct), HubJson.Options));
        });
        app.MapGet("/v1/events", async (HttpRequest request, HttpResponse response, CancellationToken ct) =>
        {
            if (!IsAuthorized(request, token))
            {
                response.StatusCode = StatusCodes.Status401Unauthorized;
                await response.WriteAsJsonAsync(
                    new HubErrorResponse(new HubError("unauthorized", "Missing or invalid Hub token.", null)),
                    HubJson.Options,
                    ct);
                return;
            }

            // Kestrel waits for open requests on stop, so streams must end when the Hub starts stopping.
            using var streamCts = CancellationTokenSource.CreateLinkedTokenSource(ct, app.Lifetime.ApplicationStopping);
            var reader = events.Subscribe(streamCts.Token);
            try
            {
                await HubEventBus.WriteSseAsync(response, reader, streamCts.Token);
            }
            catch (OperationCanceledException) when (streamCts.IsCancellationRequested)
            {
            }
        });
        app.MapPost("/v1/notifications/request", (HttpRequest request, HubNotificationRequest body) =>
        {
            if (Unauthorized(request, token) is { } unauthorized)
                return unauthorized;

            return Protected(() =>
            {
                if (string.IsNullOrWhiteSpace(body.Title))
                    throw new HubProtocolException(
                        "invalidNotification",
                        "Notification title is required.",
                        StatusCodes.Status400BadRequest);

                if (string.IsNullOrWhiteSpace(body.Kind))
                    body.Kind = "notification";

                body.Severity = NormalizeNotificationSeverity(body.Severity);
                events.Publish("notification.requested", body.WorkspacePath, body);
                return Results.Json(new { accepted = true }, HubJson.Options);
            });
        });
        HubSatelliteApi.Map(
            app,
            _config,
            _satellites!,
            events,
            request => Unauthorized(request, token),
            ProtectedAsync);
        HubProjectsApi.Map(
            app,
            _projects!,
            registry,
            events,
            request => Unauthorized(request, token),
            ProtectedAsync);
        HubMobileApi.Map(
            app,
            _mobile!,
            request => Unauthorized(request, token),
            ProtectedAsync);

        return app;
    }

    private HubStatusResponse CreateStatus(string apiBaseUrl, DateTimeOffset startedAt, string? binaryPath)
        => new(
            HubVersion: AppVersion.Informational,
            Pid: Environment.ProcessId,
            StartedAt: startedAt,
            StatePath: _paths.HubStatePath,
            ApiBaseUrl: apiBaseUrl,
            BinaryPath: binaryPath,
            Capabilities: new HubCapabilities(
                AppServerManagement: true,
                ManagedServiceManagement: true,
                PortManagement: true,
                Events: true,
                Notifications: true,
                Tray: false,
                Satellites: true,
                Mobile: true));

    private static bool IsAuthorized(HttpRequest request, string token)
    {
        var authorization = request.Headers.Authorization.ToString();
        const string prefix = "Bearer ";
        return authorization.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
               && string.Equals(authorization[prefix.Length..], token, StringComparison.Ordinal);
    }

    private static IResult? Unauthorized(HttpRequest request, string token)
        => IsAuthorized(request, token)
            ? null
            : Results.Json(
                new HubErrorResponse(new HubError("unauthorized", "Missing or invalid Hub token.", null)),
                HubJson.Options,
                statusCode: StatusCodes.Status401Unauthorized);

    private static IResult Protected(Func<IResult> action)
    {
        try
        {
            return action();
        }
        catch (HubProtocolException ex)
        {
            return ToErrorResult(ex);
        }
        catch (Exception ex)
        {
            return ToUnexpectedErrorResult(ex);
        }
    }

    private async Task<IResult> ProtectedAsync(Func<Task<IResult>> action)
    {
        try
        {
            return await action();
        }
        catch (OperationCanceledException) when (_shutdownCts.IsCancellationRequested)
        {
            return StoppingResult();
        }
        catch (HubProtocolException ex)
        {
            return ToErrorResult(ex);
        }
        catch (Exception ex)
        {
            return ToUnexpectedErrorResult(ex);
        }
    }

    private static IResult ToErrorResult(HubProtocolException ex)
        => Results.Json(
            new HubErrorResponse(new HubError(ex.Code, ex.Message, ex.Details)),
            HubJson.Options,
            statusCode: ex.StatusCode);

    private static IResult ToUnexpectedErrorResult(Exception ex)
        => Results.Json(
            new HubErrorResponse(new HubError(
                "hubInternalError",
                "Hub encountered an unexpected internal error.",
                new { type = ex.GetType().Name })),
            HubJson.Options,
            statusCode: StatusCodes.Status500InternalServerError);

    private static string NormalizeHost(string host)
        => string.IsNullOrWhiteSpace(host) ? "127.0.0.1" : host.Trim();

    private string? ResolveHubBinaryPath()
    {
        var candidate = _dotcraftBin;
        if (string.IsNullOrWhiteSpace(candidate))
        {
            var processPath = Environment.ProcessPath;
            candidate = !string.IsNullOrWhiteSpace(processPath) &&
                        Path.GetFileNameWithoutExtension(processPath).Equals("dotcraft", StringComparison.OrdinalIgnoreCase)
                ? processPath
                : typeof(HubHost).Assembly.Location;
        }

        if (string.IsNullOrWhiteSpace(candidate))
            return null;

        try
        {
            return Path.GetFullPath(candidate);
        }
        catch
        {
            return candidate;
        }
    }

    private static string CreateToken()
    {
        Span<byte> bytes = stackalloc byte[32];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes);
    }

    private static string NormalizeNotificationSeverity(string? severity)
        => severity?.Trim().ToLowerInvariant() switch
        {
            "success" => "success",
            "warning" => "warning",
            "error" => "error",
            _ => "info"
        };

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await CleanupAsync();
    }

    private async Task CleanupAsync()
    {
        if (Interlocked.Exchange(ref _cleanupStarted, 1) != 0)
            return;

        try
        {
            _shutdownCts.Cancel();
        }
        catch
        {
            // Best-effort shutdown only.
        }

        _registry?.BeginShutdown();
        _serviceRegistry?.BeginShutdown();
        _eventBus?.Publish("hub.stopping", data: new { pid = Environment.ProcessId });
        if (_optionalListenersTask is { } listeners)
        {
            try { await listeners; }
            catch (OperationCanceledException) { }
            catch (Exception ex) { _logger.LogWarning(ex, "Optional Hub listener startup failed"); }
        }
        if (_satellites is not null)
        {
            await _satellites.DisposeAsync();
            _satellites = null;
        }

        if (_app is not null)
        {
            await _app.StopAsync(CancellationToken.None);
            await _app.DisposeAsync();
            _app = null;
        }

        if (_mobile is not null)
        {
            await _mobile.DisposeAsync();
            _mobile = null;
        }

        var registry = _registry;
        var serviceRegistry = _serviceRegistry;
        _registry = null;
        _serviceRegistry = null;
        await Task.WhenAll(
            registry?.DisposeAsync().AsTask() ?? Task.CompletedTask,
            serviceRegistry?.DisposeAsync().AsTask() ?? Task.CompletedTask);

        _lockFile?.DeleteAfterDispose();
        _lockFile = null;
        _shutdownCts.Dispose();
        AnsiConsole.MarkupLine("[grey][[Hub]][/] Hub stopped");
        _logger.LogInformation("Hub stopped");
    }
}
