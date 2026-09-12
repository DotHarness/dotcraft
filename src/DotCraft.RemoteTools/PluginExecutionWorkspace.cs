using DotCraft.Configuration;
using DotCraft.Runtime;
using DotCraft.Security;
using DotCraft.Tools;
using DotCraft.Tools.BackgroundTerminals;
using DotCraft.Workspaces;
using Microsoft.Extensions.DependencyInjection;

namespace DotCraft.RemoteTools;

internal sealed class PluginExecutionWorkspace : IAsyncDisposable
{
    private readonly PluginExecutionHost _host;
    private readonly ServiceProvider _services;
    private readonly DotCraftPaths _paths;
    private readonly SemaphoreSlim _preparing = new(1, 1);
    private readonly object _gate = new();
    private readonly Dictionary<string, RemotePluginBundle> _sources = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ThreadCatalog> _catalogs = new(StringComparer.Ordinal);

    internal PluginExecutionWorkspace(DotCraftPaths paths, AppConfig config, IBackgroundTerminalService terminals)
    {
        _paths = paths;
        _services = new ServiceCollection().AddLogging().AddSingleton(paths).AddSingleton(config)
            .AddSingleton<IApprovalService>(new HostInvocationApprovalService())
            .AddSingleton(terminals).BuildServiceProvider();
        _host = new PluginExecutionHost(paths, _services);
    }

    internal async Task<PluginActivateResponse> PrepareAsync(PluginPrepareRequest request,
        IReadOnlyList<RemotePluginBundleFiles> files, CancellationToken ct, Action? store = null)
    {
        await _preparing.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            lock (_gate)
            {
                if (_catalogs.TryGetValue(request.ThreadId, out var previous) && previous.Revision > request.SnapshotRevision)
                    throw Unavailable("A newer thread snapshot is already prepared.");
                foreach (var file in files)
                {
                    var incoming = file.Bundle;
                    if (_sources.TryGetValue(incoming.PluginId, out var current)
                        && (current.SourceRevision > incoming.SourceRevision
                            || (current.SourceRevision == incoming.SourceRevision
                                && (current.SourceGeneration != incoming.SourceGeneration
                                    || current.ContentFingerprint != incoming.ContentFingerprint
                                    || current.Settings.GetRawText() != incoming.Settings.GetRawText()))))
                        throw Unavailable("A newer or different plugin source is already prepared.");
                }
                foreach (var file in files) _sources[file.Bundle.PluginId] = file.Bundle;
                _catalogs.Remove(request.ThreadId);
            }
            try { await _host.PrepareAsync(files, ct).ConfigureAwait(false); }
            catch (InvalidOperationException exception) { throw Unavailable(exception.Message); }
            var registrations = await _host.GetRegistrationsAsync(new ToolPlanningContext(request.ThreadId, null,
                _paths.WorkspacePath, _paths.Data.RootPath, request.Mode, null, [], request.SnapshotRevision,
                workspaceRoots: [_paths.WorkspacePath]), ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            var tools = new Dictionary<string, PreparedTool>(StringComparer.Ordinal);
            foreach (var expected in request.Tools)
            {
                var registration = registrations.SingleOrDefault(item => item.Definition.Id.ToString() == expected.DefinitionId
                    && RemoteToolMetadata.IsRpcEligible(item));
                if (registration is null)
                    throw Unavailable($"The prepared plugin does not export '{expected.ToolName}'.");
                var hash = RemoteToolContractHasher.Compute(registration.Definition);
                if (hash != expected.ContractHash || registration.Definition.Name.ToString() != expected.ToolName)
                    throw new RemoteToolHostException(RemoteToolErrorCodes.ToolContractMismatch,
                        $"The prepared plugin contract differs for '{expected.ToolName}'.");
                var source = files.Single(file => file.Bundle.PluginId == registration.Definition.Id.SourceId).Bundle;
                tools.Add(expected.DefinitionId, new(registration, source.SourceGeneration,
                    new(expected.DefinitionId, hash, "binding_" + Guid.NewGuid().ToString("N"))));
            }
            store?.Invoke();
            lock (_gate)
            {
                _catalogs[request.ThreadId] = new(request.SnapshotRevision, tools);
            }
            return new(tools.Values.Select(tool => tool.Binding).ToArray());
        }
        finally { _preparing.Release(); }
    }

    internal IReadOnlyList<ToolRegistration> List(string? threadId)
    {
        lock (_gate)
            return threadId is not null && _catalogs.TryGetValue(threadId, out var catalog)
                ? catalog.Tools.Values.Where(IsCurrent).Select(tool => tool.Registration).ToArray() : [];
    }

    internal ToolRegistration Resolve(RemoteInvocationMeta invocation)
    {
        lock (_gate)
        {
            if (invocation.ThreadId is null || !_catalogs.TryGetValue(invocation.ThreadId, out var catalog)
                || catalog.Revision != invocation.SnapshotRevision
                || !catalog.Tools.TryGetValue(invocation.DefinitionId, out var tool)
                || tool.Binding.BindingId != invocation.PreparedBinding || !IsCurrent(tool))
                throw Unavailable("The prepared plugin binding is no longer available.");
            return tool.Registration;
        }
    }

    private bool IsCurrent(PreparedTool tool) => _sources.TryGetValue(tool.Registration.Definition.Id.SourceId, out var source)
        && source.SourceGeneration == tool.SourceGeneration;

    internal async Task ReleaseThreadAsync(string threadId, CancellationToken ct)
    {
        await _preparing.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            lock (_gate) _catalogs.Remove(threadId);
            await _host.ReleaseThreadAsync(threadId, ct).ConfigureAwait(false);
        }
        finally { _preparing.Release(); }
    }

    public async ValueTask DisposeAsync()
    {
        await _preparing.WaitAsync().ConfigureAwait(false);
        try
        {
            lock (_gate) _catalogs.Clear();
            await _host.DisposeAsync().ConfigureAwait(false);
            await _services.DisposeAsync().ConfigureAwait(false);
        }
        finally { _preparing.Release(); }
    }

    private static RemoteToolHostException Unavailable(string message) => new(RemoteToolErrorCodes.RemoteToolUnavailable, message);
    private sealed record PreparedTool(ToolRegistration Registration, string SourceGeneration, PluginPreparedBinding Binding);
    private sealed record ThreadCatalog(long Revision, IReadOnlyDictionary<string, PreparedTool> Tools);
}
