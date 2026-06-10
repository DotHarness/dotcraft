using System.Collections.Concurrent;
using DotCraft.Protocol.AppServer;

namespace DotCraft.AppBinding;

internal sealed class AppBindingStoreAccessor
{
    private readonly ConcurrentDictionary<string, AppBindingStore> _stores = new(StringComparer.OrdinalIgnoreCase);

    public AppBindingStore GetStore(string workspaceCraftPath) =>
        _stores.GetOrAdd(Path.GetFullPath(workspaceCraftPath), path => new AppBindingStore(path));

    public static AppCatalogEntry FindApp(AppCatalogSnapshot catalog, string appId)
    {
        var entry = catalog.Entries.FirstOrDefault(candidate =>
            string.Equals(candidate.Descriptor.AppId, appId, StringComparison.Ordinal));
        if (entry == null)
            throw AppServerErrors.InvalidParams($"App '{appId}' was not found.");
        return entry;
    }

    public static AppCatalogEntry FindEnabledApp(AppCatalogSnapshot catalog, string appId)
    {
        var entry = FindApp(catalog, appId);
        if (!entry.Plugin.Installed || !entry.Plugin.Enabled)
            throw AppServerErrors.InvalidParams($"App '{appId}' requires an installed and enabled plugin.");
        return entry;
    }

    public static AppConnectionRecord? FindConnection(AppBindingStateDocument state, string userId, string appId) =>
        state.Connections.FirstOrDefault(connection =>
            string.Equals(connection.UserId, userId, StringComparison.Ordinal)
            && string.Equals(connection.AppId, appId, StringComparison.Ordinal));

    public static AppBindingRecord? FindBinding(AppBindingStateDocument state, string bindingId) =>
        state.Bindings.FirstOrDefault(binding =>
            string.Equals(binding.BindingId, bindingId, StringComparison.Ordinal));

    public static bool IsConnectionUsable(AppConnectionRecord? connection) =>
        connection is { State: AppConnectionStates.Connected }
        && (connection.ExpiresAt == null || connection.ExpiresAt > DateTimeOffset.UtcNow);
}
