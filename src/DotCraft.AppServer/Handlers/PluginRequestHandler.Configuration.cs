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
        var manifest = RequireConfigurablePlugin(
            request.Params.Id,
            Protocol.AppServer.AppServerMethodNames.PluginConfigGet);
        try
        {
            var snapshot = manifest.Channel is { } channel
                ? channelPlugins!.ReadConfig(channel).Settings
                : pluginConfigStore!.Get(manifest);
            return Task.FromResult(AppServerTypedResult<Contract.PluginConfigSnapshot>.FromResult(
                MapPluginConfigSnapshot(snapshot)));
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
        var manifest = RequireConfigurablePlugin(
            request.Params.Id,
            Protocol.AppServer.AppServerMethodNames.PluginConfigMutate);
        var operations = request.Params.Operations.Select(operation => new PluginConfigMutation(
            operation.Op,
            operation.Key,
            operation.Value.IsSet ? operation.Value.Value : null)).ToArray();
        var quiesceDotNet = manifest.Channel == null && manifest.Dotnet != null;
        if (quiesceDotNet && dotnetRuntime == null)
        {
            throw AppServerErrors.PluginConfiguration(
                PluginConfigStore.WriteFailed,
                "The .NET extension runtime coordinator is unavailable; no configuration was written.");
        }
        var commitToken = EnterMutationCommit(cancellationToken);
        PluginRuntimeMutationResult? quiesce = null;
        if (quiesceDotNet && dotnetRuntime != null)
        {
            quiesce = await dotnetRuntime.QuiesceForMutationAsync(manifest.Id, commitToken)
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
            snapshot = manifest.Channel is { } channel
                ? channelPlugins!.MutateConfig(channel, request.Params.Scope, operations)
                : pluginConfigStore!.Mutate(manifest, request.Params.Scope, operations);
        }
        catch (PluginConfigException exception)
        {
            if (quiesce != null && dotnetRuntime != null)
            {
                await dotnetRuntime.ReconcileAfterMutationAsync(manifest.Id, commitToken)
                    .ConfigureAwait(false);
            }
            throw AppServerErrors.PluginConfiguration(exception.Code, exception.Message);
        }

        if (quiesce != null && dotnetRuntime != null)
        {
            await dotnetRuntime.ReconcileAfterMutationAsync(manifest.Id, commitToken)
                .ConfigureAwait(false);
        }
        appConfigMonitor?.NotifyChanged(
            Protocol.AppServer.AppServerMethodNames.PluginConfigMutate,
            [ConfigChangeRegions.PluginConfiguration]);
        return AppServerTypedResult<Contract.PluginConfigSnapshot>.FromResult(MapPluginConfigSnapshot(snapshot));
    }

    private PluginManifest RequireConfigurablePlugin(string id, string method)
    {
        if (pluginConfigStore == null && channelPlugins == null)
            throw AppServerErrors.MethodNotFound(method);
        if (string.IsNullOrWhiteSpace(id))
            throw AppServerErrors.InvalidParams("'id' is required.");
        var canonicalId = PluginIds.Canonicalize(id.Trim());
        var plugins = RefreshPluginRuntime().Plugins;
        var plugin = plugins.FirstOrDefault(candidate =>
            candidate.Installed && PluginIds.EqualsCanonical(candidate.Manifest.Id, canonicalId));
        if (plugin == null)
            throw AppServerErrors.InvalidParams($"Plugin '{canonicalId}' was not found or is not installed.");

        if (plugin.Manifest.Channel != null)
        {
            if (connection.IsChannelAdapter || connection.IsAppPrincipalAuthenticated)
                throw AppServerErrors.InvalidRequest("Channel settings require a workspace management connection.");
            if (channelPlugins == null)
                throw AppServerErrors.MethodNotFound(method);
            if (channelPlugins.HasDescriptorConflict(plugins, plugin))
                throw AppServerErrors.PluginConfiguration(
                    PluginConfigStore.ScopeUnavailable, "Channel settings overlap another installed channel's configuration.");
        }
        else
        {
            if (pluginConfigStore == null)
                throw AppServerErrors.MethodNotFound(method);
            if (plugin.Manifest.Settings == null)
                throw AppServerErrors.PluginConfiguration(
                    PluginConfigStore.ConfigurationNotDeclared,
                    $"Plugin '{canonicalId}' does not declare a settings schema.");
        }
        return plugin.Manifest;
    }

    private void AppendPluginConfigDiagnostics(
        PluginDiscoveryResult discovery,
        List<PluginDiagnostic> diagnostics)
    {
        foreach (var plugin in discovery.Plugins.Where(plugin => plugin.Installed))
        {
            if (plugin.Manifest.Channel is { } channel && channelPlugins != null
                && !connection.IsChannelAdapter && !connection.IsAppPrincipalAuthenticated
                && !channelPlugins.HasDescriptorConflict(discovery.Plugins, plugin))
            {
                try
                {
                    _ = channelPlugins.ReadConfig(channel);
                }
                catch (PluginConfigException exception)
                {
                    diagnostics.Add(PluginDiagnostic.Error(exception.Code, exception.Message, plugin.Manifest.Id));
                }
            }
            else if (pluginConfigStore?.GetDiagnostic(plugin.Manifest) is { } diagnostic)
            {
                diagnostics.Add(diagnostic);
            }
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
        WritableScopes = snapshot.WritableScopes,
        ConfiguredSecrets = snapshot.ConfiguredSecrets
    };

    private static Contract.PluginConfigField MapPluginConfigField(PluginSettingsField field) => new()
    {
        Key = field.Key,
        Type = field.Type,
        Title = field.Title is { } title ? DotCraft.Protocol.Optional<string>.FromValue(title) : default,
        Description = field.Description is { } description
            ? DotCraft.Protocol.Optional<string>.FromValue(description)
            : default,
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
