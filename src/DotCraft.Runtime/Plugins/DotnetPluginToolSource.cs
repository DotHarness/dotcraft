using System.Text.Json;
using DotCraft.Contributions;
using DotCraft.Plugins;
using DotCraft.Tools;

namespace DotCraft.Runtime;

/// <summary>
/// The aggregate Host-owned Tool source. It serves every plugin-origin <see cref="IToolSource"/>
/// wrapped, which is why the general contribution resolution withholds them (<see cref="ToolSourceContributions"/>).
/// </summary>
internal sealed class DotnetPluginToolSource : IToolSource, IThreadScopedToolSource, IThreadForkToolBindingSource
{
    /// <summary>The stable identifier of the aggregate .NET plugin Tool source.</summary>
    public const string Id = "dotnet-plugins";

    private readonly IContributionView _contributions;
    private readonly PluginCallGateRegistry _callGates;
    private volatile IReadOnlyList<PluginDiagnostic> _diagnostics = [];
    private volatile IReadOnlyDictionary<string, IReadOnlyList<PluginRuntimeToolInfo>> _described =
        new Dictionary<string, IReadOnlyList<PluginRuntimeToolInfo>>(StringComparer.Ordinal);

    /// <summary>Creates the aggregate plugin Tool source.</summary>
    internal DotnetPluginToolSource(IContributionView contributions, PluginCallGateRegistry callGates)
    {
        _contributions = contributions ?? throw new ArgumentNullException(nameof(contributions));
        _callGates = callGates ?? throw new ArgumentNullException(nameof(callGates));
    }

    /// <inheritdoc />
    public string SourceId => Id;

    /// <inheritdoc />
    public int Priority => 100;

    /// <summary>Gets the projection findings from the most recent planning pass.</summary>
    public IReadOnlyList<PluginDiagnostic> Diagnostics => _diagnostics;

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<ToolRegistration>> GetRegistrationsAsync(
        ToolPlanningContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        var revision = _contributions.GetRevision<IToolSource>();
        var diagnostics = new List<PluginDiagnostic>();
        var registrations = new List<ToolRegistration>();
        var described = new Dictionary<string, List<PluginRuntimeToolInfo>>(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var (pluginId, generationId, source) in PluginSources(context.ThreadId))
        {
            var contributed = await CollectAsync(
                    source,
                    pluginId,
                    generationId,
                    context,
                    diagnostics,
                    cancellationToken)
                .ConfigureAwait(false);
            foreach (var registration in contributed)
            {
                if (registration == null)
                {
                    diagnostics.Add(Invalid(pluginId, null, "A plugin Tool registration is required."));
                    continue;
                }

                var toolId = registration.Definition.Id.SourceToolId.Value;
                if (!seen.Add($"{pluginId}\0{generationId}\0{toolId}"))
                {
                    diagnostics.Add(Invalid(
                        pluginId,
                        toolId,
                        $"Plugin Tool id '{toolId}' is contributed more than once and the duplicate was skipped."));
                    continue;
                }

                var wrapped = DotnetPluginToolProjection.Wrap(
                    _contributions,
                    _callGates,
                    pluginId,
                    generationId,
                    context,
                    registration,
                    revision);
                registrations.Add(wrapped);
                Describe(described, pluginId, generationId, wrapped.Definition);
            }
        }

        _diagnostics = diagnostics;
        _described = described.ToDictionary(
            static pair => pair.Key,
            static pair => (IReadOnlyList<PluginRuntimeToolInfo>)pair.Value.ToArray(),
            StringComparer.Ordinal);
        return registrations;
    }

    /// <summary>Describes the Tools one live generation contributed on the most recent planning pass.</summary>
    public IReadOnlyList<PluginRuntimeToolInfo> DescribeTools(string pluginId, string generationId) =>
        _described.TryGetValue(DescribeKey(pluginId, generationId), out var tools) ? tools : [];

    /// <inheritdoc />
    public async ValueTask ReleaseThreadAsync(string threadId, CancellationToken cancellationToken = default)
    {
        foreach (var (pluginId, generationId, source) in PluginSources(threadId))
        {
            if (source is not IThreadScopedToolSource scoped)
                continue;

            using var lease = _callGates.TryEnterCall(pluginId, generationId);
            if (lease == null)
                continue;

            try
            {
                await scoped.ReleaseThreadAsync(threadId, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // A plugin that fails to release a thread cannot fail the Host's own release pass.
            }
        }
    }

    /// <inheritdoc />
    public bool TryForkThreadBinding(string parentThreadId, string childThreadId)
    {
        var forked = false;
        foreach (var (pluginId, generationId, source) in PluginSources(parentThreadId))
        {
            if (source is not IThreadForkToolBindingSource forkable)
                continue;

            using var lease = _callGates.TryEnterCall(pluginId, generationId);
            if (lease == null)
                continue;

            try
            {
                forked |= forkable.TryForkThreadBinding(parentThreadId, childThreadId);
            }
            catch
            {
                // A plugin that cannot fork its own binding leaves the child without it, nothing more.
            }
        }

        return forked;
    }

    /// <summary>Runs the plugin's own planning code inside the generation's drain gate.</summary>
    private async ValueTask<IReadOnlyList<ToolRegistration>> CollectAsync(
        IToolSource source,
        string pluginId,
        string generationId,
        ToolPlanningContext context,
        List<PluginDiagnostic> diagnostics,
        CancellationToken cancellationToken)
    {
        using var lease = _callGates.TryEnterCall(pluginId, generationId);
        if (lease == null)
            return [];

        try
        {
            var registrations = await source.GetRegistrationsAsync(context, cancellationToken).ConfigureAwait(false);
            return registrations?.ToArray() ?? [];
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            diagnostics.Add(Invalid(pluginId, null, PluginGeneration.CopyExceptionMessage(exception)));
            return [];
        }
    }

    private IEnumerable<(string PluginId, string GenerationId, IToolSource Source)> PluginSources(string? threadId)
    {
        foreach (var entry in _contributions.ResolveEntries<IToolSource>(threadId))
        {
            if (entry.Origin.Kind == ContributionOriginKind.Plugin
                && entry.Origin.Name is { } pluginId
                && entry.Origin.Generation is { } generationId)
            {
                yield return (pluginId, generationId, entry.Contribution);
            }
        }
    }

    private static void Describe(
        Dictionary<string, List<PluginRuntimeToolInfo>> described,
        string pluginId,
        string generationId,
        ToolDefinition definition)
    {
        var key = DescribeKey(pluginId, generationId);
        if (!described.TryGetValue(key, out var tools))
            described[key] = tools = [];
        tools.Add(new PluginRuntimeToolInfo(
            definition.Id.SourceToolId.Value,
            definition.Name.Namespace,
            definition.Name.Name,
            definition.Description));
    }

    private static string DescribeKey(string pluginId, string generationId) => $"{pluginId}\0{generationId}";

    private static PluginDiagnostic Invalid(string pluginId, string? toolId, string reason) =>
        PluginDiagnostic.Warning(
            "PluginToolContributionInvalid",
            reason,
            pluginId: pluginId,
            functionName: toolId,
            parameters: new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            {
                ["toolId"] = JsonSerializer.SerializeToElement(toolId),
                ["reason"] = JsonSerializer.SerializeToElement(reason)
            });
}
