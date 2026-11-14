using System.Net;
using System.Runtime.InteropServices;
using System.Security.Authentication;
using DotCraft.CodexGateway.Persistence;
using DotCraft.CodexGateway.Relay;

namespace DotCraft.CodexGateway;

public sealed class RemoteControlHost : IAsyncDisposable
{
    private readonly RemoteControlHostOptions _options;
    private readonly Func<bool, CancellationToken, Task<RemoteControlCredentials>> _credentials;
    private readonly Action<RemoteControlHostStatus>? _statusChanged;
    private readonly RemoteControlStateStore _store;
    private readonly RemoteControlApi _api;
    private readonly RelayOutbox _outbox = new();
    private readonly RelaySessions _sessions;
    private readonly SemaphoreSlim _operation = new(1);
    private readonly CancellationTokenSource _stop = new();
    private string? _accountId;
    private EnrollmentIdentity? _identity;
    private EnrollmentResponse? _enrollment;
    private DateTimeOffset _nextRefreshAt;
    private RelayRequestException? _refreshFailure;
    private AuthenticationException? _ownerFailure;
    private Task? _run;
    private int _started;
    private int _disposed;

    public RemoteControlHost(RemoteControlHostOptions options,
        Func<bool, CancellationToken, Task<RemoteControlCredentials>> credentials,
        RemoteControlSessionFactory sessions,
        Action<RemoteControlHostStatus>? statusChanged = null)
    {
        _options = options;
        _credentials = credentials;
        _statusChanged = statusChanged;
        _store = new RemoteControlStateStore(options.StateDirectory);
        try
        {
            _api = new RemoteControlApi(options.BackendBaseUri, _store.InstallationId, options.AppServerVersion, GetCredentialsAsync);
        }
        catch
        {
            _store.Dispose();
            throw;
        }
        _sessions = new RelaySessions(sessions, _outbox);
        Status = new(_store.InstallationId, options.Name, null, "disabled");
    }

    public RemoteControlHostStatus Status { get; private set; }

