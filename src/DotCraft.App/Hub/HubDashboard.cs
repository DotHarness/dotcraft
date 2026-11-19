using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using DotCraft.AppServer;
using DotCraft.DashBoard;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;

namespace DotCraft.Hub;

internal static class HubDashboard
{
    private const string Root = "/dashboard";
    private const string WorkspacePrefix = ManagedAppServerEnvironment.DashboardWorkspacePrefix;
    private static readonly HttpClient Upstream = new(new SocketsHttpHandler
    {
        UseProxy = false,
        UseCookies = false,
        AllowAutoRedirect = false
    });

    internal static bool IsLoopback(string url) => Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.IsLoopback;

    internal static string WorkspacePath(string id) => $"{WorkspacePrefix}/{id}/";

    internal static string WorkspaceId(string canonicalPath)
    {
        var key = OperatingSystem.IsWindows() ? canonicalPath.ToUpperInvariant() : canonicalPath;
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key)).AsSpan(0, 16));
    }

    internal static void Map(
        WebApplication app, string apiBaseUrl, ProjectRegistry projects, ManagedAppServerRegistry registry, string chatPath)
    {
        if (!IsLoopback(apiBaseUrl))
            return;

        var port = new Uri(apiBaseUrl).Port;
        var chat = ManagedAppServerRegistry.CanonicalizeWorkspacePath(chatPath);

        app.Use(async (context, next) =>
        {
            if (!context.Request.Path.StartsWithSegments(Root))
            {
                await next(context);
                return;
            }

            var request = context.Request;
            var requestOrigin = $"{request.Scheme}://{request.Host}";
            var origin = request.Headers.Origin;
            var fetchSite = request.Headers["Sec-Fetch-Site"].ToString();
            if (context.Connection.RemoteIpAddress is not { } remote || !IPAddress.IsLoopback(remote)
                || !Uri.TryCreate(requestOrigin, UriKind.Absolute, out var local) || !local.IsLoopback || local.Port != port
                || (origin.Count != 0 && (origin.Count != 1 || !string.Equals(origin[0], requestOrigin, StringComparison.OrdinalIgnoreCase)))
                || (!HttpMethods.IsGet(request.Method) && fetchSite is not ("" or "same-origin" or "none")))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }

            context.Response.Headers.CacheControl = "no-store";
            await next(context);
        });

        app.MapDashBoardFrontend(WorkspacePrefix + "/{id}/");
        app.MapGet(Root + "/api/workspaces", () => Results.Json(new
        {
            workspaces = WorkspacePaths().Select(path => new
            {
                id = WorkspaceId(path),
                name = ManagedAppServerRegistry.WorkspaceComparer.Equals(path, chat) ? "Chat" : Path.GetFileName(path),
                state = registry.GetDashboardWorkspace(path).State
            })
        }, HubJson.Options));
        app.MapMethods(WorkspacePrefix + "/{id}/{**resource}",
            [HttpMethods.Get, HttpMethods.Post, HttpMethods.Delete],
            async (HttpContext context, string id, string? resource) =>
            {
                if (!TryGetUpstreamPath(resource, context.Request.Method, out var upstreamPath))
                {
                    await WriteErrorAsync(context, StatusCodes.Status404NotFound, "dashboardRouteNotFound");
                    return;
                }

                var path = WorkspacePaths().FirstOrDefault(path => WorkspaceId(path) == id);
                if (path is null)
                {
                    await WriteErrorAsync(context, StatusCodes.Status404NotFound, "dashboardWorkspaceNotFound");
                    return;
                }

                if (registry.GetDashboardWorkspace(path).Target is not { } target)
                {
                    await WriteErrorAsync(context, StatusCodes.Status503ServiceUnavailable, "dashboardWorkspaceUnavailable");
                    return;
                }

                await ForwardAsync(context, target, upstreamPath);
            });

        IEnumerable<string> WorkspacePaths() =>
            projects.List().Select(project => project.Path)
                .Concat(Directory.Exists(chat) ? [chat] : [])
                .Concat(registry.DashboardWorkspacePaths())
                .Distinct(ManagedAppServerRegistry.WorkspaceComparer);
    }

    private static bool TryGetUpstreamPath(string? resource, string method, out string path)
    {
        path = "";
        if (resource == "login" && (HttpMethods.IsGet(method) || HttpMethods.IsPost(method))
            || resource == "logout" && HttpMethods.IsPost(method))
        {
            path = Root + "/" + resource;
            return true;
        }
        if (resource is null || !resource.StartsWith("api/", StringComparison.Ordinal))
            return false;

        var segments = resource.Split('/');
        if (segments.Any(segment => segment is "" or "." or ".." || segment.Any(char.IsControl)))
            return false;
        path = Root + "/" + string.Join("/", segments.Select(Uri.EscapeDataString));
        return true;
    }

    private static async Task ForwardAsync(HttpContext context, ManagedAppServerRegistry.DashboardTarget target, string path)
    {
        var ct = context.RequestAborted;
        var request = context.Request;
        using var message = new HttpRequestMessage(new HttpMethod(request.Method),
            new Uri(target.Origin, path + request.QueryString.ToUriComponent()));
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", target.Token);
        if (request.Cookies.TryGetValue(DashBoardAuth.WorkspaceCookieName, out var cookie))
            message.Headers.TryAddWithoutValidation("Cookie",
                $"{DashBoardAuth.WorkspaceCookieName}={Uri.EscapeDataString(cookie)}");
        if (context.Features.Get<IHttpRequestBodyDetectionFeature>()?.CanHaveBody == true)
        {
            message.Content = new StreamContent(request.Body);
            if (request.ContentType is { } contentType)
                message.Content.Headers.TryAddWithoutValidation("Content-Type", contentType);
            message.Content.Headers.ContentLength = request.ContentLength;
        }

        try
        {
            using var upstream = await Upstream.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, ct);
            var response = context.Response;
            response.StatusCode = (int)upstream.StatusCode;
            response.ContentType = upstream.Content.Headers.ContentType?.ToString();
            if (upstream.Headers.Location is { } location)
                response.Headers.Location = location.OriginalString;
            if (upstream.Headers.TryGetValues("Set-Cookie", out var setCookies))
                response.Headers.SetCookie = setCookies.ToArray();

            if (upstream.Content.Headers.ContentType?.MediaType == "text/event-stream")
                await response.StartAsync(ct);
            await upstream.Content.CopyToAsync(response.Body, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception ex) when (ex is IOException or HttpRequestException or OperationCanceledException)
        {
            if (context.Response.HasStarted)
                context.Abort();
            else
            {
                context.Response.Clear();
                context.Response.Headers.CacheControl = "no-store";
                await WriteErrorAsync(context, StatusCodes.Status503ServiceUnavailable, "dashboardWorkspaceUnavailable");
            }
        }
    }

    private static Task WriteErrorAsync(HttpContext context, int status, string code)
    {
        context.Response.StatusCode = status;
        return context.Response.WriteAsJsonAsync(new { error = code }, HubJson.Options, context.RequestAborted);
    }
}
