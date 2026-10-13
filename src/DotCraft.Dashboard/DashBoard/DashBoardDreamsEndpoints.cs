using System.Text.Json;
using DotCraft.AppServer;
using DotCraft.Configuration;
using DotCraft.Dreams;
using DotCraft.Sessions;
using DotCraft.Tracing;
using DotCraft.Workspaces;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DotCraft.DashBoard;

internal static class DashBoardDreamsEndpoints
{
    public static void Map(
        IEndpointRouteBuilder endpoints,
        DotCraftPaths paths,
        DreamsService dreamsService,
        TraceStore traceStore,
        SessionPersistenceService? persistence,
        Func<string, CancellationToken, Task>? deleteThreadAsync,
        IDashBoardSessionHandler? sessionHandler,
        ILogger? logger)
    {
        endpoints.MapGet("/dashboard/api/dreams/status", (HttpContext ctx) =>
            Results.Json(BuildStatus(ctx, paths, dreamsService), DashBoardMiddleware.JsonOptions));

        endpoints.MapGet("/dashboard/api/dreams/runs", () =>
            Results.Json(
                new { runs = dreamsService.ListRuns(includeArchived: true).Select(DreamsContractMapper.ToContract).ToList() },
                DashBoardMiddleware.JsonOptions));

        endpoints.MapGet("/dashboard/api/dreams/runs/{runId}", (string runId) =>
        {
            if (dreamsService.GetRunReview(runId.Trim()) is not { } review)
                return NotFound();

            var result = JsonSerializer.SerializeToNode(DreamsContractMapper.ToContract(review), DashBoardMiddleware.JsonOptions)!.AsObject();
            result["manifest"] = ReadOptionalFile(review.Run.InputManifestPath);
            return Results.Json(result, DashBoardMiddleware.JsonOptions);
        });

        endpoints.MapPost("/dashboard/api/dreams/run", async (HttpContext ctx) =>
        {
            var run = await dreamsService.RequestRunAsync(ctx.RequestAborted).ConfigureAwait(false);
            return Results.Json(new
            {
                run = run == null ? null : DreamsContractMapper.ToContract(run),
                status = BuildStatus(ctx, paths, dreamsService)
            }, DashBoardMiddleware.JsonOptions);
        });

        endpoints.MapPost("/dashboard/api/dreams/runs/{runId}/{action}", async (HttpContext ctx, string runId, string action) =>
        {
            var id = runId.Trim();
            try
            {
                DreamsRunState? state = action.ToLowerInvariant() switch
                {
                    "apply" => dreamsService.ApplyRun(id),
                    "restore" => dreamsService.RestoreRun(id),
                    "discard" => dreamsService.DiscardRun(id),
                    "archive" => dreamsService.ArchiveRun(id),
                    "cancel" => await dreamsService.CancelRunAsync(id, ctx.RequestAborted).ConfigureAwait(false),
                    _ => throw new ArgumentException($"Unknown Dreams action: {action}")
                };
                return state == null
                    ? NotFound()
                    : Results.Json(new { run = DreamsContractMapper.ToContract(state) }, DashBoardMiddleware.JsonOptions);
            }
            catch (DreamsStoreChangedException ex)
            {
                return Error(ex.Message, StatusCodes.Status409Conflict, DreamsConstants.StoreChangedErrorType);
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
            {
                return Error(ex.Message, StatusCodes.Status400BadRequest);
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "Dashboard Dreams action failed for {RunId} / {Action}", runId, action);
                return Error(ex.Message, StatusCodes.Status500InternalServerError);
            }
        });

        endpoints.MapDelete("/dashboard/api/dreams/runs/{runId}", async (HttpContext ctx, string runId) =>
        {
            try
            {
                var deleted = dreamsService.DeleteRun(runId.Trim());
                return deleted == null
                    ? NotFound()
                    : await DeletionResultAsync([deleted], ctx.RequestAborted).ConfigureAwait(false);
            }
            catch (InvalidOperationException ex)
            {
                return Error(ex.Message, StatusCodes.Status409Conflict);
            }
        });

        endpoints.MapDelete("/dashboard/api/dreams/runs", async (HttpContext ctx) =>
        {
            try
            {
                var deleted = await dreamsService.DeleteAllRunsAsync(ctx.RequestAborted).ConfigureAwait(false);
                return await DeletionResultAsync(deleted, ctx.RequestAborted).ConfigureAwait(false);
            }
            catch (InvalidOperationException ex)
            {
                return Error(ex.Message, StatusCodes.Status409Conflict);
            }
        });

        async Task<IResult> DeletionResultAsync(IReadOnlyList<DreamsRunState> deleted, CancellationToken cancellationToken)
        {
            var failures = new List<object>();
            foreach (var run in deleted.Where(static run => !string.IsNullOrWhiteSpace(run.ThreadId)))
            {
                if (await DeleteTraceAsync(run.ThreadId!, cancellationToken).ConfigureAwait(false) is { } error)
                    failures.Add(new { runId = run.Id, threadId = run.ThreadId, error });
            }

            return Results.Json(new
            {
                deletedRunIds = deleted.Select(static run => run.Id).ToList(),
                deletedCount = deleted.Count,
                partial = failures.Count > 0,
                traceCleanupFailures = failures
            }, DashBoardMiddleware.JsonOptions);
        }

        async Task<string?> DeleteTraceAsync(string threadId, CancellationToken cancellationToken)
        {
            try
            {
                if (persistence != null)
                {
                    if (!await persistence.DeleteTraceSessionAsync(threadId, deleteThreadAsync, cancellationToken).ConfigureAwait(false))
                    {
                        if (deleteThreadAsync == null)
                            return $"Failed to delete trace for thread '{threadId}'.";
                        await deleteThreadAsync(threadId, cancellationToken).ConfigureAwait(false);
                    }

                    return null;
                }

                _ = traceStore.ClearSession(threadId);
                if (sessionHandler != null)
                {
                    try
                    {
                        await sessionHandler.DeleteThreadAsync(threadId).ConfigureAwait(false);
                    }
                    catch (KeyNotFoundException)
                    {
                    }
                }
                else if (deleteThreadAsync != null)
                {
                    await deleteThreadAsync(threadId, cancellationToken).ConfigureAwait(false);
                }

                return null;
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "Failed to delete Dream trace/thread {ThreadId}", threadId);
                return ex.Message;
            }
        }
    }

    private static object BuildStatus(HttpContext ctx, DotCraftPaths paths, DreamsService dreamsService)
    {
        var status = JsonSerializer.SerializeToNode(
            DreamsContractMapper.ToStatus(dreamsService, ResolveConfig(ctx, paths)),
            DashBoardMiddleware.JsonOptions)!.AsObject();
        status["pendingCount"] = dreamsService.ListRuns().Count(static run => run.ReviewStatus == DreamsReviewStatuses.Pending);
        return status;
    }

    private static DreamsConfig ResolveConfig(HttpContext ctx, DotCraftPaths paths) =>
        ctx.RequestServices.GetService<IAppConfigMonitor>()?.Current.Dreams
        ?? AppConfig.LoadWithGlobalFallback(Path.Combine(paths.Data.RootPath, "config.json")).Dreams
        ?? new DreamsConfig();

    private static IResult NotFound() => Error("Dream run not found.", StatusCodes.Status404NotFound);

    private static IResult Error(string message, int statusCode, string? code = null) =>
        Results.Json(new { error = message, code }, DashBoardMiddleware.JsonOptions, statusCode: statusCode);

    private static string ReadOptionalFile(string? path) =>
        !string.IsNullOrWhiteSpace(path) && File.Exists(path) ? File.ReadAllText(path) : string.Empty;
}
