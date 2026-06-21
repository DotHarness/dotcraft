using System.Security.Cryptography;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Spectre.Console;
using DotCraft.Common;
using DotCraft.Hosting;

namespace DotCraft.Hub;

/// <summary>
/// Workspace-independent local Hub host.
/// </summary>
public sealed class HubHost : IDotCraftHost
{
    private readonly HubConfig _config;
    private readonly HubPaths _paths;
    private readonly string? _dotcraftBin;
    private readonly CancellationTokenSource _shutdownCts = new();
    private WebApplication? _app;
    private HubLockFile? _lockFile;
    private HubEventBus? _eventBus;
    private ManagedAppServerRegistry? _registry;
    private int _cleanupStarted;

    /// <summary>
    /// Creates a new Hub host.
    /// </summary>
    public HubHost(HubConfig config, HubPaths paths, string? dotcraftBin = null)
    {
        _config = config;
        _paths = paths;
        _dotcraftBin = dotcraftBin;
    }

    /// <inheritdoc />
    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        if (!HubLockFile.TryAcquire(_paths, out _lockFile, out var existingInfo))
        {
            var hint = existingInfo is null
                ? "Hub already running."
                : $"Hub already running at {existingInfo.ApiBaseUrl} (pid {existingInfo.Pid}).";
            AnsiConsole.MarkupLine($"[yellow][[Hub]][/] {Markup.Escape(hint)}");
            return;
        }

        var lockFile = _lockFile;
        if (lockFile is null)
            throw new InvalidOperationException("Hub lock acquisition returned no lock file.");

        if (existingInfo is not null && !existingInfo.IsOwnerProcessAlive())
        {
            AnsiConsole.MarkupLine("[grey][[Hub]][/] Recovered stale hub.lock");
        }

        var port = _config.Port == 0 ? HubPortAllocator.AllocateLoopbackPort() : _config.Port;
        var host = NormalizeHost(_config.Host);
        var apiBaseUrl = $"http://{host}:{port}";
        var token = CreateToken();
        var startedAt = DateTimeOffset.UtcNow;
        var binaryPath = ResolveHubBinaryPath();

        try
        {
            _eventBus = new HubEventBus();
            _registry = new ManagedAppServerRegistry(
                _eventBus,
                apiBaseUrl,
                token,
                _dotcraftBin,
                _paths.AppServersRegistryPath,
                _paths.RuntimeToolsPath);
            _registry.StartHealthChecks();
            _app = BuildApp(apiBaseUrl, token, startedAt, binaryPath, _registry, _eventBus);
            _app.Urls.Add(apiBaseUrl);
            await _app.StartAsync(cancellationToken);

            var lockInfo = new HubLockInfo(
                Pid: Environment.ProcessId,
                ApiBaseUrl: apiBaseUrl,
                Token: token,
                StartedAt: startedAt,
                Version: AppVersion.Informational,
                BinaryPath: binaryPath);
            lockFile.Publish(lockInfo);
            _eventBus.Publish("hub.started", data: new { apiBaseUrl, pid = Environment.ProcessId });

            AnsiConsole.MarkupLine($"[green][[Hub]][/] DotCraft Hub started at {Markup.Escape(apiBaseUrl)}");

            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _shutdownCts.Token);
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, linkedCts.Token);
            }
            catch (OperationCanceledException) when (linkedCts.IsCancellationRequested)
            {
            }
        }
        finally
        {
            await CleanupAsync();
        }
    }

    private WebApplication BuildApp(
        string apiBaseUrl,
        string token,
        DateTimeOffset startedAt,
        string? binaryPath,
        ManagedAppServerRegistry registry,
        HubEventBus events)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        var app = builder.Build();

        app.MapGet("/v1/status", () => Results.Json(CreateStatus(apiBaseUrl, startedAt, binaryPath), HubJson.Options));
        app.MapPost("/v1/shutdown", (HttpRequest request) =>
        {
            if (Unauthorized(request, token) is { } unauthorized)
                return unauthorized;

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

            var reader = events.Subscribe(ct);
            await HubEventBus.WriteSseAsync(response, reader, ct);
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
                PortManagement: true,
                Events: true,
                Notifications: true,
                Tray: false));

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

    private static async Task<IResult> ProtectedAsync(Func<Task<IResult>> action)
    {
        try
        {
            return await action();
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

        _eventBus?.Publish("hub.stopping", data: new { pid = Environment.ProcessId });
        if (_app is not null)
        {
            await _app.StopAsync(CancellationToken.None);
            await _app.DisposeAsync();
            _app = null;
        }

        if (_registry is not null)
        {
            await _registry.DisposeAsync();
            _registry = null;
        }

        _lockFile?.DeleteAfterDispose();
        _lockFile = null;
        _shutdownCts.Dispose();
        AnsiConsole.MarkupLine("[grey][[Hub]][/] Hub stopped");
    }
}

/// <summary>
/// Hub status response.
/// </summary>
public sealed record HubStatusResponse(
    string HubVersion,
    int Pid,
    DateTimeOffset StartedAt,
    string StatePath,
    string ApiBaseUrl,
    string? BinaryPath,
    HubCapabilities Capabilities);

/// <summary>
/// Hub capability flags.
/// </summary>
public sealed record HubCapabilities(
    bool AppServerManagement,
    bool PortManagement,
    bool Events,
    bool Notifications,
    bool Tray);

/// <summary>
/// Hub API error response.
/// </summary>
public sealed record HubErrorResponse(HubError Error);

/// <summary>
/// Hub API error payload.
/// </summary>
public sealed record HubError(string Code, string Message, object? Details);
