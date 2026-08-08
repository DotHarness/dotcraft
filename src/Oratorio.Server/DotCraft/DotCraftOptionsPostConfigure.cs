using Microsoft.Extensions.Options;
using Oratorio.Server.Sources;

namespace Oratorio.Server.DotCraft;

public sealed class DotCraftOptionsPostConfigure : IPostConfigureOptions<DotCraftOptions>
{
    public void PostConfigure(string? name, DotCraftOptions options)
    {
        var workspaceRoutes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var route in options.RepositoryWorkspaceRoutes)
        {
            if (TryNormalizeRoute(route, out var project, out var workspacePath))
            {
                workspaceRoutes[project] = workspacePath;
            }
        }

        options.RepositoryWorkspaceRoutes = workspaceRoutes
            .OrderBy(route => route.Key, StringComparer.OrdinalIgnoreCase)
            .Select(route => new DotCraftRepositoryWorkspaceRoute
            {
                Project = route.Key,
                WorkspacePath = route.Value
            })
            .ToList();
    }

    private static bool TryNormalizeRoute(DotCraftRepositoryWorkspaceRoute route, out string project, out string workspacePath)
    {
        project = "";
        workspacePath = "";
        if (!SourceProjectKey.TryParse(route.Project, out var key) ||
            string.IsNullOrWhiteSpace(route.WorkspacePath))
        {
            return false;
        }

        project = key.Key;
        workspacePath = route.WorkspacePath.Trim();
        return true;
    }
}
