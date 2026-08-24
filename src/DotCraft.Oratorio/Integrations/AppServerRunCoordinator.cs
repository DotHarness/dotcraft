using System.Collections.Concurrent;
using System.Net.WebSockets;
using Microsoft.EntityFrameworkCore;
using DotCraft.Oratorio.Api;
using DotCraft.Oratorio.Data;
using DotCraft.Oratorio.Realtime;

namespace DotCraft.Oratorio.Integrations;

/// <summary>
/// Coordinates active AppServer run bindings for drawer follow-up operations.
/// </summary>
public interface IAppServerRunCoordinator
{
    void RegisterRun(string runId, IDotCraftAppServerClient client, string threadId, string? turnId);
    void UpdateRunStatus(string runId, string? turnId, string turnStatus);
    void UnregisterRun(string runId);
    Task<DrawerSnapshotResponse> GetSnapshotAsync(string runId, CancellationToken ct);
    Task<SubmitTurnResponse> SubmitTurnAsync(string runId, IReadOnlyList<TurnInputPartDto> input, string? modelId, CancellationToken ct);
    Task<InterruptResponse> InterruptTurnAsync(string runId, CancellationToken ct);
    Task<bool> TryInterruptTurnAsync(string runId, CancellationToken ct);
    Task<IReadOnlyList<ModelInfoDto>> GetModelsAsync(string? endpointUrl, CancellationToken ct);
}

