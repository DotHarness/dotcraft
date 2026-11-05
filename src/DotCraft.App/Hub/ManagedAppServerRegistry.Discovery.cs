using DotCraft.AppServer;
using DotCraft.Common;
using Microsoft.AspNetCore.Http;

namespace DotCraft.Hub;

public sealed partial class ManagedAppServerRegistry
{
    private async Task<HubAppServerResponse?> TryUseExistingWorkspaceLockAsync(
        ManagedEntry entry,
        string craftPath,
        CancellationToken cancellationToken)
    {
        var lockPath = AppServerWorkspaceLock.GetLockFilePath(craftPath);
        var info = AppServerWorkspaceLock.TryRead(lockPath);
        if (info is null)
            return null;

        if (!AppServerWorkspaceLock.IsHeld(lockPath))
            return null;

        if (entry.Process is { IsRunning: true } && entry.Process.ProcessId == info.Pid)
            return null;

        var probeError = await ExistingAppServerProbeAsync(info, cancellationToken);
        if (probeError is null)
        {
            var response = ToExternalResponse(entry, info);
            PersistExternal(entry, info);
            _events.Publish("appserver.running", entry.CanonicalWorkspacePath, new
            {
                pid = info.Pid,
                endpoints = info.Endpoints,
                external = true
            });
            return response;
        }

        throw new HubProtocolException(
            "workspaceLocked",
            "A live process appears to own the workspace AppServer lock.",
            StatusCodes.Status409Conflict,
            new { workspacePath = entry.CanonicalWorkspacePath, lockPath, pid = info.Pid, reason = probeError });
    }

    private static void VerifyManagedLock(string craftPath, int expectedPid, string canonicalWorkspacePath)
    {
        var lockPath = AppServerWorkspaceLock.GetLockFilePath(craftPath);
        var info = AppServerWorkspaceLock.TryRead(lockPath);
        if (info is { ManagedByHub: true }
            && info.Pid == expectedPid
            && AppServerWorkspaceLock.IsHeld(lockPath))
            return;

        throw new HubProtocolException(
            "appServerUnhealthy",
            "Managed AppServer did not publish the expected workspace lock.",
            StatusCodes.Status500InternalServerError,
            new { workspacePath = canonicalWorkspacePath, expectedPid, lockPath });
    }

    private void PersistExternal(ManagedEntry entry, AppServerLockInfo info)
    {
        ApplyExternalLockInfo(entry, info);
        Persist(entry);
    }

    private static HubAppServerResponse ToExternalResponse(ManagedEntry entry, AppServerLockInfo info) => new(
        entry.WorkspacePath,
        entry.CanonicalWorkspacePath,
        HubAppServerStates.Running,
        info.Pid,
        info.Endpoints,
        ExternalServiceStatus(info.Endpoints),
        info.Version,
        StartedByHub: false,
        ExitCode: null,
        LastError: null,
        RecentStderr: null);

    private static IReadOnlyDictionary<string, HubServiceStatus> ExternalServiceStatus(
        IReadOnlyDictionary<string, string> endpoints) =>
        endpoints.ToDictionary(
            pair => pair.Key,
            pair => new HubServiceStatus("external", pair.Value),
            StringComparer.OrdinalIgnoreCase);

    private static void ApplyExternalLockInfo(ManagedEntry entry, AppServerLockInfo info)
    {
        entry.Process = null;
        entry.AdoptedExternalLock = true;
        entry.State = HubAppServerStates.Running;
        entry.Pid = info.Pid;
        entry.ServerVersion = info.Version;
        entry.StartedByHub = false;
        entry.StartedInCurrentHubProcess = false;
        entry.ExitCode = null;
        entry.LastError = null;
        entry.RecentStderr = null;
        entry.LastStartedAt = info.StartedAt;
        entry.LastSeenAt = DateTimeOffset.UtcNow;
        entry.Endpoints = info.Endpoints;
        entry.ServiceStatus = ExternalServiceStatus(info.Endpoints);
    }

    private static void RefreshAdoptedExternalEntry(ManagedEntry entry, string craftPath)
    {
        var lockPath = AppServerWorkspaceLock.GetLockFilePath(craftPath);
        var info = AppServerWorkspaceLock.TryRead(lockPath);
        if (info is { } live && AppServerWorkspaceLock.IsHeld(lockPath))
        {
            ApplyExternalLockInfo(entry, live);
            return;
        }

        entry.Process = null;
        entry.AdoptedExternalLock = false;
        entry.State = HubAppServerStates.Exited;
        entry.StartedByHub = false;
        entry.StartedInCurrentHubProcess = false;
        entry.LastSeenAt = DateTimeOffset.UtcNow;
    }

    private static async Task<string?> ProbeExistingAppServerAsync(
        AppServerLockInfo info,
        CancellationToken cancellationToken)
    {
        if (!info.Endpoints.TryGetValue("appServerWebSocket", out var wsUrl)
            || string.IsNullOrWhiteSpace(wsUrl))
        {
            return "Workspace AppServer lock does not publish an appServerWebSocket endpoint.";
        }

        if (!Uri.TryCreate(wsUrl, UriKind.Absolute, out var uri))
            return "Workspace AppServer lock publishes an invalid appServerWebSocket endpoint.";

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(3));
            await using var connection = await WebSocketClientConnection.ConnectAsync(uri, null, cts.Token);
            var response = await connection.Wire.InitializeAsync(
                clientName: "dotcraft-hub-reconnect",
                clientVersion: AppVersion.Informational,
                approvalSupport: false,
                streamingSupport: true);
            response.Dispose();
            return null;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return "Workspace AppServer WebSocket probe timed out.";
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return "Workspace AppServer WebSocket probe failed: " + ex.Message;
        }
    }

}
