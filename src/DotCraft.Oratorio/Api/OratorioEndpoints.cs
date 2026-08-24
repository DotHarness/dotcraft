using DotCraft.Oratorio.Services;
using DotCraft.Oratorio.GitLab;
using DotCraft.Oratorio.GitHub;
using DotCraft.Oratorio.Integrations;
using DotCraft.Oratorio.Domain;
using DotCraft.Oratorio.Sources;
using System.Text.Json;

namespace DotCraft.Oratorio.Api;

public static class OratorioEndpoints
{
    public static RouteGroupBuilder MapOratorioApi(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/v1");

        group.MapGet("/status", (WorkspaceInventoryService workspaces) => new
        {
            name = "Oratorio",
            mode = "local-domain-api",
            workspaceMode = workspaces.GetWorkspaceMode(),
            capabilities = new
            {
                localDomainApi = true,
                webSocketUpdates = true,
                pollingLiveUpdates = true,
                serverSentEvents = false,
                gitHubReadSync = true,
                gitHubWebhooks = true,
                gitHubWrites = true,
                gitLabReadSync = true,
                gitLabWebhooks = true,
                gitLabWrites = true,
                dotCraftAppServerBridge = true,
                appServerDrawerRelay = true,
                gitHubAppIntegration = true,
                managedWorktrees = true,
                concurrencyLeases = true,
                multiWorkspaceRouting = true
            }
        });

        group.MapGet("/items", async (
            string? state,
            string? source,
            string? kind,
            string? repository,
            string? assignee,
            string? q,
            string? sort,
            bool? includeArchived,
            int? limit,
            string? cursor,
            OratorioService service,
            CancellationToken ct) =>
        {
            var result = await service.ListItemsAsync(state, source, kind, repository, assignee, q, sort, includeArchived, limit, cursor, ct);
            return Results.Ok(result);
        });

        group.MapGet("/tasks", async (
            string? state,
            string? source,
            string? kind,
            string? repository,
            string? assignee,
            string? q,
            string? sort,
            bool? includeArchived,
            int? limit,
            string? cursor,
            OratorioService service,
            CancellationToken ct) =>
        {
            var result = await service.ListItemsAsync(state, source, kind, repository, assignee, q, sort, includeArchived, limit, cursor, ct);
            return Results.Ok(new TaskListResponse(result.Items, result.NextCursor));
        });

        group.MapGet("/tasks/{taskId}", async (string taskId, OratorioService service, CancellationToken ct) =>
        {
            var result = await service.GetTaskDetailAsync(taskId, ct);
            return Results.Ok(result);
        });

        group.MapPost("/tasks/reorder", async (TaskReorderRequest request, OratorioService service, CancellationToken ct) =>
        {
            var result = await service.ReorderTasksAsync(request, ct);
            return Results.Ok(result);
        });

        group.MapPost("/items", async (CreateItemRequest request, OratorioService service, CancellationToken ct) =>
        {
            var result = await service.CreateItemAsync(request, ct);
            return Results.Created($"/api/v1/items/{Uri.EscapeDataString(result.Item.Source)}/{Uri.EscapeDataString(result.Item.ExternalId)}", result);
        });

        group.MapPost("/local-tasks", async (CreateLocalTaskRequest request, OratorioService service, CancellationToken ct) =>
        {
            var result = await service.CreateLocalTaskAsync(request, ct);
            return Results.Created($"/api/v1/items/id/{Uri.EscapeDataString(result.Item.ItemId)}", result);
        });

        group.MapGet("/sources/github/status", async (GitHubSourceService service, CancellationToken ct) =>
        {
            var result = await service.GetStatusAsync(ct);
            return Results.Ok(result);
        });

        group.MapGet("/sources/gitlab/status", async (SourceProviderService service, CancellationToken ct) =>
        {
            var result = await service.GetStatusAsync("gitlab", ct);
            return Results.Ok(result);
        });

        group.MapGet("/sources", async (SourceProviderService service, CancellationToken ct) =>
        {
            var result = await service.GetSourcesAsync(ct);
            return Results.Ok(result);
        });

        group.MapGet("/sources/sync-schedules", async (SourceSyncSchedulerService service, CancellationToken ct) =>
        {
            var result = await service.GetSchedulesAsync(ct);
            return Results.Ok(result);
        });

        group.MapGet("/sources/{provider}/sync-schedule", async (
            string provider,
            SourceSyncSchedulerService service,
            CancellationToken ct) =>
        {
            var result = await service.GetScheduleAsync(provider, ct);
            return Results.Ok(result);
        });

        group.MapPut("/sources/{provider}/sync-schedule", async (
            string provider,
            SourceSyncScheduleUpdateRequest request,
            SourceSyncSchedulerService service,
            CancellationToken ct) =>
        {
            var result = await service.UpdateScheduleAsync(provider, request, ct);
            return Results.Ok(result);
        });

        group.MapGet("/sources/status/{provider}", async (string provider, SourceProviderService service, CancellationToken ct) =>
        {
            var result = await service.GetStatusAsync(provider, ct);
            return Results.Ok(result);
        });

        group.MapGet("/sources/{provider}/status", async (string provider, SourceProviderService service, CancellationToken ct) =>
        {
            var result = await service.GetStatusAsync(provider, ct);
            return Results.Ok(result);
        });

        group.MapPost("/sources/sync-jobs", async (
            SourceSyncJobRequest request,
            SourceProviderService service,
            CancellationToken ct) =>
        {
            var result = await service.EnqueueSyncJobAsync(request, ct);
            return Results.Ok(result);
        });

        group.MapPost("/sources/{provider}/sync-jobs", async (
            string provider,
            SourceSyncJobRequest request,
            SourceProviderService service,
            CancellationToken ct) =>
        {
            var result = await service.EnqueueSyncJobAsync(request with { Provider = provider }, ct);
            return Results.Ok(result);
        });

        group.MapGet("/sources/sync-jobs/active", async (
            string? provider,
            SourceProviderService service,
            CancellationToken ct) =>
        {
            var result = await service.GetActiveSyncJobAsync(provider ?? "github", ct);
            return Results.Ok(result);
        });

        group.MapGet("/sources/sync-jobs/{jobId}", async (
            string jobId,
            string? provider,
            SourceProviderService service,
            CancellationToken ct) =>
        {
            var result = await service.GetSyncJobAsync(provider ?? "github", jobId, ct);
            return result is null ? Results.NotFound() : Results.Ok(result);
        });

        group.MapPost("/sources/github/sync", async (GitHubSyncCoordinator coordinator, CancellationToken ct) =>
        {
            var job = await coordinator.EnqueueAsync(GitHubSyncTrigger.Manual, GitHubSyncMode.Incremental, null, ct);
            var completed = await coordinator.WaitForCompletionAsync(job.JobId, ct);
            return Results.Ok(GitHubSyncCoordinator.ToSyncResponse(completed));
        });

        group.MapPost("/sources/github/sync-jobs", async (
            GitHubSyncJobRequest request,
            GitHubSyncCoordinator coordinator,
            CancellationToken ct) =>
        {
            var result = await coordinator.EnqueueAsync(
                GitHubSyncTrigger.Manual,
                request.Mode ?? GitHubSyncMode.Incremental,
                request.Repositories,
                ct);
            return Results.Ok(result);
        });

        group.MapGet("/sources/github/sync-jobs/active", async (GitHubSyncCoordinator coordinator, CancellationToken ct) =>
        {
            var result = await coordinator.GetActiveJobAsync(ct);
            return Results.Ok(result);
        });

        group.MapGet("/sources/github/sync-jobs/{jobId}", async (string jobId, GitHubSyncCoordinator coordinator, CancellationToken ct) =>
        {
            var result = await coordinator.GetJobAsync(jobId, ct);
            return result is null ? Results.NotFound() : Results.Ok(result);
        });

        group.MapPost("/sources/github/webhook", async (HttpRequest request, GitHubSourceService service, CancellationToken ct) =>
            await service.HandleWebhookAsync(request, ct));

        group.MapPost("/sources/gitlab/webhook", async (HttpRequest request, GitLabSourceService service, CancellationToken ct) =>
            await service.HandleWebhookAsync(request, ct));

        group.MapPost("/source-writes/{writeId}/retry", async (
            string writeId,
            OratorioService service,
            CancellationToken ct) =>
        {
            var result = await service.RetrySourceWriteAsync(writeId, ct);
            return Results.Ok(result);
        });

        group.MapPatch("/review-drafts/{draftId}", async (
            string draftId,
            ReviewDraftUpdateRequest request,
            ReviewDraftService drafts,
            OratorioService items,
            CancellationToken ct) =>
        {
            var result = await drafts.UpdateAsync(draftId, request, items, ct);
            return Results.Ok(result);
        });

        group.MapPost("/review-drafts/{draftId}/publish", async (
            string draftId,
            ReviewDraftService drafts,
            OratorioService items,
            CancellationToken ct) =>
        {
            var result = await drafts.PublishAsync(draftId, items, ct);
            return Results.Ok(result);
        });

        group.MapPost("/review-drafts/{draftId}/discard", async (
            string draftId,
            ReviewDraftService drafts,
            OratorioService items,
            CancellationToken ct) =>
        {
            var result = await drafts.DiscardAsync(draftId, items, ct);
            return Results.Ok(result);
        });

        group.MapPost("/review-drafts/{draftId}/comments/{commentId}/resolve", async (
            string draftId,
            string commentId,
            ResolveReviewFindingOperatorRequest request,
            ReviewFindingResolutionService resolution,
            OratorioService items,
            CancellationToken ct) =>
        {
            var itemId = await resolution.ResolveByOperatorAsync(draftId, commentId, request.ResolutionKind, request.Note, ct);
            var result = await items.GetItemDetailByIdAsync(itemId, ct);
            return Results.Ok(result);
        });

        group.MapPost("/review-drafts/{draftId}/comments/{commentId}/reopen", async (
            string draftId,
            string commentId,
            ReviewFindingResolutionService resolution,
            OratorioService items,
            CancellationToken ct) =>
        {
            var itemId = await resolution.ReopenByOperatorAsync(draftId, commentId, ct);
            var result = await items.GetItemDetailByIdAsync(itemId, ct);
            return Results.Ok(result);
        });

        group.MapGet("/dotcraft/status", async (DotCraftStatusService service, CancellationToken ct) =>
        {
            var result = await service.GetStatusAsync(ct);
            return Results.Ok(result);
        });

        group.MapPost("/dotcraft/appserver/start", async (DotCraftStatusService service, CancellationToken ct) =>
        {
            var result = await service.StartAppServerAsync(ct);
            return Results.Ok(result);
        });

        group.MapGet("/dotcraft/app-binding/status", async (OratorioAppBindingService service, CancellationToken ct) =>
        {
            var result = await service.GetConnectionStatusAsync(ct);
            return Results.Ok(result);
        });

        group.MapPost("/dotcraft/app-binding/inspect", async (
            DotCraftAppBindingHandoffRequest request,
            OratorioAppBindingService service,
            CancellationToken ct) =>
        {
            var result = await service.InspectAsync(request.Url, ct);
            return Results.Ok(result);
        });

        group.MapPost("/dotcraft/app-binding/approve", async (
            DotCraftAppBindingHandoffRequest request,
            HttpContext httpContext,
            OratorioAppBindingService service,
            CancellationToken ct) =>
        {
            var surfaceBaseUrl = $"{httpContext.Request.Scheme}://{httpContext.Request.Host}";
            var result = await service.ApproveAsync(request.Url, surfaceBaseUrl, ct);
            return Results.Ok(result);
        });

        group.MapGet("/dotcraft/workspaces", async (WorkspaceInventoryService service, CancellationToken ct) =>
        {
            var result = await service.GetWorkspacesAsync(ct);
            return Results.Ok(result);
        });

        group.MapGet("/models", async (IAppServerRunCoordinator coordinator, CancellationToken ct) =>
        {
            var result = await coordinator.GetModelsAsync(null, ct);
            return Results.Ok(result);
        });

        group.MapGet("/settings/diagnostics", async (SettingsDiagnosticsService service, CancellationToken ct) =>
        {
            var result = await service.GetDiagnosticsAsync(ct);
            return Results.Ok(result);
        });

        group.MapPost("/implementation-drafts/{draftId}/deliver", async (
            string draftId,
            OratorioService service,
            CancellationToken ct) =>
        {
            var result = await service.DeliverImplementationDraftAsync(draftId, ct);
            return Results.Ok(result);
        });

        group.MapPatch("/follow-up-drafts/{draftId}", async (
            string draftId,
            FollowUpDraftUpdateRequest request,
            OratorioService service,
            CancellationToken ct) =>
        {
            var result = await service.UpdateFollowUpDraftAsync(draftId, request, ct);
            return Results.Ok(result);
        });

        group.MapPost("/follow-up-drafts/{draftId}/discard", async (
            string draftId,
            OratorioService service,
            CancellationToken ct) =>
        {
            var result = await service.DiscardFollowUpDraftAsync(draftId, ct);
            return Results.Ok(result);
        });

        group.MapPost("/follow-up-drafts/{draftId}/create-local-task", async (
            string draftId,
            OratorioService service,
            CancellationToken ct) =>
        {
            var result = await service.CreateLocalTaskFromFollowUpDraftAsync(draftId, ct);
            return Results.Ok(result);
        });

        group.MapGet("/settings/server-configuration", async (
            HttpContext context,
            ServerConfigurationService service,
            CancellationToken ct) =>
        {
            var result = await service.GetAsync(context, ct);
            return Results.Ok(result);
        });

        group.MapPut("/settings/server-configuration", async (
            HttpContext context,
            JsonElement request,
            ServerConfigurationService service,
            CancellationToken ct) =>
        {
            var result = await service.UpdateAsync(request, context, ct);
            return Results.Ok(result);
        });

        group.MapGet("/settings/server-configuration/changes", async (
            int? limit,
            ServerConfigurationService service,
            CancellationToken ct) =>
        {
            var result = await service.GetChangesAsync(limit, ct);
            return Results.Ok(result);
        });

        group.MapGet("/items/id/{itemId}", async (string itemId, OratorioService service, CancellationToken ct) =>
        {
            var result = await service.GetItemDetailByIdAsync(itemId, ct);
            return Results.Ok(result);
        });

        group.MapPatch("/items/id/{itemId}", async (
            string itemId,
            UpdateItemRequest request,
            OratorioService service,
            CancellationToken ct) =>
        {
            var result = await service.UpdateItemByIdAsync(itemId, request, ct);
            return Results.Ok(result);
        });

        group.MapGet("/items/by-source", async (string source, string externalId, OratorioService service, CancellationToken ct) =>
        {
            var result = await service.GetItemDetailAsync(source, externalId, ct);
            return Results.Ok(result);
        });

        group.MapPost("/items/id/{itemId}/comments", async (
            string itemId,
            CommentRequest request,
            OratorioService service,
            CancellationToken ct) =>
        {
            var result = await service.AddCommentByIdAsync(itemId, request, ct);
            return Results.Ok(result);
        });

        group.MapPost("/items/id/{itemId}/discussion-turns", async (
            string itemId,
            DiscussionTurnRequest request,
            DiscussionTurnService service,
            CancellationToken ct) =>
        {
            var result = await service.CreateByItemIdAsync(itemId, request, ct);
            return Results.Ok(result);
        });

        group.MapPost("/items/id/{itemId}/source-details/sync", async (
            string itemId,
            OratorioService service,
            CancellationToken ct) =>
        {
            var result = await service.SyncSourceDetailsByIdAsync(itemId, ct);
            return Results.Ok(result);
        });

        group.MapPost("/items/id/{itemId}/dispatch", async (
            string itemId,
            DispatchRequest request,
            OratorioService service,
            CancellationToken ct) =>
        {
            var result = await service.DispatchByIdAsync(itemId, request, ct);
            return Results.Ok(result);
        });

        group.MapPost("/items/id/{itemId}/cancel-run", async (
            string itemId,
            CancelRunRequest request,
            OratorioService service,
            CancellationToken ct) =>
        {
            var result = await service.CancelRunByIdAsync(itemId, request, ct);
            return Results.Ok(result);
        });

        group.MapPost("/items/id/{itemId}/rereview", async (
            string itemId,
            OratorioService service,
            CancellationToken ct) =>
        {
            var result = await service.ReReviewPullRequestByIdAsync(itemId, ct);
            return Results.Ok(result);
        });

        group.MapPost("/items/id/{itemId}/approve", async (
            string itemId,
            DecisionRequest request,
            OratorioService service,
            CancellationToken ct) =>
        {
            var result = await service.ApproveByIdAsync(itemId, request, ct);
            return Results.Ok(result);
        });

        group.MapPost("/items/id/{itemId}/request-changes", async (
            string itemId,
            DecisionRequest request,
            OratorioService service,
            CancellationToken ct) =>
        {
            var result = await service.RequestChangesByIdAsync(itemId, request, ct);
            return Results.Ok(result);
        });

        group.MapPost("/items/id/{itemId}/reject", async (
            string itemId,
            DecisionRequest request,
            OratorioService service,
            CancellationToken ct) =>
        {
            var result = await service.RejectByIdAsync(itemId, request, ct);
            return Results.Ok(result);
        });

        group.MapPost("/items/id/{itemId}/reopen", async (
            string itemId,
            DecisionRequest request,
            OratorioService service,
            CancellationToken ct) =>
        {
            var result = await service.ReopenByIdAsync(itemId, request, ct);
            return Results.Ok(result);
        });

        group.MapPost("/items/id/{itemId}/archive", async (
            string itemId,
            OratorioService service,
            CancellationToken ct) =>
        {
            var result = await service.ArchiveByIdAsync(itemId, ct);
            return Results.Ok(result);
        });

        group.MapGet("/items/{source}/{externalId}", async (string source, string externalId, OratorioService service, CancellationToken ct) =>
        {
            var result = await service.GetItemDetailAsync(source, externalId, ct);
            return Results.Ok(result);
        });

        group.MapPost("/items/{source}/{externalId}/comments", async (
            string source,
            string externalId,
            CommentRequest request,
            OratorioService service,
            CancellationToken ct) =>
        {
            var result = await service.AddCommentAsync(source, externalId, request, ct);
            return Results.Ok(result);
        });

        group.MapPost("/items/{source}/{externalId}/dispatch", async (
            string source,
            string externalId,
            DispatchRequest request,
            OratorioService service,
            CancellationToken ct) =>
        {
            var result = await service.DispatchAsync(source, externalId, request, ct);
            return Results.Ok(result);
        });

        group.MapPost("/items/{source}/{externalId}/cancel-run", async (
            string source,
            string externalId,
            CancelRunRequest request,
            OratorioService service,
            CancellationToken ct) =>
        {
            var result = await service.CancelRunAsync(source, externalId, request, ct);
            return Results.Ok(result);
        });

        group.MapPost("/items/{source}/{externalId}/rereview", async (
            string source,
            string externalId,
            OratorioService service,
            CancellationToken ct) =>
        {
            var result = await service.ReReviewPullRequestAsync(source, externalId, ct);
            return Results.Ok(result);
        });

        group.MapPost("/items/{source}/{externalId}/approve", async (
            string source,
            string externalId,
            DecisionRequest request,
            OratorioService service,
            CancellationToken ct) =>
        {
            var result = await service.ApproveAsync(source, externalId, request, ct);
            return Results.Ok(result);
        });

        group.MapPost("/items/{source}/{externalId}/request-changes", async (
            string source,
            string externalId,
            DecisionRequest request,
            OratorioService service,
            CancellationToken ct) =>
        {
            var result = await service.RequestChangesAsync(source, externalId, request, ct);
            return Results.Ok(result);
        });

        group.MapPost("/items/{source}/{externalId}/reject", async (
            string source,
            string externalId,
            DecisionRequest request,
            OratorioService service,
            CancellationToken ct) =>
        {
            var result = await service.RejectAsync(source, externalId, request, ct);
            return Results.Ok(result);
        });

        group.MapPost("/items/{source}/{externalId}/reopen", async (
            string source,
            string externalId,
            DecisionRequest request,
            OratorioService service,
            CancellationToken ct) =>
        {
            var result = await service.ReopenAsync(source, externalId, request, ct);
            return Results.Ok(result);
        });

        group.MapGet("/runs/{runId}", async (string runId, OratorioService service, CancellationToken ct) =>
        {
            var run = await service.GetRunAsync(runId, ct);
            return Results.Ok(new RunResponse(run));
        });

        group.MapGet("/runs/{runId}/snapshot", async (string runId, IAppServerRunCoordinator coordinator, CancellationToken ct) =>
        {
            var result = await coordinator.GetSnapshotAsync(runId, ct);
            return Results.Ok(result);
        });

        group.MapPost("/runs/{runId}/turn", async (
            string runId,
            SubmitTurnRequest request,
            IAppServerRunCoordinator coordinator,
            CancellationToken ct) =>
        {
            var result = await coordinator.SubmitTurnAsync(runId, request.Input, request.ModelId, ct);
            return Results.Ok(result);
        });

        group.MapPost("/runs/{runId}/turn/interrupt", async (string runId, IAppServerRunCoordinator coordinator, CancellationToken ct) =>
        {
            var result = await coordinator.InterruptTurnAsync(runId, ct);
            return Results.Ok(result);
        });

        return group;
    }
}
