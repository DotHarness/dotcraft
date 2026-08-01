using Contract = DotCraft.Protocol.Contracts.AppServer;

namespace DotCraft.Protocol.AppServer;

/// <summary>
/// Sends AppServer interactive server-to-client requests and resolves the matching
/// Session Core request when the client replies.
/// </summary>
internal sealed class AppServerInteractiveRequestSender
{
    private readonly AppServerConnection _connection;
    private readonly IAppServerTransport _transport;
    private readonly ISessionService _sessionService;
    private readonly SessionApprovalDecision _defaultApprovalDecision;
    private readonly Func<bool> _transportUnavailable;
    private readonly Action? _markTransportUnavailable;

    public AppServerInteractiveRequestSender(
        AppServerConnection connection,
        IAppServerTransport transport,
        ISessionService sessionService,
        SessionApprovalDecision defaultApprovalDecision,
        Func<bool>? transportUnavailable = null,
        Action? markTransportUnavailable = null)
    {
        _connection = connection;
        _transport = transport;
        _sessionService = sessionService;
        _defaultApprovalDecision = defaultApprovalDecision;
        _transportUnavailable = transportUnavailable ?? (() => false);
        _markTransportUnavailable = markTransportUnavailable;
    }

    public async Task SendApprovalRequestAsync(
        string threadId,
        string turnId,
        string itemId,
        ApprovalRequestPayload request,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(turnId))
            return;

        if (!_connection.SupportsApproval || _transportUnavailable())
        {
            await ResolveApprovalWithFallbackAsync(threadId, turnId, request.RequestId, CancellationToken.None);
            return;
        }

        if (!_connection.TryRegisterInteractiveRequest(
            DotCraft.Protocol.Contracts.AppServer.AppServerMethodNames.ApprovalRequest,
            threadId,
            turnId,
            request.RequestId))
            return;

        var approvalParams = new AppServerApprovalRequestParams
        {
            ThreadId = threadId,
            TurnId = turnId,
            ItemId = itemId,
            RequestId = request.RequestId,
            ApprovalType = request.ApprovalType,
            Operation = request.Operation,
            Target = request.Target,
            ScopeKey = request.ScopeKey,
            Reason = request.Reason,
            ExpiresAt = request.ExpiresAt
        };

        AppServerTypedClientResponse<Contract.ApprovalResponseResult> response;
        try
        {
            response = await _transport.RequestAsync(
                Contract.AppServerRpc.ApprovalRequest,
                AppServerContractMapper.ToContract(approvalParams),
                CancellationToken.None,
                timeout: RemainingApprovalTimeout(request.ExpiresAt));
        }
        catch (OperationCanceledException)
        {
            await ResolveApprovalWithFallbackAsync(threadId, turnId, request.RequestId, CancellationToken.None);
            return;
        }
        catch (Exception ex) when (AppServerEventDispatcher.IsTransportUnavailableException(ex))
        {
            _markTransportUnavailable?.Invoke();
            await ResolveApprovalWithFallbackAsync(threadId, turnId, request.RequestId, CancellationToken.None);
            return;
        }

        var decision = ParseApprovalDecision(response.Result);
        await TryResolveApprovalAsync(threadId, turnId, request.RequestId, decision, CancellationToken.None);
    }

    private static TimeSpan RemainingApprovalTimeout(DateTimeOffset expiresAt)
    {
        var remaining = expiresAt - DateTimeOffset.UtcNow;
        return remaining > TimeSpan.Zero ? remaining : TimeSpan.FromMilliseconds(1);
    }

    public async Task SendUserInputRequestAsync(
        string threadId,
        string turnId,
        string itemId,
        UserInputRequestPayload request,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(turnId))
            return;

        if (!_connection.SupportsRequestUserInput || _transportUnavailable())
        {
            await TryResolveUserInputAsync(threadId, turnId, request.RequestId, new RequestUserInputResponse(), CancellationToken.None);
            return;
        }

        if (!_connection.TryRegisterInteractiveRequest(
            DotCraft.Protocol.Contracts.AppServer.AppServerMethodNames.UserInputRequest,
            threadId,
            turnId,
            request.RequestId))
            return;

        var requestParams = new AppServerRequestUserInputParams
        {
            ThreadId = threadId,
            TurnId = turnId,
            ItemId = itemId,
            RequestId = request.RequestId,
            Questions = request.Questions.ToList()
        };

        AppServerTypedClientResponse<Contract.UserInputResponseResult> response;
        try
        {
            response = await _transport.RequestAsync(
                Contract.AppServerRpc.UserInputRequest,
                AppServerContractMapper.ToContract(requestParams),
                CancellationToken.None,
                timeout: Timeout.InfiniteTimeSpan);
        }
        catch (OperationCanceledException)
        {
            await TryResolveUserInputAsync(threadId, turnId, request.RequestId, new RequestUserInputResponse(), CancellationToken.None);
            return;
        }
        catch (Exception ex) when (AppServerEventDispatcher.IsTransportUnavailableException(ex))
        {
            _markTransportUnavailable?.Invoke();
            await TryResolveUserInputAsync(threadId, turnId, request.RequestId, new RequestUserInputResponse(), CancellationToken.None);
            return;
        }

        await TryResolveUserInputAsync(threadId, turnId, request.RequestId, ParseUserInputResponse(response.Result), CancellationToken.None);
    }

    public async Task ResolveApprovalWithFallbackAsync(
        string threadId,
        string turnId,
        string requestId,
        CancellationToken ct)
    {
        var fallbackDecision = await ResolveNonInteractiveApprovalDecisionAsync(threadId, ct);
        await TryResolveApprovalAsync(threadId, turnId, requestId, fallbackDecision, ct);
    }

    private async Task TryResolveApprovalAsync(
        string threadId,
        string turnId,
        string requestId,
        SessionApprovalDecision decision,
        CancellationToken ct)
    {
        try
        {
            await _sessionService.ResolveApprovalAsync(threadId, turnId, requestId, decision, ct);
        }
        catch (OperationCanceledException) { /* Ignore if session was cancelled */ }
    }

    private async Task<SessionApprovalDecision> ResolveNonInteractiveApprovalDecisionAsync(
        string threadId,
        CancellationToken ct)
    {
        try
        {
            var thread = await _sessionService.GetThreadAsync(threadId, ct);
            return thread.Configuration?.ApprovalPolicy switch
            {
                ApprovalPolicy.AutoApprove => SessionApprovalDecision.AcceptOnce,
                ApprovalPolicy.Interrupt => SessionApprovalDecision.CancelTurn,
                _ => _defaultApprovalDecision
            };
        }
        catch
        {
            return _defaultApprovalDecision;
        }
    }

    private async Task TryResolveUserInputAsync(
        string threadId,
        string turnId,
        string requestId,
        RequestUserInputResponse response,
        CancellationToken ct)
    {
        try
        {
            await _sessionService.ResolveUserInputRequestAsync(
                threadId,
                turnId,
                requestId,
                response,
                ct);
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
            var domain = AppServerContractMapper.ToDomain(result);

            return new RequestUserInputResponse
            {
                Answers = domain.Answers ?? new Dictionary<string, RequestUserInputAnswer>(StringComparer.Ordinal)
            };
        }
        catch
        {
            return new RequestUserInputResponse();
        }
    }
}
