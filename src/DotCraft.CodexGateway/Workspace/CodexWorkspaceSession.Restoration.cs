using System.Collections.Concurrent;
using System.Text.Json;
using DotCraft.Sdk.Wire;

namespace DotCraft.CodexGateway.Workspace;

public sealed partial class CodexWorkspaceSession
{
    private readonly ConcurrentDictionary<string, TaskCompletionSource> _attachmentReady = new();
    private readonly SemaphoreSlim _attachmentLock = new(1, 1);
    private Task _restoration = Task.CompletedTask;

    private void ConnectionChanged(WireConnectionState state, Exception? error)
    {
        if (_lifetime.IsCancellationRequested) return;
        if (state != WireConnectionState.Ready)
        {
            foreach (var thread in _attached.Keys)
                _attachmentReady[thread] = new(TaskCreationOptions.RunContinuationsAsynchronously);
            return;
        }
        _restoration = RestoreConnectionsAsync(_lifetime.Token);
    }

    private async Task RestoreConnectionsAsync(CancellationToken ct)
    {
        foreach (var thread in _attached.Keys)
        {
            try { await AttachAsync(thread, default, ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
            catch (Exception error)
            {
                var threadId = _routes.External("thread", thread);
                System.Diagnostics.Trace.TraceWarning("codex_gateway.sourceObservationFailed thread={0} errorType={1}",
                    threadId, error.GetType().Name);
                await _send(WorkspaceJson.Element(new
                {
                    method = "thread/status/changed", @params = new
                    {
                        threadId, status = WorkspaceProjector.Status(null)
                    }
                }), ct);
            }
        }
    }

    private async Task EnsureAttachedAsync(string thread, CancellationToken ct)
    {
        if (_attached.ContainsKey(thread))
        {
            if (_attachmentReady.TryGetValue(thread, out var ready))
                await ready.Task.WaitAsync(ct);
            return;
        }
        await AttachAsync(thread, default, ct);
    }

    private async Task<JsonElement> AttachAsync(string thread, JsonElement p, CancellationToken ct)
    {
        await _attachmentLock.WaitAsync(ct);
        var ready = _attachmentReady.GetOrAdd(thread, _ => new(TaskCreationOptions.RunContinuationsAsynchronously));
        if (ready.Task.IsCompleted)
            _attachmentReady[thread] = ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _attached[thread] = 0;
        try
        {
            var result = await RequestAsync("thread/resume", new
            {
                threadId = thread, cwd = p.Text("cwd"),
                runtimeWorkspaceRoots = p.Get("runtimeWorkspaceRoots").Value(),
                initialTurnsPage = ResumePageOptions(p)
            }, ct);
            await RestoreCurrentStateAsync(thread, result, ct);
            ready.TrySetResult();
            return result;
        }
        catch
        {
            _attached.TryRemove(thread, out _);
            ready.TrySetResult();
            throw;
        }
        finally { _attachmentLock.Release(); }
    }

    private static object? ResumePageOptions(JsonElement p)
    {
        if (p.ValueKind != JsonValueKind.Object) return null;
        var initial = p.Get("initialTurnsPage");
        if (initial.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null) return null;
        if (initial.ValueKind != JsonValueKind.Object)
            throw new ArgumentException("initialTurnsPage must be an object.");
        var direction = Direction(initial, "desc");
        var view = initial.Text("itemsView") ?? "summary";
        if (view is not ("summary" or "full" or "notLoaded"))
            throw new ArgumentException("Unsupported itemsView.");
        var limit = initial.Limit(20);
        return direction == "asc" ? null : new { limit, itemsView = view == "summary" ? "summary" : "none" };
    }

    private async Task RestoreCurrentStateAsync(string thread, JsonElement result, CancellationToken ct)
    {
        var threadId = _routes.External("thread", thread);
        await _send(WorkspaceJson.Element(new
        {
            method = "thread/status/changed",
            @params = new { threadId, status = WorkspaceProjector.RuntimeStatus(result.Get("thread").Get("runtime")) }
        }), ct);
        var current = result.Get("currentState");
        if (current.Text("turnId") is not { } turn) return;
        var turnId = _routes.External("turn", thread, turn);
        foreach (var entry in current.Get("activeItems").Rows()
                     .Select(source => (Source: source, Item: _projector.Item(thread, turn, source)))
                     .Where(entry => entry.Item is not null).GroupBy(entry => (string)entry.Item!["id"]!)
                     .Select(group => group.Last()))
        {
            var source = entry.Source;
            var item = entry.Item!;
            RecordItemStart((string)item["id"]!);
            await _send(WorkspaceJson.Element(new
            {
                method = "item/started",
                @params = new { threadId, turnId, item }
            }), ct);
        }
        if (current.Text("diff") is { } diff)
            await _send(WorkspaceJson.Element(new { method = "turn/diff/updated", @params = new { threadId, turnId, diff } }), ct);
    }
}
