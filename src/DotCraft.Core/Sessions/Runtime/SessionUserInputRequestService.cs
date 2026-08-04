using System.Collections.Concurrent;
using SessionTurn = DotCraft.Sessions.SessionTurn;

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
    private readonly CancellationToken _turnCancellationToken;
    private readonly Action<string, SessionThreadRuntimeSignal>? _runtimeSignalForBroadcast;
    private readonly ConcurrentDictionary<string, PendingUserInputRequest> _pending = new();

    private sealed class PendingUserInputRequest(
        TaskCompletionSource<RequestUserInputResponse> completion)
    {
        public TaskCompletionSource<RequestUserInputResponse> Completion { get; } = completion;
    }

    public SessionUserInputRequestService(
        SessionEventChannel channel,
        SessionTurn turn,
        Func<int> nextItemSeq,
        CancellationToken turnCancellationToken,
        Action<string, SessionThreadRuntimeSignal>? runtimeSignalForBroadcast = null)
    {
        _channel = channel;
        _turn = turn;
        _nextItemSeq = nextItemSeq;
        _turnCancellationToken = turnCancellationToken;
        _runtimeSignalForBroadcast = runtimeSignalForBroadcast;
    }

    public Task<RequestUserInputResponse> RequestAsync(
        string requestId,
        IReadOnlyList<RequestUserInputQuestion> questions)
    {
        var payload = new UserInputRequestPayload
        {
            RequestId = requestId,
            Questions = questions.Select(NormalizeQuestion).ToArray()
        };
        return RequestCoreAsync(requestId, payload);
    }

    public bool TryResolve(string requestId, RequestUserInputResponse response)
    {
        if (!_pending.TryRemove(requestId, out var pending))
            return false;

        var responseItem = CreateItem(ItemType.UserInputResponse, new UserInputResponsePayload
        {
            RequestId = requestId,
            Response = response
        });
        _turn.Items.Add(responseItem);
        _turn.Status = TurnStatus.Running;

        _channel.EmitItemStarted(responseItem);
        _channel.EmitUserInputResolved(responseItem);
        _channel.EmitItemCompleted(responseItem);
        _runtimeSignalForBroadcast?.Invoke(_turn.ThreadId, SessionThreadRuntimeSignal.UserInputResolved);

        pending.Completion.TrySetResult(response);
        return true;
    }

    private async Task<RequestUserInputResponse> RequestCoreAsync(
        string requestId,
        UserInputRequestPayload payload)
    {
        var requestItem = CreateItem(ItemType.UserInputRequest, payload);
        _turn.Items.Add(requestItem);
        _turn.Status = TurnStatus.WaitingInput;

        var tcs = new TaskCompletionSource<RequestUserInputResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[requestId] = new PendingUserInputRequest(tcs);

        _channel.EmitItemStarted(requestItem);
        _channel.EmitItemCompleted(requestItem);
        _channel.EmitUserInputRequested(requestItem);
        _runtimeSignalForBroadcast?.Invoke(_turn.ThreadId, SessionThreadRuntimeSignal.UserInputRequested);

        await using var reg = _turnCancellationToken.Register(() =>
        {
            if (_pending.TryRemove(requestId, out var pending))
            {
                _runtimeSignalForBroadcast?.Invoke(_turn.ThreadId, SessionThreadRuntimeSignal.UserInputResolved);

                pending.Completion.TrySetCanceled(_turnCancellationToken);
            }
        });

        return await tcs.Task;
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
