using Contract = DotCraft.Protocol.AppServer;
using Methods = DotCraft.Protocol.AppServer.AppServerMethodNames;
using DotCraft.Sessions;
using RequestKey = DotCraft.AppServer.AppServerInteractiveRequestHolders.RequestKey;

namespace DotCraft.AppServer;

/// <summary>
/// Sends AppServer interactive server-to-client requests and resolves the matching
/// Session Core request when the client replies.
/// </summary>
internal sealed class AppServerInteractiveRequestSender
{
    private readonly AppServerConnection _connection;
    private readonly IAppServerTransport _transport;
    private readonly ISessionService _sessionService;
    private readonly Func<bool> _transportUnavailable;
    private readonly Action? _markTransportUnavailable;
    private readonly AppServerInteractiveRequestHolders _holders;

    public AppServerInteractiveRequestSender(
        AppServerConnection connection,
        IAppServerTransport transport,
        ISessionService sessionService,
        Func<bool>? transportUnavailable = null,
        Action? markTransportUnavailable = null)
    {
        _connection = connection;
        _transport = transport;
        _sessionService = sessionService;
        _transportUnavailable = transportUnavailable ?? (() => false);
        _markTransportUnavailable = markTransportUnavailable;
        _holders = AppServerInteractiveRequestHolders.For(sessionService);
    }

    public async Task SendApprovalRequestAsync(
        string threadId,
        string turnId,
        string itemId,
        ApprovalRequestPayload request)
    {
        if (!_connection.SupportsApproval
            || TryHold(new RequestKey(Methods.ApprovalRequest, threadId, turnId, request.RequestId)) is not { } hold)
        {
            return;
        }

        var approvalParams = new Contract.ApprovalRequestParams
        {
            ThreadId = threadId,
            TurnId = turnId,
            ItemId = itemId,
            RequestId = request.RequestId,
            ApprovalType = request.ApprovalType,
            Operation = request.Operation,
            Target = request.Target,
            TargetLabel = request.TargetLabel,
            ScopeKey = request.ScopeKey,
            Reason = request.Reason,
            Shell = ToContract(request.Shell)
        };

        var response = await AwaitAnswerAsync(
            hold,
            ct => _transport.RequestAsync(
                Contract.AppServerRpc.ApprovalRequest,
                approvalParams,
                ct,
                timeout: Timeout.InfiniteTimeSpan));
        if (response is not null && _holders.TryResolve(hold.Key))
        {
            await TryResolveApprovalAsync(
                threadId,
                turnId,
                request.RequestId,
                ParseApprovalDecision(response.Result));
        }
    }

    public async Task SendUserInputRequestAsync(
        string threadId,
        string turnId,
        string itemId,
        UserInputRequestPayload request)
    {
        if (!_connection.SupportsRequestUserInput
            || TryHold(new RequestKey(Methods.UserInputRequest, threadId, turnId, request.RequestId)) is not { } hold)
        {
            return;
        }

        var requestParams = new Contract.UserInputRequestParams
        {
            ThreadId = threadId,
            TurnId = turnId,
            ItemId = itemId,
            RequestId = request.RequestId,
            IsBlocking = request.IsBlocking,
            Questions = request.Questions.Select(static question => new Contract.UserInputQuestion
            {
                Id = question.Id,
                Header = question.Header,
                Question = question.Question,
                IsOther = question.IsOther,
                IsSecret = question.IsSecret,
                Options = question.Options.Select(static option => new Contract.UserInputOption
                {
                    Label = option.Label,
                    Description = option.Description
                }).ToArray()
            }).ToArray()
        };

        var response = await AwaitAnswerAsync(
            hold,
            ct => _transport.RequestAsync(
                Contract.AppServerRpc.UserInputRequest,
                requestParams,
                ct,
                timeout: Timeout.InfiniteTimeSpan));
        if (response is not null && _holders.TryResolve(hold.Key))
        {
            await TryResolveUserInputAsync(
                threadId,
                turnId,
                request.RequestId,
                ParseUserInputResponse(response.Result));
        }
    }

    public async Task ResolveApprovalByPolicyAsync(
        string threadId,
        string turnId,
        string requestId,
        SessionApprovalDecision defaultDecision)
    {
        if (!_holders.TryResolve(new RequestKey(Methods.ApprovalRequest, threadId, turnId, requestId)))
            return;

        var decision = await ResolveNonInteractiveApprovalDecisionAsync(threadId, defaultDecision);
        await TryResolveApprovalAsync(threadId, turnId, requestId, decision);
    }

    public async Task ResolveUserInputWithEmptyAnswersAsync(string threadId, string turnId, string requestId)
    {
        if (_holders.TryResolve(new RequestKey(Methods.UserInputRequest, threadId, turnId, requestId)))
            await TryResolveUserInputAsync(threadId, turnId, requestId, new RequestUserInputResponse());
    }

