using System.Collections.Concurrent;
using System.Text.Json;
using DotCraft.AppServer;
using DotCraft.Common;
using DotCraft.Protocol.AppServer;
using DotCraft.Sessions.Wire;

namespace DotCraft.Hub;

internal sealed class HubLocalImportConnection(ManagedAppServerRegistry registry)
{
    private readonly ConcurrentDictionary<string, AttentionSnapshot> _attention = new(ManagedAppServerRegistry.WorkspaceComparer);

    public async Task<ImportCompletedNotification> RunAsync(
        string projectPath, ImportRunParams request, bool startIfMissing, Action<int> progress, CancellationToken ct)
    {
        var appServer = await registry.EnsureAsync(new EnsureAppServerRequest
        {
            WorkspacePath = projectPath, StartIfMissing = startIfMissing
        }, ct);
        await using var connection = await OpenAsync(appServer, ct);
        _attention.TryRemove(projectPath, out _);
        using var started = await connection.Wire.SendRequestAsync("import/run", request, ct: ct);
        var result = ReadResult<ImportRunResult>(started.RootElement);

        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromMinutes(10);
        while (deadline - DateTimeOffset.UtcNow is { Ticks: > 0 } remaining)
        {
            using var notification = await connection.Wire.WaitForNotificationAsync(timeout: remaining, ct: ct);
            ct.ThrowIfCancellationRequested();
            if (notification is null) break;
            var payload = notification.RootElement;
            if (!payload.TryGetProperty("params", out var parameters)
                || !parameters.TryGetProperty("importId", out var importId) || importId.GetString() != result.ImportId)
                continue;

            switch (payload.GetProperty("method").GetString())
            {
                case "import/completed":
                    return parameters.Deserialize<ImportCompletedNotification>(SessionWireJsonOptions.Default)
                        ?? throw new InvalidDataException("The destination returned no import result.");
                case "import/progress":
                    if (parameters.TryGetProperty("source", out var source) && source.GetString() is { } sourceId
                        && parameters.TryGetProperty("completed", out var completed) && completed.TryGetInt32(out var count))
                    {
                        counts[sourceId] = count;
                        progress(counts.Values.Sum());
                    }
                    break;
            }
        }

        using var history = await connection.Wire.SendRequestAsync("import/history/list", new { }, ct: ct);
        return ReadResult<ImportHistoryResult>(history.RootElement).Imports.FirstOrDefault(batch => batch.ImportId == result.ImportId)
            ?? throw new TimeoutException("The destination did not report the import results.");
    }

    public async Task<HubLocalImportState> AddLiveAttentionAsync(HubLocalImportState state, string chatPath, CancellationToken ct)
    {
        var candidates = state.Imports.SelectMany(batch => batch.Outcomes)
            .Where(outcome => (outcome.Status is "imported" or "attention") && (outcome.Category is "mcp" or "plugins"))
            .Select(outcome => outcome.ProjectPath ?? chatPath).Distinct(ManagedAppServerRegistry.WorkspaceComparer)
            .ToArray();
        if (candidates.Length == 0)
            return state;

        var livePaths = registry.RunningWorkspaces();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(2));
        var attention = new ConcurrentBag<HubLocalImportOutcome>();
        try
        {
            await Parallel.ForEachAsync(candidates.Where(livePaths.Contains),
                new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = timeout.Token },
                async (path, token) =>
                {
                    var now = DateTimeOffset.UtcNow;
                    if (!_attention.TryGetValue(path, out var cached) || cached.ExpiresAt < now)
                    {
                        try
                        {
                            await using var connection = await OpenAsync(registry.GetByWorkspace(path), token);
                            using var history = await connection.Wire.SendRequestAsync("import/history/list", new { }, ct: token);
                            var name = ManagedAppServerRegistry.WorkspaceComparer.Equals(path, chatPath) ? null : Path.GetFileName(path);
                            var outcomes = ReadResult<ImportHistoryResult>(history.RootElement).Attention
                                .Select(outcome => HubLocalImportOutcome.AtDestination(outcome, path, name)).ToArray();
                            cached = new AttentionSnapshot(now.AddSeconds(15), outcomes);
                            _attention[path] = cached;
                        }
                        catch (Exception ex) when (ex is not OperationCanceledException) { return; }
                    }

                    foreach (var outcome in cached.Outcomes)
                        attention.Add(outcome);
                });
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { }

        return state with { Attention = state.Attention.Concat(attention).Distinct().ToArray() };
    }

    private static async Task<WebSocketClientConnection> OpenAsync(HubAppServerResponse appServer, CancellationToken ct)
    {
        if (appServer.State != HubAppServerStates.Running
            || !appServer.Endpoints.TryGetValue("appServerWebSocket", out var endpoint)
            || !Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) || !uri.IsLoopback)
            throw new InvalidOperationException("The destination cannot accept local import requests.");

        var connection = await WebSocketClientConnection.ConnectAsync(uri, ct: ct);
        try
        {
            using var initialized = await connection.Wire.InitializeAsync(
                clientName: "dotcraft-hub-import", clientVersion: AppVersion.Informational,
                approvalSupport: false, streamingSupport: false, ct: ct);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    private sealed record AttentionSnapshot(DateTimeOffset ExpiresAt, IReadOnlyList<HubLocalImportOutcome> Outcomes);

    private static T ReadResult<T>(JsonElement response) where T : class =>
        response.TryGetProperty("result", out var result)
            ? result.Deserialize<T>(SessionWireJsonOptions.Default)
                ?? throw new InvalidDataException("The destination sent an empty import response.")
            : throw new InvalidOperationException("The destination rejected the import request.");
}
