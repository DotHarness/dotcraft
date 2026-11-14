using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using DotCraft.CodexGateway.Relay;
using System.Text.Json;
using DotCraft.CodexGateway.Workspace;
using DotCraft.Sdk;
using DotCraft.Sdk.Hub;
using DotCraft.Sdk.Wire;

namespace DotCraft.CodexGateway.Protocol;

public sealed class CodexHandshakeSession(
    RemoteControlHostOptions options,
    Func<RemoteControlHostStatus> status,
    Func<JsonElement, CancellationToken, Task> send,
    Action<string>? observedMethod = null,
    Func<string, JsonElement, CancellationToken, Task<object?>>? requestHandler = null) : IRemoteControlRpcSession
{
    private readonly string _stateDirectory = Path.GetFullPath(options.StateDirectory);
    private bool _initialized;
    private bool _experimentalApi;
    private readonly HashSet<string> _optOutNotifications = new(StringComparer.Ordinal);

    public async Task ReceiveAsync(JsonElement message, CancellationToken cancellationToken)
    {
        if (!message.TryGetProperty("method", out var methodValue) || methodValue.ValueKind != JsonValueKind.String)
            return;

        var method = methodValue.GetString()!;
        observedMethod?.Invoke(method);
        if (!message.TryGetProperty("id", out var id))
            return;

        if (method == "initialize")
        {
            if (_initialized)
            {
                await ErrorAsync(id, -32600, "Already initialized", cancellationToken);
                return;
            }

            if (!message.TryGetProperty("params", out var parameters)
                || parameters.ValueKind != JsonValueKind.Object
                || !parameters.TryGetProperty("clientInfo", out var clientInfo)
                || clientInfo.ValueKind != JsonValueKind.Object
                || !clientInfo.TryGetProperty("name", out var name) || name.ValueKind != JsonValueKind.String
                || !clientInfo.TryGetProperty("version", out var version) || version.ValueKind != JsonValueKind.String)
            {
                await ErrorAsync(id, -32602, "initialize requires clientInfo.name and clientInfo.version", cancellationToken);
                return;
            }

            _experimentalApi = parameters.TryGetProperty("capabilities", out var capabilities)
                && capabilities.ValueKind == JsonValueKind.Object
                && capabilities.TryGetProperty("experimentalApi", out var experimental)
                && experimental.ValueKind == JsonValueKind.True;
            if (capabilities.ValueKind == JsonValueKind.Object
                && capabilities.TryGetProperty("optOutNotificationMethods", out var methods)
                && methods.ValueKind == JsonValueKind.Array)
                foreach (var notification in methods.EnumerateArray())
                    _optOutNotifications.Add(notification.GetString()!);
            _initialized = true;
            await ResultAsync(id, new
            {
                userAgent = "dotcraft_codex_gateway/" + options.AppServerVersion,
                codexHome = _stateDirectory,
                platformFamily = OperatingSystem.IsWindows() ? "windows" : "unix",
                platformOs = PlatformOs()
            }, cancellationToken);
            return;
        }

        if (!_initialized)
        {
            await ErrorAsync(id, -32600, "Not initialized", cancellationToken);
            return;
        }

        if (requestHandler is not null)
        {
            try
            {
                var parameters = message.TryGetProperty("params", out var value) && value.ValueKind == JsonValueKind.Object ? value : EmptyParams;
                CodexGatewayDiagnostics.Request(observedMethod, method, parameters);
                var started = Stopwatch.GetTimestamp();
                if (await requestHandler(method, parameters, cancellationToken) is { } result)
                {
                    await ResultAsync(id, result, cancellationToken, method, started);
                    return;
                }
            }
            catch (ProjectRequiredException exception)
            {
                await send(JsonSerializer.SerializeToElement(new
                {
                    id, error = new { code = -32602, message = exception.Message, data = new { code = "projectRequired" } }
                }), cancellationToken);
                return;
            }
            catch (JsonRpcException exception)
            {
                observedMethod?.Invoke(method + ":sourceRpcError");
                await ErrorAsync(id, exception.RpcCode, "The source workspace rejected the request.", cancellationToken);
                return;
            }
            catch (ArgumentException exception)
            {
                await ErrorAsync(id, -32602, exception.Message, cancellationToken);
                return;
            }
            catch (JsonException)
            {
                await ErrorAsync(id, -32602, "The request or source response contains invalid JSON data.", cancellationToken);
                return;
            }
            catch (GatewayHistoryLimitException exception)
            {
                observedMethod?.Invoke(method + ":historyReadLimitExceeded");
                await send(JsonSerializer.SerializeToElement(new
                {
                    id, error = new
                    {
                        code = -32603,
                        message = "The requested full history exceeds the gateway response limit. Use paginated history.",
                        data = new { code = "historyReadLimitExceeded", maxBytes = exception.MaxBytes }
                    }
                }), cancellationToken);
                return;
            }
            catch (JsonRpcMessageTooLargeException exception)
            {
                observedMethod?.Invoke(method + ":sourceMessageTooLarge");
                await send(JsonSerializer.SerializeToElement(new
                {
                    id, error = new
                    {
                        code = -32603, message = "The source response exceeds its transport message limit.",
                        data = new { code = "sourceMessageTooLarge", maxBytes = exception.LimitBytes }
                    }
                }), cancellationToken);
                return;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                var category = exception is IOException or HttpRequestException or System.Net.WebSockets.WebSocketException
                    or DotCraftException or HubClientException ? "sourceUnavailable" : "sourceRequestFailed";
                observedMethod?.Invoke(method + ":" + category);
                await send(JsonSerializer.SerializeToElement(new
                {
                    id, error = new
                    {
                        code = -32603,
                        message = category == "sourceUnavailable" ? "The source workspace connection is unavailable." :
                            "The source workspace request could not be completed.",
                        data = new { code = category }
                    }
                }), cancellationToken);
                return;
            }
        }

        switch (method)
        {
            case "remoteControl/status/read":
                if (!_experimentalApi)
                {
                    await ErrorAsync(id, -32600, "remoteControl/status/read requires experimentalApi capability", cancellationToken);
                    break;
                }
                var current = status();
                await ResultAsync(id, new
                {
                    status = current.Status,
                    serverName = current.ServerName,
                    installationId = current.InstallationId,
                    environmentId = current.EnvironmentId
                }, cancellationToken);
                break;
            case "permissionProfile/list":
                await ResultAsync(id, CodexPermissionProfile.List(), cancellationToken);
                break;
            case "configRequirements/read":
                await ResultAsync(id, CodexPermissionProfile.Requirements(), cancellationToken);
                break;
            default:
                await ErrorAsync(id, -32601, "Method not found: " + method, cancellationToken);
                break;
        }
    }

    public Task SendAsync(JsonElement message, CancellationToken cancellationToken)
    {
        if (!message.TryGetProperty("id", out _) && message.TryGetProperty("method", out var method)
            && _optOutNotifications.Contains(method.GetString()!))
            return Task.CompletedTask;
        CodexGatewayDiagnostics.Notification(observedMethod, message);
        return send(message, cancellationToken);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private Task ResultAsync(JsonElement id, object result, CancellationToken cancellationToken, string? method = null, long started = 0)
    {
        var message = JsonSerializer.SerializeToElement(new { id, result });
        var bytes = Encoding.UTF8.GetByteCount(message.GetRawText());
        if (bytes > RelayFrames.MaxMessageBytes) throw new GatewayHistoryLimitException(RelayFrames.MaxMessageBytes);
        CodexGatewayDiagnostics.Response(observedMethod, method, message.Get("result"), bytes,
            started == 0 ? 0 : Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        return send(message, cancellationToken);
    }

    private Task ErrorAsync(JsonElement id, int code, string message, CancellationToken cancellationToken) =>
        send(JsonSerializer.SerializeToElement(new { id, error = new { code, message } }), cancellationToken);

    private static string PlatformOs() => OperatingSystem.IsWindows() ? "windows"
        : OperatingSystem.IsMacOS() ? "macos"
        : OperatingSystem.IsLinux() ? "linux"
        : OperatingSystem.IsFreeBSD() ? "freebsd"
        : RuntimeInformation.OSDescription;

    private static readonly JsonElement EmptyParams = JsonSerializer.Deserialize<JsonElement>("{}");
}
