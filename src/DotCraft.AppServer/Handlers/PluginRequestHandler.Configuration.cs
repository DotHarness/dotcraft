using System.Text.Json;
using DotCraft.Configuration;
using DotCraft.Plugins;
using Contract = DotCraft.Protocol.AppServer;

namespace DotCraft.AppServer;

internal sealed partial class PluginRequestHandler
{
    private Task<AppServerTypedResult<Contract.PluginConfigSnapshot>> HandlePluginConfigGetAsync(
        AppServerTypedRequest<Contract.PluginConfigGetParams> request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var plugin = RequireConfigurablePlugin(
            request.Params.Id,
            Protocol.AppServer.AppServerMethodNames.PluginConfigGet);
        try
        {
            return Task.FromResult(AppServerTypedResult<Contract.PluginConfigSnapshot>.FromResult(
                MapPluginConfigSnapshot(pluginConfigStore!.Get(plugin.Manifest))));
        }
        catch (PluginConfigException exception)
        {
            throw AppServerErrors.PluginConfiguration(exception.Code, exception.Message);
        }
    }

    private async Task<AppServerTypedResult<Contract.PluginConfigSnapshot>> HandlePluginConfigMutateAsync(
        AppServerTypedRequest<Contract.PluginConfigMutateParams> request,
        CancellationToken cancellationToken)
    {
        var plugin = RequireConfigurablePlugin(
            request.Params.Id,
            Protocol.AppServer.AppServerMethodNames.PluginConfigMutate);
        var operations = request.Params.Operations.Select(operation => new PluginConfigMutation(
            operation.Op,
            operation.Key,
            operation.Value.IsSet ? operation.Value.Value : null)).ToArray();
        if (plugin.Manifest.Dotnet != null && dotnetRuntime == null)
        {
            throw AppServerErrors.PluginConfiguration(
                PluginConfigStore.WriteFailed,
                "The .NET plugin runtime coordinator is unavailable; no configuration was written.");
        }
        var commitToken = EnterMutationCommit(cancellationToken);
        PluginRuntimeMutationResult? quiesce = null;
        if (plugin.Manifest.Dotnet != null && dotnetRuntime != null)
        {
            quiesce = await dotnetRuntime.QuiesceForMutationAsync(plugin.Manifest.Id, commitToken)
                .ConfigureAwait(false);
            if (quiesce.Outcome == PluginRuntimeMutationOutcome.NotApplied)
            {
                var diagnostic = quiesce.Diagnostics.FirstOrDefault();
                throw AppServerErrors.PluginConfiguration(
                    diagnostic?.Code ?? PluginConfigStore.WriteFailed,
                    diagnostic?.Message ?? "The plugin runtime could not be quiesced; no configuration was written.");
            }
        }

        PluginConfigSnapshot snapshot;
        try
        {
            snapshot = pluginConfigStore!.Mutate(
                plugin.Manifest,
                request.Params.Scope,
                operations);
        }
        catch (PluginConfigException exception)
        {
            if (quiesce != null && dotnetRuntime != null)
            {
                await dotnetRuntime.ReconcileAfterMutationAsync(plugin.Manifest.Id, commitToken)
                    .ConfigureAwait(false);
            }
            throw AppServerErrors.PluginConfiguration(exception.Code, exception.Message);
        }

        if (quiesce != null && dotnetRuntime != null)
        {
            await dotnetRuntime.ReconcileAfterMutationAsync(plugin.Manifest.Id, commitToken)
                .ConfigureAwait(false);
        }
        appConfigMonitor?.NotifyChanged(
            Protocol.AppServer.AppServerMethodNames.PluginConfigMutate,
            [ConfigChangeRegions.PluginConfiguration]);
        return AppServerTypedResult<Contract.PluginConfigSnapshot>.FromResult(MapPluginConfigSnapshot(snapshot));
    }

    private DiscoveredPlugin RequireConfigurablePlugin(string id, string method)
    {
        if (pluginConfigStore == null)
            throw AppServerErrors.MethodNotFound(method);
        if (string.IsNullOrWhiteSpace(id))
            throw AppServerErrors.InvalidParams("'id' is required.");
        var canonicalId = PluginIds.Canonicalize(id.Trim());
        var plugin = RefreshPluginRuntime().Plugins.FirstOrDefault(candidate =>
            candidate.Installed && PluginIds.EqualsCanonical(candidate.Manifest.Id, canonicalId));
        if (plugin == null)
            throw AppServerErrors.InvalidParams($"Plugin '{canonicalId}' was not found or is not installed.");
        if (plugin.Manifest.Settings == null)
        {
            throw AppServerErrors.PluginConfiguration(
                PluginConfigStore.ConfigurationNotDeclared,
                $"Plugin '{canonicalId}' does not declare a settings schema.");
        }
        return plugin;
    }

    private void AppendPluginConfigDiagnostics(
        PluginDiscoveryResult discovery,
        List<PluginDiagnostic> diagnostics)
    {
        if (pluginConfigStore == null)
            return;
        foreach (var plugin in discovery.Plugins.Where(plugin => plugin.Installed && plugin.Manifest.Settings != null))
        {
            if (pluginConfigStore.GetDiagnostic(plugin.Manifest) is { } diagnostic)
                diagnostics.Add(diagnostic);
        }
    }

    private static Contract.PluginConfigSnapshot MapPluginConfigSnapshot(PluginConfigSnapshot snapshot) => new()
    {
        Schema = new Contract.PluginConfigSchema
        {
            Fields = snapshot.Schema.Fields.Select(MapPluginConfigField).ToArray()
        },
        Personal = snapshot.Personal,
        Workspace = snapshot.Workspace,
        Value = snapshot.Value,
        WritableScopes = snapshot.WritableScopes
    };

    private static Contract.PluginConfigField MapPluginConfigField(PluginSettingsField field) => new()
    {
        Key = field.Key,
        Type = field.Type,
        DefaultValue = field.DefaultValue is { } defaultValue
            ? DotCraft.Protocol.Optional<JsonElement>.FromValue(defaultValue)
            : default,
        Options = field.Options.Count > 0
            ? DotCraft.Protocol.Optional<IReadOnlyList<string>>.FromValue(field.Options)
            : default,
        Min = field.Min is { } min
            ? DotCraft.Protocol.Optional<double>.FromValue(min)
            : default,
        Max = field.Max is { } max
            ? DotCraft.Protocol.Optional<double>.FromValue(max)
            : default
    };
}
