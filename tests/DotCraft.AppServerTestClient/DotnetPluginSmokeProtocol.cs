using System.Text.Json;
using DotCraft.AppServer;

namespace DotCraft.AppServerTestClient;

internal sealed class DotnetPluginSmokeProtocol
{
    private readonly AppServerClient client;
    private long snapshotRevision = -1;

    public DotnetPluginSmokeProtocol(AppServerClient client) => this.client = client;

    public async Task InitializeAsync()
    {
        using var response = await client.InitializeAsync(approvalSupport: true, streamingSupport: true);
        EnsureSuccess(response, "initialize_failed");
        if (!response.RootElement.GetProperty("result")
                .GetProperty("capabilities")
                .TryGetProperty("pluginManagement", out var capability)
            || !capability.GetBoolean())
        {
            throw Failure("initialize", "plugin_management_unavailable");
        }
    }

    public async Task<PluginSnapshot> ListAsync()
    {
        using var response = await client.SendRequestAsync(
            DotCraft.Protocol.AppServer.AppServerMethodNames.PluginList,
            new { includeDisabled = true });
        EnsureSuccess(response, "plugin_list_failed");
        var result = response.RootElement.GetProperty("result");
        var revision = result.GetProperty("snapshotRevision").GetInt64();
        if (revision < snapshotRevision)
            throw Failure("plugin/list", "snapshot_revision_regressed");
        snapshotRevision = Math.Max(snapshotRevision, revision);
        var plugins = result.GetProperty("plugins").EnumerateArray()
            .Select(ParsePlugin)
            .ToDictionary(static plugin => plugin.Id, StringComparer.OrdinalIgnoreCase);
        return new PluginSnapshot(revision, plugins);
    }

    public Task<PluginMutation> InstallLocalAsync(string path, string expectedId) =>
        MutateAsync(
            DotCraft.Protocol.AppServer.AppServerMethodNames.PluginInstallLocal,
            new { path },
            expectedId);

    public Task<PluginMutation> SetTrustedAsync(string id, bool trusted) =>
        MutateAsync(
            DotCraft.Protocol.AppServer.AppServerMethodNames.PluginSetTrusted,
            new { id, trusted },
            id);

    public Task<PluginMutation> SetEnabledAsync(string id, bool enabled) =>
        MutateAsync(
            DotCraft.Protocol.AppServer.AppServerMethodNames.PluginSetEnabled,
            new { id, enabled },
            id);

    public Task<PluginMutation> RemoveAsync(string id) =>
        MutateAsync(
            DotCraft.Protocol.AppServer.AppServerMethodNames.PluginRemove,
            new { id },
            id);

    private async Task<PluginMutation> MutateAsync(string method, object parameters, string expectedId)
    {
        using var response = await client.SendRequestAsync(method, parameters);
        EnsureSuccess(response, "plugin_mutation_failed");
        var result = response.RootElement.GetProperty("result");
        var outcome = result.GetProperty("outcome").GetString() ?? string.Empty;
        if (outcome is not ("applied" or "noChange"))
            throw Failure(method, "plugin_mutation_not_applied");

        var revision = result.GetProperty("snapshotRevision").GetInt64();
        if (revision < snapshotRevision || outcome == "applied" && revision <= snapshotRevision)
            throw Failure(method, "snapshot_revision_not_advanced");
        snapshotRevision = Math.Max(snapshotRevision, revision);

        PluginState? plugin = null;
        if (result.TryGetProperty("plugin", out var pluginElement)
            && pluginElement.ValueKind == JsonValueKind.Object)
        {
            plugin = ParsePlugin(pluginElement);
            if (!string.Equals(plugin.Id, expectedId, StringComparison.OrdinalIgnoreCase))
                throw Failure(method, "plugin_mutation_wrong_plugin");
        }

        var affected = result.GetProperty("affectedPlugins").EnumerateArray()
            .Select(static item => item.GetProperty("id").GetString() ?? string.Empty)
            .Where(static id => id.Length > 0)
            .ToArray();

        if (outcome == "applied")
        {
            using var notification = await client.WaitForNotificationAsync(
                DotCraft.Protocol.AppServer.AppServerMethodNames.PluginSnapshotUpdated,
                TimeSpan.FromSeconds(30))
                ?? throw Failure(method, "plugin_snapshot_notification_missing");
            var notificationParams = notification.RootElement.GetProperty("params");
            ValidateSnapshotNotification(notificationParams, revision, expectedId, affected, method);
        }

        return new PluginMutation(outcome, revision, plugin, affected);
    }

