namespace DotCraft.Agents;

internal sealed class ProviderLoginSession : IProviderLoginInteraction, IDisposable
{
    private readonly object _gate = new();
    private readonly CancellationTokenSource _cancellation;
    private readonly IProviderAuthenticator _authenticator;
    private readonly string _method;
    private readonly Action<ProviderLoginState> _publish;
    private readonly Action _authenticated;
    private readonly DateTimeOffset _createdAt = DateTimeOffset.UtcNow;
    private TaskCompletionSource<string>? _answer;
    private ProviderLoginState _state;

    public ProviderLoginSession(string providerId, string instanceKey, string owner, IProviderAuthenticator authenticator,
        string method, CancellationToken generationStopping, CancellationToken ownerStopping,
        Action<ProviderLoginState> publish, Action authenticated)
    {
        Owner = owner;
        InstanceKey = instanceKey;
        _authenticator = authenticator;
        _method = method;
        _publish = publish;
        _authenticated = authenticated;
        _cancellation = CancellationTokenSource.CreateLinkedTokenSource(generationStopping, ownerStopping);
        _cancellation.CancelAfter(TimeSpan.FromMinutes(15));
        _state = new ProviderLoginState(Guid.NewGuid().ToString("N"), providerId, 0, "running");
    }

    public string Owner { get; }
    public string InstanceKey { get; }
    public ProviderLoginState State { get { lock (_gate) return _state; } }
    public Task Completion { get; private set; } = Task.CompletedTask;
    public bool IsTerminal => State.Status is "completed" or "cancelled" or "failed";
    public bool IsExpired => IsTerminal && Completion.IsCompleted && DateTimeOffset.UtcNow - _createdAt > TimeSpan.FromMinutes(15);

    public void Start() => Completion = Task.Run(RunAsync);
    public void Cancel() { if (!_cancellation.IsCancellationRequested) _cancellation.Cancel(); }
    public void Dispose() => _cancellation.Dispose();

    public ValueTask NotifyAsync(ProviderLoginEvent notification, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        Update(state => state with { Event = notification });
        return ValueTask.CompletedTask;
    }

    public async Task<string> PromptAsync(ProviderLoginPrompt prompt, CancellationToken ct)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _cancellation.Token);
        var completion = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_gate)
        {
            if (_answer is not null)
                throw new InvalidOperationException("Only one authentication prompt may be pending.");
            _answer = completion;
        }
        Update(state => state with { Status = "waiting", PromptId = Guid.NewGuid().ToString("N"), Prompt = prompt });
        try { return await completion.Task.WaitAsync(linked.Token).ConfigureAwait(false); }
        finally
        {
            lock (_gate) _answer = null;
            Update(state => state with { Status = "running", PromptId = null, Prompt = null });
        }
    }

    public void Respond(string promptId, string value)
    {
        lock (_gate)
        {
            if (_state.PromptId != promptId || _answer is null || !_answer.TrySetResult(value))
                throw new ProviderAuthenticationException("ProviderLoginPromptStale", "The authentication prompt is no longer pending.");
        }
    }

    private async Task RunAsync()
    {
        try
        {
            var status = await _authenticator.LoginAsync(_method, this, _cancellation.Token).ConfigureAwait(false);
            _cancellation.Token.ThrowIfCancellationRequested();
            _authenticated();
            Update(state => state with { Status = "completed", Authentication = status });
        }
        catch (OperationCanceledException)
        {
            Update(state => state with { Status = "cancelled", ErrorCode = "ProviderLoginCancelled" });
        }
        catch (Exception)
        {
            Update(state => state with
            {
                Status = "failed", ErrorCode = "ProviderLoginFailed",
                ErrorMessage = "Provider sign-in failed. Try again."
            });
        }
    }

    private void Update(Func<ProviderLoginState, ProviderLoginState> update)
    {
        ProviderLoginState next;
        lock (_gate)
        {
            next = update(_state) with { Revision = _state.Revision + 1 };
            _state = next;
        }
        _publish(next);
    }
}
