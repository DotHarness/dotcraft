using DotCraft.Oratorio.Api;
using DotCraft.Oratorio.Integrations;
using DotCraft.Oratorio.Realtime;

namespace DotCraft.Oratorio.Hosting;

public static partial class OratorioApplication
{
    private static void MapApplication(WebApplication app)
    {
        app.UseWebSockets();
        app.Use(async (context, next) =>
        {
            if (context.Request.Path.StartsWithSegments(
                    OratorioBoardSurfaceRuntime.SurfacePath,
                    out var remainingPath))
            {
                var surface = context.RequestServices.GetRequiredService<OratorioBoardSurfaceRuntime>();
                if (!surface.Authorize(context.Request.Headers.Authorization.ToString()))
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return;
                }

                context.Items["oratorio.surface-authorized"] = true;
                context.Request.Path = new PathString("/api/v1").Add(remainingPath);
            }

            await next();
        });
        app.UseRouting();
        app.UseCors("DesktopRenderer");
        app.Use(AuthorizeBindingMcpAsync);
        var managedServiceToken = app.Configuration[ServiceTokenKey];
        app.Use((context, next) => AuthorizeManagedServiceApiAsync(context, next, managedServiceToken));
        app.Use(async (context, next) =>
        {
            try
            {
                await next();
            }
            catch (OratorioApiException ex)
            {
                context.Response.StatusCode = ex.StatusCode;
                await context.Response.WriteAsJsonAsync(ex.ToResponse());
            }
        });

        app.MapGet("/health", () => new
        {
            service = "oratorio",
            status = "ok",
            version = typeof(OratorioApplication).Assembly.GetName().Version?.ToString(),
            time = DateTimeOffset.UtcNow
        });

        app.MapOratorioApi();
        app.MapBoardStream();
        app.MapMcp("/dotcraft/bindings/{bindingId}/mcp");
    }

    private static async Task AuthorizeManagedServiceApiAsync(HttpContext context, RequestDelegate next, string? expectedToken)
    {
        if (string.IsNullOrWhiteSpace(expectedToken)
            || !RequiresManagedServiceBearer(context.Request.Path)
            || context.Items.ContainsKey("oratorio.surface-authorized"))
        {
            await next(context);
            return;
        }

        var authorization = context.Request.Headers.Authorization.ToString();
        const string prefix = "Bearer ";
        var authorized = authorization.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            && string.Equals(authorization[prefix.Length..], expectedToken, StringComparison.Ordinal);
        if (authorized)
        {
            await next(context);
            return;
        }

        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await context.Response.WriteAsJsonAsync(new
        {
            error = new
            {
                code = "oratorio.serviceUnauthorized",
                message = "Missing or invalid Oratorio service token."
            }
        });
    }

    private static async Task AuthorizeBindingMcpAsync(HttpContext context, RequestDelegate next)
    {
        if (!context.Request.Path.StartsWithSegments("/dotcraft/bindings", out var remaining) ||
            !remaining.Value!.EndsWith("/mcp", StringComparison.OrdinalIgnoreCase))
        {
            await next(context);
            return;
        }

        var bindingId = context.Request.RouteValues["bindingId"]?.ToString();
        var runtime = context.RequestServices.GetRequiredService<OratorioBindingMcpRuntime>();
        if (string.IsNullOrWhiteSpace(bindingId) ||
            !runtime.TryAuthorize(bindingId, context.Request.Headers.Authorization.ToString(), out var principal))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        context.User = principal;
        await next(context);
    }

    private static bool RequiresManagedServiceBearer(PathString path)
    {
        if (!path.StartsWithSegments("/api/v1"))
            return false;

        return !path.StartsWithSegments("/api/v1/sources/github/webhook")
            && !path.StartsWithSegments("/api/v1/sources/gitlab/webhook");
    }

    private static bool IsDesktopRendererOrigin(string origin)
    {
        if (string.Equals(origin, "null", StringComparison.OrdinalIgnoreCase) ||
            origin.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri))
        {
            return false;
        }

        if (uri.Scheme is not ("http" or "https"))
        {
            return false;
        }

        return uri.IsLoopback && uri.Port is 5173 or 5174 or 5177;
    }
}