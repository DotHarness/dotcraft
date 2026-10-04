using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace DotCraft.Hub;

internal static class HubProjectsApi
{
    public static void Map(
        IEndpointRouteBuilder app,
        ProjectRegistry projects,
        ManagedAppServerRegistry appServers,
        HubEventBus events,
        Func<HttpRequest, IResult?> unauthorized,
        Func<Func<Task<IResult>>, Task<IResult>> protectedAsync)
    {
        app.MapGet("/v1/projects", async (HttpRequest request) =>
            unauthorized(request)
            ?? await protectedAsync(() =>
            {
                var running = appServers.RunningWorkspaces();
                return Task.FromResult(Results.Json(
                    new HubProjectListResponse([.. projects.List().Select(project => ToResponse(project, running))]),
                    HubJson.Options));
            }));

        app.MapPost("/v1/projects/open", async (HttpRequest request, ProjectPathRequest body) =>
            unauthorized(request)
            ?? await protectedAsync(() =>
            {
                var project = projects.Open(body.Path);
                events.Publish("projects.changed");
                return Task.FromResult(Results.Json(ToResponse(project, appServers.RunningWorkspaces()), HubJson.Options));
            }));

        app.MapPost("/v1/projects/remove", async (HttpRequest request, ProjectPathRequest body) =>
            unauthorized(request)
            ?? await protectedAsync(() =>
            {
                projects.Remove(body.Path);
                events.Publish("projects.changed");
                return Task.FromResult(Results.Json(new { removed = true }, HubJson.Options));
            }));
    }

    private static HubProjectResponse ToResponse(ProjectRecord project, IReadOnlySet<string> running) => new(
        project.Path,
        Path.GetFileName(project.Path),
        project.AddedAt,
        project.LastOpenedAt,
        running.Contains(project.Path));
}