    public Task RunAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        if (Interlocked.Exchange(ref _started, 1) != 0)
            throw new InvalidOperationException("codex_remote_control.already_running: Host can only be run once.");
        return _run = RunCoreAsync(cancellationToken);
    }

    public async Task<RemoteControlPairing> PairAsync(CancellationToken cancellationToken = default)
    {
        var result = await RelayRequestAsync<PairingResponse>("pair", new { manual_code = true }, cancellationToken);
        if (result.ServerId != _identity!.ServerId || result.EnvironmentId != _identity.EnvironmentId)
            throw new InvalidDataException("codex_remote_control.identity_mismatch: Pairing returned a different host.");
        return new(result.PairingCode, result.ManualPairingCode, result.EnvironmentId, result.ExpiresAt);
    }

    public async Task<bool> IsPairingClaimedAsync(RemoteControlPairing pairing, CancellationToken cancellationToken = default)
    {
        await EnsureEnrollmentAsync(cancellationToken);
        if (pairing.EnvironmentId != _identity!.EnvironmentId)
            throw new ArgumentException("codex_remote_control.identity_mismatch: Pairing belongs to a different host.", nameof(pairing));
        var result = await RelayRequestAsync<PairingStatusResponse>("pair/status",
            new { pairing_code = pairing.PairingCode }, cancellationToken);
        return result.Claimed;
    }

    private async Task<T> RelayRequestAsync<T>(string action, object body, CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        using var request = CancellationTokenSource.CreateLinkedTokenSource(ct, _stop.Token);
        for (var attempt = 0; ; attempt++)
        {
            var enrollment = await EnsureEnrollmentAsync(request.Token);
            try
            {
                return await _api.RelayRequestAsync<T>(action, body, enrollment.RemoteControlToken, request.Token);
            }
            catch (RelayRequestException error) when (attempt == 0
                && error.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                await InvalidateAsync(false, request.Token);
            }
        }
    }

    private async Task RunCoreAsync(CancellationToken ct)
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(ct, _stop.Token);
        var token = lifetime.Token;
        var attempt = 0;
        try
        {
            while (!token.IsCancellationRequested)
            {
                TimeSpan? retryAfter = null;
                var connectedAt = DateTimeOffset.MaxValue;
                try
                {
                    Publish("connecting");
                    var enrollment = await EnsureEnrollmentAsync(token);
                    await RelaySocket.RunAsync(_api, enrollment, _store.InstallationId, _options.Name,
                        _sessions, _outbox,
                        () =>
                        {
                            connectedAt = DateTimeOffset.UtcNow;
                            Publish("connected");
                        }, token);
                }
                catch (RelayRequestException error) when (error.ServerMissing && error.Operation is "connect" or "refresh")
                {
                    RelayDiagnostics.Failure(null, "relay_enrollment_missing", error);
                    await InvalidateAsync(true, token);
                }
                catch (RelayRequestException error) when (error.Operation == "connect"
                    && error.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                {
                    RelayDiagnostics.Failure(null, "relay_token_rejected", error);
                    await InvalidateAsync(false, token);
                }
                catch (RelayRequestException error) when (error.IsTransient)
                {
                    RelayDiagnostics.Failure(null, "relay_request", error);
                    retryAfter = error.RetryAfter;
                }
                catch (HttpRequestException error) when (error.StatusCode is null)
                {
                    RelayDiagnostics.Failure(null, "relay_connect", error);
                }
                catch (IOException error) { RelayDiagnostics.Failure(null, "relay_transport", error); }
                catch (System.Net.WebSockets.WebSocketException error) { RelayDiagnostics.Failure(null, "relay_transport", error); }
                catch (OperationCanceledException error) when (!token.IsCancellationRequested)
                {
                    RelayDiagnostics.Failure(null, "relay_timeout", error);
                }
                if (token.IsCancellationRequested)
                    break;
                Publish("connecting");
                if (DateTimeOffset.UtcNow - connectedAt >= TimeSpan.FromSeconds(60))
                    attempt = 0;
                var seconds = Math.Min(30, 5 * Math.Pow(2, Math.Min(attempt++, 3)));
                var delay = retryAfter is { } guidance
                    ? guidance + TimeSpan.FromSeconds(Random.Shared.NextDouble() * 30)
                    : TimeSpan.FromSeconds(seconds * (0.5 + Random.Shared.NextDouble() * 0.5));
                await Task.Delay(delay, token);
                await _sessions.SweepAsync();
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            if (_ownerFailure is not null)
                throw _ownerFailure;
        }
        catch
        {
            Publish("errored");
            throw;
        }
        finally
        {
            await _sessions.DisposeAsync();
            if (Status.Status != "errored")
                Publish("disabled");
        }
    }

    private async Task<EnrollmentResponse> EnsureEnrollmentAsync(CancellationToken ct)
    {
        await _operation.WaitAsync(ct);
        try
        {
            var credentials = await GetCredentialsAsync(false, ct);
            _identity ??= _store.Find(_api.BaseUri, credentials.AccountId);
            var now = DateTimeOffset.UtcNow;
            if (_enrollment is { } current && current.ExpiresAt > now.AddMinutes(5))
                return current;
            if (now < _nextRefreshAt)
            {
                if (_refreshFailure is not null
                    && (_enrollment is null || _enrollment.ExpiresAt <= now || _refreshFailure.RetryAfter is not null))
                    throw _refreshFailure;
                if (_enrollment is not null && _enrollment.ExpiresAt > now)
                    return _enrollment;
            }
            EnrollmentResponse response;
            try
            {
                response = _identity is null
                    ? await _api.AccountRequestAsync<EnrollmentResponse>("enroll", new
                    {
                        name = _options.Name, os = OperatingSystem.IsWindows() ? "windows"
                            : OperatingSystem.IsMacOS() ? "macos" : OperatingSystem.IsLinux() ? "linux" : "unknown",
                        arch = RuntimeInformation.OSArchitecture switch
                        {
                            Architecture.X64 => "x86_64", Architecture.Arm64 => "aarch64",
                            Architecture.X86 => "x86", _ => RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant()
                        },
                        app_server_version = _options.AppServerVersion, installation_id = _store.InstallationId
                    }, ct)
                    : await _api.AccountRequestAsync<EnrollmentResponse>("refresh",
                        new { server_id = _identity.ServerId, installation_id = _store.InstallationId }, ct);
            }
            catch (RelayRequestException error) when (error.IsTransient)
            {
                var failedAt = DateTimeOffset.UtcNow;
                _refreshFailure = error;
                _nextRefreshAt = failedAt + (error.RetryAfter is { } guidance
                    ? guidance + TimeSpan.FromSeconds(Random.Shared.NextDouble() * 30)
                    : TimeSpan.FromSeconds(Random.Shared.Next(24, 37)));
                if (error.RetryAfter is not null || _enrollment is null || _enrollment.ExpiresAt <= failedAt)
                    throw;
                return _enrollment;
            }
            if (_identity is not null && (response.ServerId != _identity.ServerId
                || response.EnvironmentId != _identity.EnvironmentId))
                throw new InvalidDataException("codex_remote_control.identity_mismatch: Refresh returned a different host.");
            if (string.IsNullOrWhiteSpace(response.RemoteControlToken) || response.ExpiresAt <= DateTimeOffset.UtcNow)
                throw new InvalidDataException("codex_remote_control.invalid_token: Enrollment returned no valid relay token.");
            _identity = new(response.ServerId, response.EnvironmentId);
            _store.Set(_api.BaseUri, credentials.AccountId, _identity);
            _enrollment = response;
            _nextRefreshAt = default;
            _refreshFailure = null;
            Status = Status with { EnvironmentId = _identity.EnvironmentId };
            return response;
        }
        finally
        {
            _operation.Release();
        }
    }

    private async Task<RemoteControlCredentials> GetCredentialsAsync(bool forceRefresh, CancellationToken ct)
    {
        var credentials = await _credentials(forceRefresh, ct);
        if (string.IsNullOrWhiteSpace(credentials.AccountId) || string.IsNullOrWhiteSpace(credentials.AccessToken))
            throw new AuthenticationException("codex_remote_control.authentication_required: ChatGPT account authentication is required.");
        var owner = Interlocked.CompareExchange(ref _accountId, credentials.AccountId, null);
        if (owner is not null && owner != credentials.AccountId)
        {
            _ownerFailure = new AuthenticationException("codex_remote_control.account_changed: Restart the host after changing accounts.");
            _stop.Cancel();
            throw _ownerFailure;
        }
        return credentials;
    }

    private async Task InvalidateAsync(bool identity, CancellationToken ct)
    {
        await _operation.WaitAsync(ct);
        try
        {
            _enrollment = null;
            _nextRefreshAt = default;
            _refreshFailure = null;
            if (identity)
            {
                _store.Set(_api.BaseUri, _accountId!, null);
                _identity = null;
            }
        }
        finally
        {
            _operation.Release();
        }
    }

    private void Publish(string status)
    {
        Status = Status with { Status = status };
        _statusChanged?.Invoke(Status);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        _stop.Cancel();
        try
        {
            if (_run is not null)
                await _run;
            else
                await _sessions.DisposeAsync();
        }
        finally
        {
            await _operation.WaitAsync();
            _api.Dispose();
            _store.Dispose();
            _operation.Release();
            _stop.Dispose();
        }
    }
}
