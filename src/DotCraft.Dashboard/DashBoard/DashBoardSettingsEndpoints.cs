using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Agents;
using DotCraft.Configuration;
using DotCraft.Workspaces;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DotCraft.DashBoard;

internal static class DashBoardSettingsEndpoints
{
    public static void Map(
        IEndpointRouteBuilder endpoints,
        ConfigurationService configuration,
        IReadOnlyList<ConfigSchemaSection> schema,
        DotCraftPaths paths,
        ILogger? logger)
    {
        endpoints.MapGet("/dashboard/api/config/schema", () =>
            Results.Json(schema, DashBoardMiddleware.JsonOptions));

        endpoints.MapGet("/dashboard/api/config/edit", () =>
        {
            var read = configuration.Read(includeLayers: true);
            var global = read.Layers!.Single(layer => layer.Layer.Type == ConfigLayerType.User);
            var workspace = read.Layers!.Single(layer => layer.Layer.Type == ConfigLayerType.Workspace);
            var merged = read.Config;

            var authEnabled = AtomicConfigDocument.Value(merged, "DashBoard") is JsonObject dashBoard
                && AtomicConfigDocument.Value(dashBoard, "Username") is JsonValue username && username.ToString().Length > 0
                && AtomicConfigDocument.Value(dashBoard, "Password") is JsonValue password && password.ToString().Length > 0;

            return Results.Json(new
            {
                global = global.Config,
                workspace = workspace.Config,
                merged,
                globalPath = global.Layer.FilePath,
                workspacePath = workspace.Layer.FilePath,
                authEnabled
            }, DashBoardMiddleware.RawJsonOptions);
        });

        endpoints.MapPost("/dashboard/api/config/workspace", ctx =>
            SaveWorkspaceConfigAsync(ctx, configuration, logger));

        endpoints.MapGet("/dashboard/api/config/models", async (HttpContext ctx) =>
        {
            var workspaceConfigPath = Path.Combine(paths.Data.RootPath, "config.json");
            var config = ctx.RequestServices.GetService<IAppConfigMonitor>()?.Current
                ?? AppConfig.LoadWithGlobalFallback(workspaceConfigPath);
            var providers = ctx.RequestServices.GetRequiredService<ModelProviderRegistry>();
            var result = await ModelProviderCatalog.FetchAsync(
                config,
                providers,
                cancellationToken: ctx.RequestAborted);

            if (!result.Success)
            {
                return Results.Json(new
                {
                    success = false,
                    errorCode = result.ErrorCode.ToString(),
                    errorMessage = result.ErrorMessage,
                    models = Array.Empty<ModelCatalogEntry>()
                }, DashBoardMiddleware.RawJsonOptions, statusCode: MapModelCatalogStatusCode(result.ErrorCode));
            }

            return Results.Json(new
            {
                success = true,
                models = result.Models
            }, DashBoardMiddleware.RawJsonOptions);
        });
    }

    private static async Task SaveWorkspaceConfigAsync(
        HttpContext ctx,
        ConfigurationService configuration,
        ILogger? logger)
    {
        using var reader = new StreamReader(ctx.Request.Body);
        var body = await reader.ReadToEndAsync();

        JsonObject? document;
        try
        {
            document = JsonNode.Parse(body) as JsonObject;
        }
        catch (JsonException)
        {
            document = null;
        }

        if (document == null)
        {
            logger?.LogWarning("Dashboard config save rejected: the body is not a JSON object");
            ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
            await ctx.Response.WriteAsJsonAsync(new { success = false, error = "Invalid JSON" });
            return;
        }

        try
        {
            var result = await configuration.ReplaceLayerAsync(ConfigLayerType.Workspace, document, "dashboard", ctx.RequestAborted);
            logger?.LogInformation("Dashboard config saved to {Path}", result.FilePath);
            await ctx.Response.WriteAsJsonAsync(new { success = true, path = result.FilePath });
        }
        catch (ConfigWriteException ex)
        {
            logger?.LogWarning("Dashboard config save rejected: {Message}", ex.Message);
            ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
            await ctx.Response.WriteAsJsonAsync(new { success = false, error = ex.Message });
        }
    }

    private static int MapModelCatalogStatusCode(ModelCatalogErrorCode code) => code switch
    {
        ModelCatalogErrorCode.MissingApiKey => StatusCodes.Status400BadRequest,
        ModelCatalogErrorCode.InvalidEndpoint => StatusCodes.Status400BadRequest,
        ModelCatalogErrorCode.Unauthorized => StatusCodes.Status401Unauthorized,
        ModelCatalogErrorCode.Forbidden => StatusCodes.Status403Forbidden,
        ModelCatalogErrorCode.EndpointNotSupported => StatusCodes.Status404NotFound,
        ModelCatalogErrorCode.Timeout => StatusCodes.Status504GatewayTimeout,
        ModelCatalogErrorCode.Network => StatusCodes.Status502BadGateway,
        _ => StatusCodes.Status500InternalServerError
    };
}