public sealed class AppServerRunCoordinator(
    IServiceScopeFactory scopeFactory,
    DrawerStateService drawerState,
    IDotCraftAppServerClientFactory clientFactory,
    IDotCraftAppServerProcessManager processManager,
    IDotCraftAppServerEndpointResolver endpointResolver,
    IDotCraftWorkspaceResolver workspaceResolver) : IAppServerRunCoordinator
{
    private static readonly TimeSpan ModelCacheTtl = TimeSpan.FromMinutes(5);
    private readonly ConcurrentDictionary<string, RunBinding> _bindings = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _modelCacheLock = new(1, 1);
    private IReadOnlyList<ModelInfoDto>? _cachedModels;
    private DateTimeOffset _modelsCachedAt;

    public void RegisterRun(string runId, IDotCraftAppServerClient client, string threadId, string? turnId)
    {
        _bindings[runId] = new RunBinding(client, threadId, turnId, "running");
    }

    public void UpdateRunStatus(string runId, string? turnId, string turnStatus)
    {
        _bindings.AddOrUpdate(
            runId,
            _ => new RunBinding(null, "", turnId, turnStatus),
            (_, existing) => existing with
            {
                CurrentTurnId = string.IsNullOrWhiteSpace(turnId) ? existing.CurrentTurnId : turnId,
                TurnStatus = turnStatus
            });
    }

    public void UnregisterRun(string runId)
    {
        _bindings.TryRemove(runId, out _);
    }

    public async Task<DrawerSnapshotResponse> GetSnapshotAsync(string runId, CancellationToken ct)
    {
        var cached = drawerState.GetSnapshot(runId);
        if (cached.FromCache)
        {
            return cached;
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OratorioDbContext>();
        var run = await db.Runs.AsNoTracking()
            .Include(x => x.Item)
            .FirstOrDefaultAsync(x => x.RunId == runId, ct)
            ?? throw OratorioApiException.RunNotFound(runId);

        var summary = ToRunSummary(run, null);
        drawerState.UpdateRunStatus(runId, summary, DateTimeOffset.UtcNow);

        if (!string.IsNullOrWhiteSpace(run.ThreadId) && !string.IsNullOrWhiteSpace(run.AppServerEndpoint))
        {
            try
            {
                await using var client = await clientFactory.ConnectAsync(run.AppServerEndpoint!, ct, endpointResolver.ResolveConfiguredToken());
                var read = await client.ReadThreadAsync(run.ThreadId!, ct);
                foreach (var item in read.Items)
                {
                    drawerState.UpsertItem(runId, item, item.CompletedAt ?? item.CreatedAt ?? DateTimeOffset.UtcNow);
                }
            }
            catch (Exception ex) when (ex is WebSocketException or InvalidOperationException or OperationCanceledException)
            {
                if (ex is OperationCanceledException && ct.IsCancellationRequested)
                {
                    throw;
                }
            }
        }

        var snapshot = drawerState.GetSnapshot(runId);
        return snapshot with { FromCache = false };
    }

    public async Task<SubmitTurnResponse> SubmitTurnAsync(string runId, IReadOnlyList<TurnInputPartDto> input, string? modelId, CancellationToken ct)
    {
        if (input.Count == 0 || input.All(part => string.IsNullOrWhiteSpace(part.Text) && string.IsNullOrWhiteSpace(part.Url) && string.IsNullOrWhiteSpace(part.Path)))
        {
            throw OratorioApiException.Validation("Turn input cannot be empty.");
        }

        if (!_bindings.TryGetValue(runId, out var binding) || binding.Client is null || string.IsNullOrWhiteSpace(binding.ThreadId))
        {
            throw new OratorioApiException(
                StatusCodes.Status422UnprocessableEntity,
                "runNotActive",
                "The requested run is not connected to an active AppServer client.",
                new Dictionary<string, object?> { ["runId"] = runId });
        }

        if (string.Equals(binding.TurnStatus, "running", StringComparison.OrdinalIgnoreCase))
        {
            var queuedInputId = await binding.Client.EnqueueTurnAsync(binding.ThreadId, input, ct);
            return new SubmitTurnResponse("queued", null, queuedInputId);
        }

        var turnId = await binding.Client.StartTurnAsync(binding.ThreadId, input, modelId, ct);
        UpdateRunStatus(runId, turnId, "running");
        return new SubmitTurnResponse("started", turnId, null);
    }

    public async Task<InterruptResponse> InterruptTurnAsync(string runId, CancellationToken ct)
    {
        if (!await TryInterruptTurnAsync(runId, ct))
        {
            throw new OratorioApiException(
                StatusCodes.Status422UnprocessableEntity,
                "runNotInterruptible",
                "The requested run does not have an active turn to interrupt.",
                new Dictionary<string, object?> { ["runId"] = runId });
        }

        return new InterruptResponse(true);
    }

    public async Task<bool> TryInterruptTurnAsync(string runId, CancellationToken ct)
    {
        if (!_bindings.TryGetValue(runId, out var binding) ||
            binding.Client is null ||
            string.IsNullOrWhiteSpace(binding.ThreadId) ||
            string.IsNullOrWhiteSpace(binding.CurrentTurnId))
        {
            return false;
        }

        await binding.Client.InterruptTurnAsync(binding.ThreadId, binding.CurrentTurnId!, ct);
        return true;
    }

    public async Task<IReadOnlyList<ModelInfoDto>> GetModelsAsync(string? endpointUrl, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        if (_cachedModels is not null && now - _modelsCachedAt < ModelCacheTtl)
        {
            return _cachedModels;
        }

        await _modelCacheLock.WaitAsync(ct);
        try
        {
            now = DateTimeOffset.UtcNow;
            if (_cachedModels is not null && now - _modelsCachedAt < ModelCacheTtl)
            {
                return _cachedModels;
            }

            var activeClient = _bindings.Values.Select(x => x.Client).FirstOrDefault(x => x is not null);
            if (activeClient is not null)
            {
                _cachedModels = await activeClient.ListModelsAsync(ct);
                _modelsCachedAt = now;
                return _cachedModels;
            }

            var resolvedEndpoint = endpointUrl;
            var resolvedToken = endpointResolver.ResolveConfiguredToken();
            if (string.IsNullOrWhiteSpace(resolvedEndpoint))
            {
                var workspacePath = workspaceResolver.ResolveWorkspacePath(null);
                var endpoint = await processManager.EnsureAvailableAsync(workspacePath, ct);
                resolvedEndpoint = endpoint.Url;
                resolvedToken = endpoint.Token ?? resolvedToken;
            }

            await using var client = await clientFactory.ConnectAsync(resolvedEndpoint!, ct, resolvedToken);
            _cachedModels = await client.ListModelsAsync(ct);
            _modelsCachedAt = now;
            return _cachedModels;
        }
        finally
        {
            _modelCacheLock.Release();
        }
    }

    private static RunSummaryDto ToRunSummary(OratorioRun run, string? turnStatus) =>
        new(
            run.RunId,
            run.Status.ToString(),
            run.ThreadId,
            run.TurnId,
            turnStatus,
            run.ErrorCode,
            run.ErrorMessage,
            run.ProgressPercent,
            run.StatusMessage,
            run.LastHeartbeatAt ?? run.CompletedAt ?? run.StartedAt);

    private sealed record RunBinding(
        IDotCraftAppServerClient? Client,
        string ThreadId,
        string? CurrentTurnId,
        string TurnStatus);
}
