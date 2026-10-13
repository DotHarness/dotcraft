using DotCraft.Agents;
using DotCraft.Memory;
using DotCraft.Sessions;
using DotCraft.Workspaces;

namespace DotCraft.Dreams;

/// <summary>The consolidatable stores of a workspace: the workspace store and the own store of each Agent Profile that declares one.</summary>
internal sealed class DreamsStoreCatalog(DotCraftPaths paths)
{
    public DreamsStore Workspace => new(null, DirectoryOf(null));

    public IReadOnlyList<DreamsStore> List()
    {
        var scopes = new AgentProfileStore(paths.Data.RootPath, paths.UserData.RootPath)
            .List(includeInvalid: false)
            .Where(static entry => !entry.Shadowed)
            .Select(static entry => entry.CompiledConfiguration?.MemoryScope)
            .OfType<string>()
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal);
        return [Workspace, .. scopes.Select(scope => new DreamsStore(scope, DirectoryOf(scope)))];
    }

    public DreamsStore? Find(string? scope) =>
        scope == null ? Workspace : List().FirstOrDefault(store => store.Scope == scope);

    public string DirectoryOf(string? scope) =>
        Path.GetFullPath(MemoryScopes.StoreDirectory(
            scope == null ? null : new ThreadConfiguration { MemoryScope = scope },
            paths.Data.RootPath));
}
