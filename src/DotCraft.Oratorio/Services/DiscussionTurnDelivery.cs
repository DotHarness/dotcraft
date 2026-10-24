using System.Text.Json;
using DotCraft.Protocol.AppServer;
using DotCraft.Sdk.Wire;
using Microsoft.EntityFrameworkCore;
using DotCraft.Oratorio.Api;
using DotCraft.Oratorio.Data;
using DotCraft.Oratorio.Domain;
using DotCraft.Oratorio.Integrations;
using DotCraft.Oratorio.Realtime;

namespace DotCraft.Oratorio.Services;

public sealed class DiscussionTurnDelivery(
    IServiceScopeFactory scopeFactory,
    OratorioDbContext db,
    DiscussionTurnService discussionTurns,
    ConversationThreadLocator threads,
    ThreadStateTracker threadState,
    AppServerPromptBuilder promptBuilder,
    ConversationThreadStarter threadStarter,
    DiscussionThreadSessions sessions,
    DiscussionSessionRunner sessionRunner,
    InlineQuestionContext inlineQuestions,
    IAppServerRunCoordinator runCoordinator,
    IDotCraftAppServerClientFactory clientFactory,
    IDotCraftAppServerEndpointResolver endpointResolver,
    IDotCraftAppServerProcessManager processManager,
    IDotCraftWorkspaceResolver workspaceResolver,
    OratorioDynamicToolCatalog dynamicTools,
    IClock clock,
    BoardEventHub boardEvents,
    ILogger<DiscussionTurnDelivery> logger)
{
    private const int MethodNotFound = -32601;
    private static readonly RunStatus[] ActiveRunStatuses = [RunStatus.Queued, RunStatus.Dispatching, RunStatus.Running];
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task DeliverAsync(string discussionTurnId, CancellationToken ct)
    {
        var turn = await db.DiscussionTurns
            .Include(x => x.Item)
            .Include(x => x.QuestionComment)
            .FirstOrDefaultAsync(x => x.DiscussionTurnId == discussionTurnId, ct);
        if (turn?.Item is null || turn.Status != DiscussionTurnStatus.Pending)
        {
            return;
        }

        try
        {
            var question = new DiscussionQuestionInput(
                turn.QuestionComment?.Body ?? "",
                await inlineQuestions.DescribeAsync(turn, ct));
            var activeRun = await db.Runs.AsNoTracking()
                .Where(x => x.ItemId == turn.ItemId && x.RunnerKind == "appServer" && ActiveRunStatuses.Contains(x.Status))
                .OrderByDescending(x => x.StartedAt)
                .FirstOrDefaultAsync(ct);
            if (activeRun is not null)
            {
                if (runCoordinator.FindRun(activeRun.RunId) is { } bound)
                {
                    await DeliverIntoRunAsync(turn, question, activeRun, bound, ct);
                }

                return;
            }

            var thread = await threads.FindDiscussionThreadAsync(turn.ItemId, ct);
            if (thread is not null)
            {
                if (sessions.Find(thread.ThreadId) is not { } session ||
                    !await TryDeliverIntoSessionAsync(turn, question, session, thread, ct))
                {
                    await OpenSessionAsync(turn, question, thread, ct);
                }

                return;
            }

            if (turn.OriginProvider == DiscussionTurnService.GitHubOrigin && turn.Item.Kind == ItemKind.PullRequest)
            {
                await StartThreadAsync(turn, question, ct);
                return;
            }

            await FailAsync(turn, "noCompatibleDiscussionThread", "No conversation thread with the Oratorio discussion reply tool is available.");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (WorktreeException ex)
        {
            await FailAsync(turn, ex.Code, ex.Message);
        }
        catch (DotCraftWorkspaceResolutionException ex)
        {
            await FailAsync(turn, ex.Code, ex.Message);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Agent Discussion Turn {DiscussionTurnId} could not be delivered.", discussionTurnId);
            await FailAsync(turn, "discussionTurnFailed", ex.Message);
        }
    }

    private async Task DeliverIntoRunAsync(
        OratorioDiscussionTurn turn,
        DiscussionQuestionInput question,
        OratorioRun run,
        ActiveRunThread bound,
        CancellationToken ct)
    {
        var state = await threadState.DescribeAsync(turn.Item!, turn.Item!.HeadSha, bound.ThreadId, ct);
        var prompt = DiscussionTurnPrompt.Build(turn, question, state.Section, runActive: true, reviewTarget: null);
        var context = run.PromptContextJson is null ? null : ConversationThreadContext.Reduce(run.PromptContextJson);
        await MarkDeliveringAsync(turn, bound.ThreadId, run.AppServerEndpoint, run.RunId, context, ct);

        string? turnId;
        if (bound.ActiveTurnId is { } activeTurnId)
        {
            try
            {
                await bound.Client.SteerTurnAsync(bound.ThreadId, activeTurnId, Input(prompt), ct);
            }
            catch (Exception ex) when (IsSteerRejection(ex))
            {
                await RevertToPendingAsync(turn, ct);
                return;
            }

            turnId = activeTurnId;
        }
        else
        {
            turnId = await bound.Client.StartTurnAsync(bound.ThreadId, Input(prompt), turn.ModelId, ct);
        }

        await MarkDeliveredAsync(turn, bound.ThreadId, turnId, state.State, bound.ActiveTurnId is null ? "runTurn" : "steer", ct);
    }

    public async Task AdoptFromEndedRunAsync(string discussionTurnId, CancellationToken ct)
    {
        var turn = await db.DiscussionTurns
            .Include(x => x.Item)
            .FirstOrDefaultAsync(x => x.DiscussionTurnId == discussionTurnId, ct);
        if (turn?.Item is null || turn.Status != DiscussionTurnStatus.Running || turn.ThreadId is null || turn.AppServerEndpoint is null)
        {
            return;
        }

        if (sessions.Find(turn.ThreadId) is { } existing)
        {
            existing.Waiting[turn.DiscussionTurnId] = clock.UtcNow;
            await ReleaseFromRunAsync(turn, ct);
            return;
        }

        var client = await clientFactory.ConnectAsync(turn.AppServerEndpoint, ct, endpointResolver.ResolveConfiguredToken());
        var started = false;
        try
        {
            var threadId = turn.ThreadId;
            var toolSet = dynamicTools.CreateDiscussionToolSet();
            BindDiscussionTools(client, () => threadId, toolSet.AllowedLocalNames);
            var purpose = turn.ThreadContextJson is null ? RunPurpose.ReviewAnalysis : ConversationThreadContext.Purpose(turn.ThreadContextJson);
            var guidance = AppServerPromptBuilder.BuildThreadRuntimeAdditionalContext(purpose, turn.Item.Kind, turn.Item.Source);
            await client.ResumeThreadAsync(threadId, toolSet.Declarations, guidance, ct);
            await client.SubscribeThreadAsync(threadId, ct);
            var session = new DiscussionThreadSession(client, threadId);
            session.Waiting[turn.DiscussionTurnId] = clock.UtcNow;
            await ReleaseFromRunAsync(turn, ct);
            sessions.Add(session);
            sessionRunner.Start(session, awaitBackgroundWork: true);
            started = true;
        }
        finally
        {
            if (!started)
            {
                await client.DisposeAsync();
            }
        }
    }

    private async Task ReleaseFromRunAsync(OratorioDiscussionTurn turn, CancellationToken ct)
    {
        turn.RunId = null;
        turn.UpdatedAt = clock.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    private async Task<bool> TryDeliverIntoSessionAsync(
        OratorioDiscussionTurn turn,
        DiscussionQuestionInput question,
        DiscussionThreadSession session,
        ConversationThread thread,
        CancellationToken ct)
    {
        await session.DeliveryGate.WaitAsync(ct);
        try
        {
            if (session.Closed)
            {
                return false;
            }

            var state = await threadState.DescribeAsync(turn.Item!, turn.Item!.HeadSha, session.ThreadId, ct);
            var prompt = DiscussionTurnPrompt.Build(turn, question, state.Section, runActive: false, reviewTarget: null);
            await MarkDeliveringAsync(turn, session.ThreadId, thread.AppServerEndpoint, null, ConversationThreadContext.Reduce(thread.ContextJson), ct);
            session.Waiting[turn.DiscussionTurnId] = clock.UtcNow;

            var activeTurnId = session.ActiveTurnId;
            string? turnId;
            try
            {
                if (activeTurnId is not null)
                {
                    await session.Client.SteerTurnAsync(session.ThreadId, activeTurnId, Input(prompt), ct);
                    turnId = activeTurnId;
                }
                else
                {
                    turnId = await session.Client.StartTurnAsync(session.ThreadId, Input(prompt), turn.ModelId, ct);
                    session.TurnStarted(turnId);
                }
            }
            catch (Exception ex) when (activeTurnId is not null && IsSteerRejection(ex))
            {
                session.Waiting.TryRemove(turn.DiscussionTurnId, out _);
                await RevertToPendingAsync(turn, ct);
                return true;
            }
            catch
            {
                session.Waiting.TryRemove(turn.DiscussionTurnId, out _);
                throw;
            }

            await MarkDeliveredAsync(turn, session.ThreadId, turnId, state.State, activeTurnId is null ? "turn" : "steer", ct);
            return true;
        }
        finally
        {
            session.DeliveryGate.Release();
        }
    }

    private async Task OpenSessionAsync(OratorioDiscussionTurn turn, DiscussionQuestionInput question, ConversationThread thread, CancellationToken ct)
    {
        var client = await clientFactory.ConnectAsync(thread.AppServerEndpoint, ct, endpointResolver.ResolveConfiguredToken());
        var started = false;
        try
        {
            if (!client.SupportsDynamicToolRebind)
            {
                await FailAsync(turn, "dynamicToolRebindUnsupported", "The AppServer cannot rebind Oratorio discussion tools for this thread.");
                return;
            }

            if (!client.SupportsRuntimeAdditionalContext)
            {
                await FailAsync(turn, "runtimeAdditionalContextUnsupported", "The DotCraft AppServer does not support runtime additional context required by Oratorio discussion turns.");
                return;
            }

            if (turn.Item!.Kind == ItemKind.PullRequest &&
                ConversationThreadContext.Purpose(thread.ContextJson) == RunPurpose.ReviewAnalysis &&
                ConversationThreadContext.UsesManagedWorktree(thread.ContextJson, out var baseWorkspacePath))
            {
                await PrepareWorkspaceAsync(turn, baseWorkspacePath, ct);
            }

            var toolSet = dynamicTools.CreateDiscussionToolSet();
            BindDiscussionTools(client, () => thread.ThreadId, toolSet.AllowedLocalNames);
            var guidance = AppServerPromptBuilder.BuildThreadRuntimeAdditionalContext(
                ConversationThreadContext.Purpose(thread.ContextJson),
                turn.Item!.Kind,
                turn.Item.Source);
            await client.ResumeThreadAsync(thread.ThreadId, toolSet.Declarations, guidance, ct);
            await client.SubscribeThreadAsync(thread.ThreadId, ct);
            await BeginSessionAsync(
                new DiscussionThreadSession(client, thread.ThreadId),
                turn,
                question,
                thread.AppServerEndpoint,
                ConversationThreadContext.Reduce(thread.ContextJson),
                reviewTarget: null,
                ct);
            started = true;
        }
        finally
        {
            if (!started)
            {
                await client.DisposeAsync();
            }
        }
    }

    private async Task StartThreadAsync(OratorioDiscussionTurn turn, DiscussionQuestionInput question, CancellationToken ct)
    {
        var item = turn.Item!;
        var baseWorkspacePath = workspaceResolver.ResolveWorkspacePath(item.Repository);
        var workspace = await PrepareWorkspaceAsync(turn, baseWorkspacePath, ct);
        var endpoint = await processManager.EnsureAvailableAsync(baseWorkspacePath, ct);
        var client = await clientFactory.ConnectAsync(endpoint.Url, ct, endpoint.Token);
        var started = false;
        try
        {
            if (!client.SupportsRuntimeAdditionalContext)
            {
                await FailAsync(turn, "runtimeAdditionalContextUnsupported", "The DotCraft AppServer does not support runtime additional context required by Oratorio.");
                return;
            }

            var tools = dynamicTools.CreateRunToolSet(RunPurpose.ReviewAnalysis, item.Kind, item.Source);
            var guidance = AppServerPromptBuilder.BuildThreadRuntimeAdditionalContext(RunPurpose.ReviewAnalysis, item.Kind, item.Source);
            string? threadId = null;
            BindDiscussionTools(client, () => threadId, dynamicTools.CreateDiscussionToolSet().AllowedLocalNames);
            threadId = await threadStarter.StartThreadAsync(client, item.Title, baseWorkspacePath, workspace.Path, tools, guidance, ct);
            await client.SubscribeThreadAsync(threadId, ct);
            var context = ConversationThreadContext.Create(RunPurpose.ReviewAnalysis, workspace.Path, workspace.BasePath, tools.QualifiedIds);
            var reviewTarget = await promptBuilder.BuildReviewTargetAsync(item, item.HeadSha, workspace, ct);
            await BeginSessionAsync(
                new DiscussionThreadSession(client, threadId),
                turn,
                question,
                endpoint.Url,
                context,
                reviewTarget,
                ct);
            started = true;
        }
        finally
        {
            if (!started)
            {
                await client.DisposeAsync();
            }
        }
    }

    private async Task<ReviewTargetWorkspace> PrepareWorkspaceAsync(OratorioDiscussionTurn turn, string baseWorkspacePath, CancellationToken ct)
    {
        turn.BaseWorkspacePath = baseWorkspacePath;
        if (!threadStarter.UsesManagedWorktrees)
        {
            await db.SaveChangesAsync(ct);
            return new ReviewTargetWorkspace(baseWorkspacePath, baseWorkspacePath, null, null, null);
        }

        turn.WorktreeStatus = WorktreeStatus.Preparing;
        await db.SaveChangesAsync(ct);
        WorktreePrepareResult result;
        try
        {
            result = await threadStarter.PrepareWorktreeAsync(
                ConversationThreadStarter.CreateWorktreeRequest(turn.DiscussionTurnId, turn.Item!, turn.Item!.HeadSha, baseWorkspacePath),
                ct);
        }
        catch (WorktreeException)
        {
            turn.WorktreeStatus = WorktreeStatus.Failed;
            await db.SaveChangesAsync(CancellationToken.None);
            throw;
        }

        turn.BaseWorkspacePath = result.BaseWorkspacePath;
        turn.WorktreePath = result.WorktreePath;
        turn.WorktreeStatus = WorktreeStatus.Ready;
        await db.SaveChangesAsync(ct);
        return new ReviewTargetWorkspace(result.WorktreePath, result.BaseWorkspacePath, result.WorktreeBranch, result.BaseRef, result.BaseSha);
    }

    private async Task BeginSessionAsync(
        DiscussionThreadSession session,
        OratorioDiscussionTurn turn,
        DiscussionQuestionInput question,
        string endpoint,
        string threadContextJson,
        string? reviewTarget,
        CancellationToken ct)
    {
        var state = await threadState.DescribeAsync(turn.Item!, turn.Item!.HeadSha, reviewTarget is null ? session.ThreadId : null, ct);
        var prompt = DiscussionTurnPrompt.Build(turn, question, state.Section, runActive: false, reviewTarget);
        await MarkDeliveringAsync(turn, session.ThreadId, endpoint, null, threadContextJson, ct);
        session.Waiting[turn.DiscussionTurnId] = clock.UtcNow;
        var turnId = await session.Client.StartTurnAsync(session.ThreadId, Input(prompt), turn.ModelId, ct);
        session.TurnStarted(turnId);
        await MarkDeliveredAsync(turn, session.ThreadId, turnId, state.State, reviewTarget is null ? "turn" : "thread", ct);
        sessions.Add(session);
        sessionRunner.Start(session);
    }

    private void BindDiscussionTools(IDotCraftAppServerClient client, Func<string?> threadId, IReadOnlySet<string> allowedLocalNames)
    {
        client.SetDynamicToolHandler(async (call, handlerCt) =>
        {
            using var scope = scopeFactory.CreateScope();
            var turnId = await ResolveCallDiscussionTurnAsync(scope.ServiceProvider.GetRequiredService<OratorioDbContext>(), call, handlerCt);
            return await dynamicTools.InvokeAsync(
                call,
                new OratorioToolInvocationContext(
                    scope.ServiceProvider,
                    call,
                    OratorioToolSurface.Discussion,
                    ExpectedThreadId: threadId(),
                    ExpectedDiscussionTurnId: turnId),
                allowedLocalNames,
                handlerCt);
        });
    }

    private static async Task<string?> ResolveCallDiscussionTurnAsync(OratorioDbContext db, DynamicToolCallParams call, CancellationToken ct)
    {
        var running = await db.DiscussionTurns.AsNoTracking()
            .Where(x => x.ThreadId == call.ThreadId && x.Status == DiscussionTurnStatus.Running)
            .OrderBy(x => x.StartedAt)
            .Select(x => new { x.DiscussionTurnId, x.TurnId })
            .ToListAsync(ct);
        return (running.FirstOrDefault(x => x.TurnId == call.TurnId) ?? running.FirstOrDefault())?.DiscussionTurnId;
    }

    private async Task MarkDeliveringAsync(
        OratorioDiscussionTurn turn,
        string threadId,
        string? endpoint,
        string? runId,
        string? threadContextJson,
        CancellationToken ct)
    {
        var now = clock.UtcNow;
        turn.Status = DiscussionTurnStatus.Running;
        turn.StartedAt = now;
        turn.UpdatedAt = now;
        turn.ThreadId = threadId;
        turn.AppServerEndpoint = endpoint;
        turn.RunId = runId;
        turn.ThreadContextJson = threadContextJson;
        turn.ErrorCode = null;
        turn.ErrorMessage = null;
        await db.SaveChangesAsync(ct);
    }

    private async Task MarkDeliveredAsync(
        OratorioDiscussionTurn turn,
        string threadId,
        string? turnId,
        ItemThreadState state,
        string delivery,
        CancellationToken ct)
    {
        await threadState.RecordAsync(threadId, state, ct);
        if (string.IsNullOrWhiteSpace(turn.TurnId))
        {
            turn.TurnId = turnId;
        }

        turn.PromptContextJson = JsonSerializer.Serialize(new
        {
            promptMode = "discussion",
            runtimeContextVersion = AppServerPromptBuilder.RuntimeContextVersion,
            delivery,
            turn.RunId,
            threadId,
            turnId,
            origin = turn.OriginProvider,
            openFindings = state.OpenFindings
        }, JsonOptions);
        turn.UpdatedAt = clock.UtcNow;
        await db.SaveChangesAsync(ct);
        boardEvents.PublishTaskUpdated(turn.Item!, turn.UpdatedAt);
    }

    private async Task RevertToPendingAsync(OratorioDiscussionTurn turn, CancellationToken ct)
    {
        turn.Status = DiscussionTurnStatus.Pending;
        turn.StartedAt = null;
        turn.RunId = null;
        turn.UpdatedAt = clock.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    private Task FailAsync(OratorioDiscussionTurn turn, string code, string message) =>
        discussionTurns.FailAsync(turn.DiscussionTurnId, code, message, CancellationToken.None);

    private static bool IsSteerRejection(Exception ex) =>
        ex is JsonRpcException { RpcCode: not MethodNotFound } or InvalidOperationException;

    private static IReadOnlyList<TurnInputPartDto> Input(string prompt) =>
        [new TurnInputPartDto("text", prompt, null, null, null, null, null, null)];
}
