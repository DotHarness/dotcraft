using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;

namespace DotCraft.Hub;

internal sealed class MobileProjects(ProjectRegistry projects, ManagedAppServerRegistry appServers, HubPaths paths)
{
    private const string ChatsDisplayName = "Chats";

    public static string ProjectId(string workspacePath)
    {
        var normalized = Path.TrimEndingDirectorySeparator(Path.GetFullPath(workspacePath));
        if (OperatingSystem.IsWindows())
            normalized = normalized.ToUpperInvariant();
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)))[..24];
    }

    public IReadOnlyList<MobileProject> List() => [.. Known().Select(project => project.Project)];

    public KnownProject? Find(string projectId) =>
        Known().FirstOrDefault(project => string.Equals(project.Project.ProjectId, projectId, StringComparison.Ordinal));

    public async Task<MobileProject> EnsureAsync(string projectId, CancellationToken cancellationToken)
    {
        var project = Find(projectId) ?? throw ProjectNotFound();
        try
        {
            if (project.IsChats)
                DefaultChatWorkspace.Ensure(paths);
            await appServers.EnsureAsync(
                new EnsureAppServerRequest
                {
                    WorkspacePath = project.WorkspacePath,
                    Client = new HubClientInfo { Name = "dotcraft-mobile" }
                },
                cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new HubProtocolException(
                "appServerStartFailed",
                "The project could not start on this computer.",
                StatusCodes.Status500InternalServerError);
        }

        return (Find(projectId) ?? project).Project;
    }

    public Uri? AppServerEndpoint(KnownProject project)
    {
        try
        {
            var appServer = appServers.GetByWorkspace(project.WorkspacePath);
            return appServer.State == HubAppServerStates.Running
                   && appServer.Endpoints.TryGetValue("appServerWebSocket", out var url)
                   && Uri.TryCreate(url, UriKind.Absolute, out var endpoint)
                ? endpoint
                : null;
        }
        catch (HubProtocolException)
        {
            return null;
        }
    }

    private IEnumerable<KnownProject> Known()
    {
        var running = appServers.RunningWorkspaces();
        var chats = ManagedAppServerRegistry.CanonicalizeWorkspacePath(paths.DefaultChatWorkspacePath);
        foreach (var project in projects.List())
        {
            yield return new KnownProject(
                project.Path,
                false,
                new MobileProject(ProjectId(project.Path), Path.GetFileName(project.Path), running.Contains(project.Path), project.LastOpenedAt));
        }

        yield return new KnownProject(chats, true, new MobileProject(ProjectId(chats), ChatsDisplayName, running.Contains(chats), null));
    }

    private static HubProtocolException ProjectNotFound() =>
        new("projectNotFound", "No project on this computer has that id.", StatusCodes.Status404NotFound);
}

internal sealed record KnownProject(string WorkspacePath, bool IsChats, MobileProject Project);