    public static PluginState Require(PluginSnapshot snapshot, string id, string phase)
    {
        if (!snapshot.Plugins.TryGetValue(id, out var plugin))
            throw Failure(phase, "plugin_missing");
        return plugin;
    }

    public static void RequireState(PluginState plugin, string state, string phase)
    {
        if (!string.Equals(plugin.RuntimeState, state, StringComparison.Ordinal))
            throw Failure(phase, $"plugin_state_not_{state}");
    }

    public static void RequireBlockedBy(PluginState plugin, string blockerCode, string phase)
    {
        RequireState(plugin, "blocked", phase);
        if (!plugin.Blockers.Contains(blockerCode, StringComparer.Ordinal))
            throw Failure(phase, "plugin_blocker_missing");
    }

    public static DotnetPluginSmokeException Failure(string phase, string errorCode) =>
        new(phase, errorCode);

    internal static void ValidateSnapshotNotification(
        JsonElement notificationParams,
        long revision,
        string expectedId,
        IReadOnlyList<string> affected,
        string phase)
    {
        if (notificationParams.GetProperty("snapshotRevision").GetInt64() != revision)
            throw Failure(phase, "plugin_snapshot_revision_mismatch");
        var notifiedIds = notificationParams.GetProperty("pluginIds").EnumerateArray()
            .Select(static item => item.GetString())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!notifiedIds.Contains(expectedId) || affected.Any(id => !notifiedIds.Contains(id)))
            throw Failure(phase, "plugin_snapshot_ids_incomplete");
    }

    private static void EnsureSuccess(JsonDocument response, string errorCode)
    {
        if (response.RootElement.TryGetProperty("error", out _))
            throw Failure("appserver", errorCode);
    }

    private static PluginState ParsePlugin(JsonElement plugin)
    {
        var runtimeState = string.Empty;
        var trustStatus = string.Empty;
        var blockers = Array.Empty<string>();
        if (plugin.TryGetProperty("dotnetRuntime", out var runtime)
            && runtime.ValueKind == JsonValueKind.Object)
        {
            runtimeState = runtime.GetProperty("state").GetString() ?? string.Empty;
            trustStatus = runtime.GetProperty("trustStatus").GetString() ?? string.Empty;
            blockers = runtime.GetProperty("blockers").EnumerateArray()
                .Select(static blocker => blocker.GetProperty("code").GetString() ?? string.Empty)
                .Where(static code => code.Length > 0)
                .ToArray();
        }

        return new PluginState(
            plugin.GetProperty("id").GetString() ?? string.Empty,
            plugin.GetProperty("installed").GetBoolean(),
            plugin.GetProperty("enabled").GetBoolean(),
            runtimeState,
            trustStatus,
            blockers);
    }
}

internal sealed record PluginSnapshot(long Revision, IReadOnlyDictionary<string, PluginState> Plugins);

internal sealed record PluginState(
    string Id,
    bool Installed,
    bool Enabled,
    string RuntimeState,
    string TrustStatus,
    IReadOnlyList<string> Blockers);

internal sealed record PluginMutation(
    string Outcome,
    long Revision,
    PluginState? Plugin,
    IReadOnlyList<string> AffectedPluginIds);
