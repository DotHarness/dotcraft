using DotCraft.Common;
using DotCraft.RemoteTools;
using Microsoft.AspNetCore.Http;

namespace DotCraft.Hub;

internal sealed class MobilePairingRequests(MobileRegistry registry, HubEventBus events, TimeProvider? timeProvider = null)
{
    private static readonly TimeSpan CallWait = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan Abandoned = TimeSpan.FromSeconds(30);

    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
    private readonly Lock _gate = new();
    private PairingRequest? _request;
    private string? _deniedKeyHash;

    public async Task<(MobilePairResponse? Paired, string? RequestKey)> PairAsync(
        MobilePairRequest body,
        string displayName,
        Func<MobileComputer> computer,
        CancellationToken ct)
    {
        if (string.IsNullOrEmpty(body.RequestKey))
            return (null, Open(body, displayName));
        var request = Find(body.RequestKey);
        Decision decision;
        try
        {
            decision = await request.Decision.Task.WaitAsync(CallWait, _time, ct).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            return (null, null);
        }

        if (decision == Decision.Denied)
            throw Denied();
        lock (_gate)
        {
            if (!ReferenceEquals(_request, request))
                throw RequestEnded();
            End(request);
        }

        var (device, credential) = registry.AddDevice(
            displayName,
            body.Platform!,
            body.OsVersion?.Trim() ?? string.Empty,
            body.AppVersion?.Trim() ?? string.Empty);
        events.Publish("mobile.devicePaired", data: new
        {
            deviceId = device.DeviceId,
            displayName = device.DisplayName,
            platform = device.Platform,
            pairingId = request.PairingId
        });
        return (new MobilePairResponse(device.DeviceId, credential, computer()), null);
    }

    public bool Allow(string pairingId) => Decide(pairingId, Decision.Allowed);

    public bool Deny(string pairingId) => Decide(pairingId, Decision.Denied);

    public void Cancel()
    {
        PairingRequest? request;
        lock (_gate)
        {
            request = _request;
            if (request is not null)
                End(request);
        }
        if (request is not null)
            Cancelled(request);
    }

    private string Open(MobilePairRequest body, string displayName)
    {
        var key = TokenUtilities.GenerateToken();
        PairingRequest request;
        lock (_gate)
        {
            if (registry.TryConsumePairing(body.Code!) is not { } pairingId)
                throw RequestEnded();
            request = new PairingRequest(pairingId, TokenUtilities.HashToken(key));
            request.Timer = _time.CreateTimer(_ => Expire(request), null, Abandoned, Timeout.InfiniteTimeSpan);
            _request = request;
        }
        events.Publish("mobile.pairingRequested", data: new
        {
            pairingId = request.PairingId,
            displayName,
            platform = body.Platform
        });
        return key;
    }

    private PairingRequest Find(string requestKey)
    {
        lock (_gate)
        {
            if (_deniedKeyHash is { } denied && TokenUtilities.VerifyToken(requestKey, denied))
                throw Denied();
            if (_request is not { } open || !TokenUtilities.VerifyToken(requestKey, open.KeyHash))
                throw RequestEnded();
            open.Timer.Change(CallWait + Abandoned, Timeout.InfiniteTimeSpan);
            return open;
        }
    }

    private bool Decide(string pairingId, Decision decision)
    {
        lock (_gate)
        {
            if (_request is not { } request || request.PairingId != pairingId)
                return false;
            request.Decision.TrySetResult(decision);
            if (decision == Decision.Denied)
            {
                _deniedKeyHash = request.KeyHash;
                End(request);
            }
            return true;
        }
    }

    private void Expire(PairingRequest request)
    {
        lock (_gate)
        {
            if (!ReferenceEquals(_request, request))
                return;
            End(request);
        }
        Cancelled(request);
    }

    private void End(PairingRequest request)
    {
        _request = null;
        request.Timer.Dispose();
        request.Decision.TrySetResult(Decision.Ended);
    }

    private void Cancelled(PairingRequest request) =>
        events.Publish("mobile.pairingCancelled", data: new { pairingId = request.PairingId });

    private static HubProtocolException Denied() => new(
        "pairingDenied",
        "The computer declined this phone.",
        StatusCodes.Status403Forbidden);

    private static HubProtocolException RequestEnded() => new(
        "pairingCodeInvalid",
        "The pairing code is not valid. Ask the computer for a new one.",
        StatusCodes.Status400BadRequest);

    private enum Decision
    {
        Allowed,
        Denied,
        Ended
    }

    private sealed class PairingRequest(string pairingId, string keyHash)
    {
        public string PairingId { get; } = pairingId;
        public string KeyHash { get; } = keyHash;
        public TaskCompletionSource<Decision> Decision { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ITimer Timer { get; set; } = null!;
    }
}