    public void ObserveResolution(SessionEvent evt)
    {
        RequestKey? key = evt switch
        {
            { EventType: SessionEventType.ApprovalResolved, TurnId: { } turnId, ItemPayload.Payload: ApprovalResponsePayload approval } =>
                new RequestKey(Methods.ApprovalRequest, evt.ThreadId, turnId, approval.RequestId),
            { EventType: SessionEventType.UserInputResolved, TurnId: { } turnId, ItemPayload.Payload: UserInputResponsePayload userInput } =>
                new RequestKey(Methods.UserInputRequest, evt.ThreadId, turnId, userInput.RequestId),
            _ => null
        };
        if (key is { } resolved)
            _holders.TryResolve(resolved);
    }

    private AppServerInteractiveRequestHolders.Hold? TryHold(RequestKey key) =>
        !_transportUnavailable()
        && _connection.TryRegisterInteractiveRequest(key.Method, key.ThreadId, key.TurnId, key.RequestId)
            ? _holders.TryHold(key)
            : null;

    private async Task<AppServerTypedClientResponse<TResult>?> AwaitAnswerAsync<TResult>(
        AppServerInteractiveRequestHolders.Hold hold,
        Func<CancellationToken, Task<AppServerTypedClientResponse<TResult>>> send)
        where TResult : class
    {
        using var resolvedElsewhere = new CancellationTokenSource();
        try
        {
            var request = send(resolvedElsewhere.Token);
            if (await Task.WhenAny(request, hold.Resolved) == request)
                return await request;

            resolvedElsewhere.Cancel();
            _ = request.ContinueWith(
                static task => _ = task.Exception,
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
            return null;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception ex) when (AppServerEventDispatcher.IsTransportUnavailableException(ex))
        {
            _markTransportUnavailable?.Invoke();
            return null;
        }
        finally
        {
            _holders.Release(hold);
        }
    }

    internal static Contract.ApprovalShellDetails? ToContract(ShellApprovalDetails? shell) =>
        shell is null
            ? null
            : new Contract.ApprovalShellDetails
            {
                Reasons = shell.Reasons,
                RememberedPrefixes = shell.RememberedPrefixes,
                RemembersExactCommand = shell.RemembersExactCommand
            };

    private async Task TryResolveApprovalAsync(
        string threadId,
        string turnId,
        string requestId,
        SessionApprovalDecision decision)
    {
        try
        {
            await _sessionService.ResolveApprovalAsync(threadId, turnId, requestId, decision, CancellationToken.None);
        }
        catch (OperationCanceledException) { /* Ignore if session was cancelled */ }
    }

    private async Task<SessionApprovalDecision> ResolveNonInteractiveApprovalDecisionAsync(
        string threadId,
        SessionApprovalDecision defaultDecision)
    {
        try
        {
            var thread = await _sessionService.GetThreadAsync(threadId, CancellationToken.None);
            return thread.Configuration?.ApprovalPolicy switch
            {
                ApprovalPolicy.AutoApprove => SessionApprovalDecision.AcceptOnce,
                ApprovalPolicy.Deny => SessionApprovalDecision.Reject,
                _ => defaultDecision
            };
        }
        catch
        {
            return defaultDecision;
        }
    }

    private async Task TryResolveUserInputAsync(
        string threadId,
        string turnId,
        string requestId,
        RequestUserInputResponse response)
    {
        try
        {
            await _sessionService.ResolveUserInputRequestAsync(
                threadId,
                turnId,
                requestId,
                response,
                CancellationToken.None);
        }
        catch (OperationCanceledException) { /* Ignore if session was cancelled */ }
    }

    private static SessionApprovalDecision ParseApprovalDecision(Contract.ApprovalResponseResult? result)
    {
        return result?.Decision switch
        {
            "accept" => SessionApprovalDecision.AcceptOnce,
            "acceptForSession" => SessionApprovalDecision.AcceptForSession,
            "acceptAlways" => SessionApprovalDecision.AcceptAlways,
            "decline" => SessionApprovalDecision.Reject,
            "cancel" => SessionApprovalDecision.CancelTurn,
            _ => SessionApprovalDecision.Reject
        };
    }

    private static RequestUserInputResponse ParseUserInputResponse(Contract.UserInputResponseResult? result)
    {
        if (result is null)
            return new RequestUserInputResponse();

        try
        {
            return new RequestUserInputResponse
            {
                Answers = result.Answers.ToDictionary(
                    static answer => answer.Key,
                    static answer => new RequestUserInputAnswer
                    {
                        Answers = answer.Value.Answers.ToList()
                    },
                    StringComparer.Ordinal)
            };
        }
        catch
        {
            return new RequestUserInputResponse();
        }
    }
}
