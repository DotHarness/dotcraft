using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DotCraft.Auth.OpenAI;

/// <summary>
/// Background poller that periodically refreshes the ChatGPT usage / rate-limit snapshot. Owns the
/// in-memory cache and broadcasts <see cref="SnapshotChanged"/> when new data arrives. Cadence
/// mirrors what orca uses (5-minute base, 30-second debounce on manual refresh, exponential backoff
/// on repeated failures).
/// </summary>
internal sealed class OpenAIUsagePoller : IOpenAIUsageService, IAsyncDisposable
{
    /// <summary>Default 5-minute background poll cadence.</summary>
    public static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(5);

    /// <summary>Minimum gap between two refresh attempts to absorb rapid manual triggers.</summary>
    public static readonly TimeSpan ManualRefreshDebounce = TimeSpan.FromSeconds(30);

    /// <summary>Backoff cap when consecutive fetches fail.</summary>
    public static readonly TimeSpan MaxBackoff = TimeSpan.FromHours(1);

    private readonly IOpenAIAuthService _authService;
    private readonly OpenAIUsageClient _usageClient;
    private readonly ILogger<OpenAIUsagePoller> _logger;
    private readonly object _stateGate = new();
    private readonly SemaphoreSlim _fetchGate = new(1, 1);

    private OpenAIUsageSnapshot? _snapshot;
    private DateTimeOffset _lastAttempt = DateTimeOffset.MinValue;
    private int _consecutiveFailures;
    private CancellationTokenSource? _loopCts;
    private Task? _loopTask;

    public OpenAIUsagePoller(
        IOpenAIAuthService authService,
        OpenAIUsageClient usageClient,
        ILogger<OpenAIUsagePoller>? logger = null)
    {
        _authService = authService ?? throw new ArgumentNullException(nameof(authService));
        _usageClient = usageClient ?? throw new ArgumentNullException(nameof(usageClient));
        _logger = logger ?? NullLogger<OpenAIUsagePoller>.Instance;

        _authService.LoggedIn += OnLoggedIn;
        _authService.LoggedOut += OnLoggedOut;
    }

    public event Action<OpenAIUsageSnapshot?>? SnapshotChanged;

    public OpenAIUsageSnapshot? CurrentSnapshot
    {
        get { lock (_stateGate) return _snapshot; }
    }

    /// <summary>Starts the polling loop if an account is already signed in.</summary>
    public void Start()
    {
        if (_authService.IsAuthenticated)
            EnsureLoopRunning();
    }

    public async Task<OpenAIUsageSnapshot?> RefreshAsync(CancellationToken cancellationToken)
    {
        if (!_authService.IsAuthenticated)
        {
            ReplaceSnapshot(null);
            return null;
        }

        // Debounce — manual refreshes don't hammer the endpoint.
        DateTimeOffset lastAttempt;
        lock (_stateGate)
        {
            lastAttempt = _lastAttempt;
        }
        var sinceLast = DateTimeOffset.UtcNow - lastAttempt;
        if (sinceLast < ManualRefreshDebounce)
        {
            _logger.LogTrace("Skipping usage refresh: only {Ms}ms since last attempt.", sinceLast.TotalMilliseconds);
            return CurrentSnapshot;
        }

        return await FetchOnceAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<OpenAIUsageSnapshot?> FetchOnceAsync(CancellationToken cancellationToken)
    {
        await _fetchGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            lock (_stateGate) { _lastAttempt = DateTimeOffset.UtcNow; }

            var snapshot = await _usageClient.FetchAsync(cancellationToken).ConfigureAwait(false);
            lock (_stateGate) { _consecutiveFailures = 0; }
            ReplaceSnapshot(snapshot);
            return snapshot;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (OpenAIAuthException ex)
        {
            lock (_stateGate) { _consecutiveFailures++; }
            _logger.LogWarning("Failed to refresh ChatGPT usage: {Reason} {Message}", ex.Reason, ex.Message);
            return null;
        }
        catch (Exception ex)
        {
            lock (_stateGate) { _consecutiveFailures++; }
            _logger.LogWarning(ex, "Unexpected error fetching ChatGPT usage.");
            return null;
        }
        finally
        {
            _fetchGate.Release();
        }
    }

    private void ReplaceSnapshot(OpenAIUsageSnapshot? next)
    {
        bool changed;
        lock (_stateGate)
        {
            changed = !Equals(_snapshot, next);
            _snapshot = next;
        }
        if (!changed) return;
        try { SnapshotChanged?.Invoke(next); }
        catch (Exception ex) { _logger.LogWarning(ex, "Usage SnapshotChanged subscriber threw."); }
    }

    private void OnLoggedIn(OpenAIAuthStatus status)
    {
        EnsureLoopRunning();
        _ = Task.Run(async () =>
        {
            try { await FetchOnceAsync(CancellationToken.None).ConfigureAwait(false); }
            catch { /* logged by FetchOnceAsync */ }
        });
    }

    private void OnLoggedOut()
    {
        StopLoop();
        lock (_stateGate)
        {
            _consecutiveFailures = 0;
            _lastAttempt = DateTimeOffset.MinValue;
        }
        ReplaceSnapshot(null);
    }

    private void EnsureLoopRunning()
    {
        lock (_stateGate)
        {
            if (_loopTask is { IsCompleted: false }) return;
            _loopCts?.Dispose();
            _loopCts = new CancellationTokenSource();
            var token = _loopCts.Token;
            _loopTask = Task.Run(() => LoopAsync(token), token);
        }
    }

    private void StopLoop()
    {
        CancellationTokenSource? cts;
        lock (_stateGate)
        {
            cts = _loopCts;
            _loopCts = null;
        }
        try { cts?.Cancel(); }
        catch (ObjectDisposedException) { /* ignore */ }
    }

    private async Task LoopAsync(CancellationToken cancellationToken)
    {
        // First fetch on startup; subsequent polls run on the cadence below.
        try { await FetchOnceAsync(cancellationToken).ConfigureAwait(false); }
        catch (OperationCanceledException) { return; }
        catch { /* logged */ }

        while (!cancellationToken.IsCancellationRequested)
        {
            int failures;
            lock (_stateGate) { failures = _consecutiveFailures; }
            var delay = NextDelay(failures);
            try { await Task.Delay(delay, cancellationToken).ConfigureAwait(false); }
            catch (OperationCanceledException) { return; }

            if (!_authService.IsAuthenticated)
                return;

            try { await FetchOnceAsync(cancellationToken).ConfigureAwait(false); }
            catch (OperationCanceledException) { return; }
            catch { /* logged */ }
        }
    }

    internal static TimeSpan NextDelay(int consecutiveFailures)
    {
        if (consecutiveFailures <= 0)
            return PollInterval;
        // Exponential backoff: 10m, 20m, 40m, capped at 1h.
        var seconds = Math.Min(MaxBackoff.TotalSeconds, 600 * Math.Pow(2, consecutiveFailures - 1));
        return TimeSpan.FromSeconds(seconds);
    }

    public async ValueTask DisposeAsync()
    {
        _authService.LoggedIn -= OnLoggedIn;
        _authService.LoggedOut -= OnLoggedOut;
        StopLoop();
        var loop = _loopTask;
        if (loop is not null)
        {
            try { await loop.ConfigureAwait(false); }
            catch { /* loop swallows by design */ }
        }
        _fetchGate.Dispose();
    }
}
