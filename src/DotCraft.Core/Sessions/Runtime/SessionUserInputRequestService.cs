using System.Collections.Concurrent;

namespace DotCraft.Sessions;

/// <summary>
/// Per-turn service that pauses model tool execution while the active client
/// answers short model-initiated questions.
/// </summary>
internal sealed class SessionUserInputRequestService
{
    private readonly SessionEventChannel _channel;
    private readonly SessionTurn _turn;
    private readonly Func<int> _nextItemSeq;
    private readonly Action<string, SessionThreadRuntimeSignal, SessionTurn?>? _runtimeSignalForBroadcast;
    private readonly ConcurrentDictionary<string, TaskCompletionSource<RequestUserInputResponse>> _pending = new();
    private readonly Lock _gate = new();
    private bool _closed;

    public SessionUserInputRequestService(
        SessionEventChannel channel,
        SessionTurn turn,
        Func<int> nextItemSeq,
        CancellationToken turnCancellationToken,
        Action<string, SessionThreadRuntimeSignal, SessionTurn?>? runtimeSignalForBroadcast = null)
    {
        _channel = channel;
        _turn = turn;
        _nextItemSeq = nextItemSeq;
        _runtimeSignalForBroadcast = runtimeSignalForBroadcast;
        turnCancellationToken.Register(Close);
    }

    public Task<RequestUserInputResponse> RequestAsync(
        string requestId,
        IReadOnlyList<RequestUserInputQuestion> questions,
        bool isBlocking)
    {
        var payload = new UserInputRequestPayload
        {
            RequestId = requestId,
            Questions = questions.Select(NormalizeQuestion).ToArray(),
            IsBlocking = isBlocking
        };
        return RequestCoreAsync(requestId, payload);
    }

    public bool TryResolve(string requestId, RequestUserInputResponse response)
    {
        if (!_pending.TryRemove(requestId, out var completion))
            return false;

        RecordResolution(requestId, response);
        completion.TrySetResult(response);
        return true;
    }

    /// <summary>
    /// Resolves every pending request with an empty response because the Turn ended,
    /// and cancels any later request.
    /// </summary>
    public void Close()
    {
        lock (_gate)
            _closed = true;

        foreach (var requestId in _pending.Keys)
        {
            if (!_pending.TryRemove(requestId, out var completion))
                continue;

            RecordResolution(requestId, new RequestUserInputResponse());
            completion.TrySetCanceled();
        }
    }

    private void RecordResolution(string requestId, RequestUserInputResponse response)
    {
        var responseItem = CreateItem(ItemType.UserInputResponse, new UserInputResponsePayload
        {
            RequestId = requestId,
            Response = response
        });
        _turn.Items.Add(responseItem);
        if (_turn.Status == TurnStatus.WaitingInput)
            _turn.Status = TurnStatus.Running;

        _channel.EmitItemStarted(responseItem);
        _channel.EmitUserInputResolved(responseItem);
        _channel.EmitItemCompleted(responseItem);
        _runtimeSignalForBroadcast?.Invoke(_turn.ThreadId, SessionThreadRuntimeSignal.UserInputResolved, _turn);
    }

    private Task<RequestUserInputResponse> RequestCoreAsync(
        string requestId,
        UserInputRequestPayload payload)
    {
        var tcs = new TaskCompletionSource<RequestUserInputResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_gate)
        {
            if (_closed)
                return Task.FromCanceled<RequestUserInputResponse>(new CancellationToken(canceled: true));

            var requestItem = CreateItem(ItemType.UserInputRequest, payload);
            _turn.Items.Add(requestItem);
            _turn.Status = TurnStatus.WaitingInput;
            _pending[requestId] = tcs;

            _channel.EmitItemStarted(requestItem);
            _channel.EmitItemCompleted(requestItem);
            _channel.EmitUserInputRequested(requestItem);
            _runtimeSignalForBroadcast?.Invoke(_turn.ThreadId, SessionThreadRuntimeSignal.UserInputRequested, _turn);
        }

        return tcs.Task;
    }

    private static RequestUserInputQuestion NormalizeQuestion(RequestUserInputQuestion question) =>
        new()
        {
            Id = question.Id.Trim(),
            Header = question.Header.Trim(),
            Question = question.Question.Trim(),
            IsOther = true,
            IsSecret = question.IsSecret,
            Options = question.Options
                .Where(option => !string.IsNullOrWhiteSpace(option.Label))
                .Select(option => new RequestUserInputQuestionOption
                {
                    Label = option.Label.Trim(),
                    Description = option.Description.Trim()
                })
                .ToList()
        };

    private SessionItem CreateItem(ItemType type, object payload)
    {
        var now = DateTimeOffset.UtcNow;
        return new SessionItem
        {
            Id = SessionIdGenerator.NewItemId(_nextItemSeq()),
            TurnId = _turn.Id,
            Type = type,
            Status = ItemStatus.Completed,
            CreatedAt = now,
            CompletedAt = now,
            Payload = payload
        };
    }
}
