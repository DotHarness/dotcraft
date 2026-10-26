using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Channels;
using DotCraft.Oratorio.Integrations;
using DotCraft.Oratorio.Api;
using DotCraft.Protocol.AppServer;
using DotCraft.Sdk;
using DynamicToolCall = DotCraft.Protocol.AppServer.DynamicToolCallParams;
using DynamicToolResult = DotCraft.Protocol.AppServer.DynamicToolCallResult;
using RuntimeDynamicToolDeclaration = DotCraft.Protocol.AppServer.RuntimeDynamicToolDeclaration;

namespace DotCraft.Oratorio.Tests;

internal sealed partial class FakeAppServerClient(
    FakeAppServerOutcome outcome,
    Func<AppServerThreadStartRequest, string> startThread,
    Action<FakeAppServerThreadResumeRequest> resumeThread,
    Func<string, string> startTurn,
    Action<string> captureTurnPrompt,
    Action<string, string> interruptTurn,
    Func<bool> supportsRuntimeAdditionalContext,
    Func<bool> useMismatchedToolThreadId,
    Func<string?> getFollowUpDraftRepository,
    Func<string?> getFollowUpDraftBranch,
    Action<DynamicToolResult> captureToolResult,
    Func<string, Task> wakeTurnStarted,
    Action<FakeAppServerSteer> captureSteer) : IDotCraftAppServerClient
{
    private readonly TaskCompletionSource<string> _steered = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Channel<DotCraftRunEvent> _notifications = Channel.CreateUnbounded<DotCraftRunEvent>();
    private Func<DynamicToolCall, CancellationToken, Task<DynamicToolResult>>? _dynamicToolHandler;
    private readonly TaskCompletionSource _backgroundWorkObserved = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private FakeAppServerThreadResumeRequest? _lastResumeRequest;
    private bool _interrupted;
    private volatile bool _backgroundWork;
    private int _backgroundWorkChecks;

    public Func<string?> LastSteeredPrompt { get; init; } = () => null;
    public Func<Task> SteeredRunRelease { get; init; } = () => Task.CompletedTask;

    public bool SupportsDynamicToolRebind => true;
    public bool SupportsRuntimeAdditionalContext => supportsRuntimeAdditionalContext();
    public global::DotCraft.Sdk.AppBinding.DotCraftAppBindingClient AppBindings =>
        throw new NotSupportedException("The general AppServer test fake does not exercise App Binding.");

    public void SetDynamicToolHandler(Func<DynamicToolCall, CancellationToken, Task<DynamicToolResult>> handler) =>
        _dynamicToolHandler = handler;

    public Task<string> StartThreadAsync(AppServerThreadStartRequest request, CancellationToken ct)
    {
        var threadId = startThread(request);
        _notifications.Writer.TryWrite(Notification("thread/started", new { threadId }));
        return Task.FromResult(threadId);
    }

    public Task ResumeThreadAsync(
        string threadId,
        IReadOnlyList<RuntimeDynamicToolDeclaration>? dynamicTools,
        IReadOnlyDictionary<string, AppServerRuntimeAdditionalContextEntry>? runtimeAdditionalContext,
        CancellationToken ct)
    {
        _lastResumeRequest = new FakeAppServerThreadResumeRequest(threadId, dynamicTools, runtimeAdditionalContext);
        resumeThread(_lastResumeRequest);
        _notifications.Writer.TryWrite(Notification("thread/resumed", new { threadId }));
        if (outcome == FakeAppServerOutcome.HoldBackgroundWorkOnResume)
        {
            _backgroundWork = true;
        }
        else if (outcome == FakeAppServerOutcome.ReplyOnResumeAfterBackgroundWork)
        {
            _backgroundWork = true;
            _ = Task.Run(async () =>
            {
                await _backgroundWorkObserved.Task;
                _backgroundWork = false;
                const string wakeTurnId = "turn-wake-1";
                _notifications.Writer.TryWrite(Notification("turn/started", new { threadId, turnId = wakeTurnId }));
                await SubmitDiscussionReplyAsync(threadId, wakeTurnId, "call-discussion-1", ExtractDiscussionTurnId(LastSteeredPrompt() ?? "", null));
                _notifications.Writer.TryWrite(Notification("turn/completed", new { threadId, turnId = wakeTurnId }));
            });
        }
        return Task.CompletedTask;
    }

    public Task SubscribeThreadAsync(string threadId, CancellationToken ct) => Task.CompletedTask;

    public Task<string?> StartTurnAsync(string threadId, string prompt, CancellationToken ct)
    {
        captureTurnPrompt(prompt);
        var turnId = startTurn(threadId);
        _notifications.Writer.TryWrite(Notification("turn/started", new { threadId, turnId }));
        if (outcome == FakeAppServerOutcome.Success)
        {
            _notifications.Writer.TryWrite(Notification("item/agentMessage/delta", new { delta = "DotCraft analysis complete. " }));
            _notifications.Writer.TryWrite(Notification("turn/completed", new { threadId, turnId, summary = "DotCraft analysis complete." }));
            _notifications.Writer.TryComplete();
        }
        else if (outcome == FakeAppServerOutcome.DrawerItemPublishFailure)
        {
            _notifications.Writer.TryWrite(DisposedNotification("item/started"));
            _notifications.Writer.TryWrite(Notification("item/agentMessage/delta", new { itemId = "item-after-drawer-failure", threadId, turnId, delta = "DotCraft analysis complete. " }));
            _notifications.Writer.TryWrite(Notification("turn/completed", new { threadId, turnId, summary = "DotCraft analysis complete." }));
            _notifications.Writer.TryComplete();
        }
        else if (outcome is FakeAppServerOutcome.SubmitReviewDraft or FakeAppServerOutcome.SubmitSyntheticBadAnchorReviewDraft or FakeAppServerOutcome.SubmitRetryAnchorReviewDraft or FakeAppServerOutcome.SubmitCleanReviewDraft or FakeAppServerOutcome.SubmitSummaryOnlyReviewDraft or FakeAppServerOutcome.SubmitSummaryOnlyReviewDraftThenDisconnect or FakeAppServerOutcome.SubmitSuggestionTextNotFoundReviewDraft or FakeAppServerOutcome.SubmitAmbiguousSuggestionTextReviewDraft or FakeAppServerOutcome.SubmitMultiLineReviewDraft or FakeAppServerOutcome.SubmitContextLineReviewDraft or FakeAppServerOutcome.SubmitNoOpReviewDraft)
        {
            _ = Task.Run(async () =>
            {
                var isSummaryOnly = outcome is FakeAppServerOutcome.SubmitSummaryOnlyReviewDraft or FakeAppServerOutcome.SubmitSummaryOnlyReviewDraftThenDisconnect;
                if (_dynamicToolHandler is not null)
                {
                    if (outcome == FakeAppServerOutcome.SubmitRetryAnchorReviewDraft)
                    {
                        var toolThreadId = useMismatchedToolThreadId() ? "thread-mismatch" : threadId;
                        var firstArguments = JsonSerializer.SerializeToElement(new
                        {
                            summary = new
                            {
                                majorCount = 1,
                                minorCount = 0,
                                suggestionCount = 0,
                                body = "Verbose review summary from DotCraft."
                            },
                            comments = new object[]
                            {
                                new
                                {
                                    severity = "YELLOW",
                                    title = "Synthetic anchor drift",
                                    body = "This finding is initially anchored to a full-file line that is not commentable in the diff.",
                                    path = "src/Example/SyntheticFileSearcher.cs",
                                    kind = "commentOnly",
                                    line = 129,
                                    side = "right",
                                    reason = "requiresLargerChange"
                                }
                            }
                        }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
                        var firstResult = await _dynamicToolHandler(CreateCall(toolThreadId, turnId, "call-review-1", "oratorio_run", "SubmitReviewDraft", firstArguments), CancellationToken.None);
                        captureToolResult(firstResult);

                        var retryArguments = JsonSerializer.SerializeToElement(new
                        {
                            summary = new
                            {
                                majorCount = 1,
                                minorCount = 0,
                                suggestionCount = 0,
                                body = "Verbose retry review summary from DotCraft."
                            },
                            comments = new object[]
                            {
                                new
                                {
                                    severity = "YELLOW",
                                    title = "Synthetic anchor drift",
                                    body = "This finding was re-anchored to a commentable diff line.",
                                    path = "src/Example/SyntheticFileSearcher.cs",
                                    kind = "commentOnly",
                                    line = 255,
                                    side = "right",
                                    reason = "requiresLargerChange"
                                }
                            }
                        }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
                        var retryResult = await _dynamicToolHandler(CreateCall(toolThreadId, turnId, "call-review-2", "oratorio_run", "SubmitReviewDraft", retryArguments), CancellationToken.None);
                        captureToolResult(retryResult);
                    }
                    else
                    {
                        object[] comments = outcome switch
                        {
                            FakeAppServerOutcome.SubmitSummaryOnlyReviewDraft or FakeAppServerOutcome.SubmitSummaryOnlyReviewDraftThenDisconnect => [],
                            FakeAppServerOutcome.SubmitSuggestionTextNotFoundReviewDraft => new object[]
                            {
                            new
                            {
                                severity = "RED",
                                title = "Missing suggestion text",
                                body = "This suggestion intentionally references text that is not in the right-side diff.",
                                path = "src/Auth/RefreshTokenStore.cs",
                                kind = "suggestion",
                                oldText = "        return missing;",
                                newText = "        return token;"
                            }
                            },
                            FakeAppServerOutcome.SubmitAmbiguousSuggestionTextReviewDraft => new object[]
                            {
                            new
                            {
                                severity = "RED",
                                title = "Ambiguous suggestion text",
                                body = "This suggestion intentionally references a repeated right-side snippet.",
                                path = "src/Auth/RefreshTokenStore.cs",
                                kind = "suggestion",
                                oldText = "        return refreshed;",
                                newText = "        return token;"
                            }
                            },
                            FakeAppServerOutcome.SubmitMultiLineReviewDraft => new object[]
                            {
                            new
                            {
                                severity = "RED",
                                title = "Refresh order is reversed",
                                body = "The refreshed value should be returned before falling back to the original token.",
                                path = "src/Auth/RefreshTokenStore.cs",
                                kind = "suggestion",
                                oldText = "        return token;\n        return refreshed;",
                                newText = "        return refreshed;\n        return token;"
                            }
                            },
                            FakeAppServerOutcome.SubmitContextLineReviewDraft => new object[]
                            {
                            new
                            {
                                severity = "RED",
                                title = "Context line needs explicit handling",
                                body = "The replacement targets an unchanged diff context line.",
                                path = "src/Auth/RefreshTokenStore.cs",
                                kind = "suggestion",
                                oldText = "line89",
                                newText = "line89 fixed"
                            }
                            },
                            FakeAppServerOutcome.SubmitNoOpReviewDraft => new object[]
                            {
                            new
                            {
                                severity = "YELLOW",
                                title = "No-op replacement",
                                body = "This suggestion intentionally matches the current diff text.",
                                path = "src/Auth/RefreshTokenStore.cs",
                                kind = "suggestion",
                                oldText = "        return refreshed;",
                                newText = "        return refreshed;"
                            }
                            },
                            FakeAppServerOutcome.SubmitSyntheticBadAnchorReviewDraft => new object[]
                            {
                            new
                            {
                                severity = "YELLOW",
                                title = "Synthetic anchor drift",
                                body = "This finding is anchored to a full-file line that is not commentable in the diff.",
                                path = "src/Example/SyntheticFileSearcher.cs",
                                kind = "commentOnly",
                                line = 129,
                                side = "right",
                                reason = "requiresLargerChange"
                            }
                            },
                            FakeAppServerOutcome.SubmitCleanReviewDraft => new object[]
                            {
                            new
                            {
                                severity = "RED",
                                title = "Missing refresh guard",
                                body = "The refresh path can return the stale token.",
                                path = "src/Auth/RefreshTokenStore.cs",
                                kind = "suggestion",
                                oldText = "        return refreshed;",
                                newText = "        return token;"
                            },
                            new
                            {
                                severity = "YELLOW",
                                title = "Validate middleware setup",
                                body = "The middleware should validate before invoking the next step.",
                                path = "src/Auth/JwtMiddleware.cs",
                                kind = "commentOnly",
                                line = 22,
                                side = "right",
                                reason = "needsHumanDecision"
                            }
                            },
                            _ => new object[]
                            {
                            new
                            {
                                severity = "RED",
                                title = "Missing refresh guard",
                                body = "The refresh path can return the stale token.",
                                path = "src/Auth/RefreshTokenStore.cs",
                                kind = "suggestion",
                                oldText = "        return refreshed;",
                                newText = "        return token;"
                            },
                            new
                            {
                                severity = "YELLOW",
                                title = "Validate middleware setup",
                                body = "The middleware should validate before invoking the next step.",
                                path = "src/Auth/JwtMiddleware.cs",
                                kind = "commentOnly",
                                line = 22,
                                side = "right",
                                reason = "needsHumanDecision"
                            },
                            new
                            {
                                severity = "YELLOW",
                                title = "Missing file",
                                body = "This path is not in the PR diff.",
                                path = "missing/File.cs",
                                kind = "commentOnly",
                                line = 5,
                                side = "right",
                                reason = "cannotAnchorSafely"
                            },
                            new
                            {
                                severity = "RED",
                                title = "Bad comment anchor",
                                body = "This line is not commentable on the right side.",
                                path = "src/Auth/RefreshTokenStore.cs",
                                kind = "commentOnly",
                                line = 999,
                                side = "right",
                                reason = "cannotAnchorSafely"
                            }
                            }
                        };
                        var arguments = JsonSerializer.SerializeToElement(new
                        {
                            summary = new
                            {
                                majorCount = isSummaryOnly ? 0 : 1,
                                minorCount = isSummaryOnly ? 0 : 1,
                                suggestionCount = outcome switch
                                {
                                    FakeAppServerOutcome.SubmitSummaryOnlyReviewDraft or FakeAppServerOutcome.SubmitSummaryOnlyReviewDraftThenDisconnect => 0,
                                    _ => 1
                                },
                                body = isSummaryOnly
                                    ? "Verbose clean review summary from DotCraft."
                                    : "Verbose review summary from DotCraft."
                            },
                            comments
                        }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
                        var toolThreadId = useMismatchedToolThreadId() ? "thread-mismatch" : threadId;
                        var result = await _dynamicToolHandler(CreateCall(toolThreadId, turnId, "call-review-1", "oratorio_run", "SubmitReviewDraft", arguments), CancellationToken.None);
                        captureToolResult(result);
                    }
                }

                var completion = isSummaryOnly
                    ? "DotCraft analysis complete."
                    : "DotCraft review draft submitted.";
                _notifications.Writer.TryWrite(Notification("item/agentMessage/delta", new { delta = completion + " " }));
                if (outcome != FakeAppServerOutcome.SubmitSummaryOnlyReviewDraftThenDisconnect)
                {
                    _notifications.Writer.TryWrite(Notification("turn/completed", new { threadId, turnId, summary = completion }));
                }
                _notifications.Writer.TryComplete();
            });
        }
        else if (outcome == FakeAppServerOutcome.SubmitSummaryOnlyReviewDraftTwice)
        {
            _ = Task.Run(async () =>
            {
                await SubmitSummaryOnlyDraftAsync(threadId, turnId, "call-review-1", "First review summary from DotCraft.");
                await SubmitSummaryOnlyDraftAsync(threadId, turnId, "call-review-2", "Second review summary from DotCraft.");
                _notifications.Writer.TryWrite(Notification("turn/completed", new { threadId, turnId, summary = "DotCraft review draft submitted." }));
                _notifications.Writer.TryComplete();
            });
        }
        else if (outcome == FakeAppServerOutcome.CompleteTurnWithoutDraftStreamOpen)
        {
            _notifications.Writer.TryWrite(Notification("turn/completed", new { threadId, turnId, summary = "DotCraft analysis complete." }));
        }
        else if (outcome == FakeAppServerOutcome.HoldWakeTurnAfterBackgroundWork)
        {
            _backgroundWork = true;
            _notifications.Writer.TryWrite(Notification("turn/completed", new { threadId, turnId, summary = "Waiting for the background reviewer." }));
            _ = Task.Run(async () =>
            {
                await _backgroundWorkObserved.Task;
                _backgroundWork = false;
                _notifications.Writer.TryWrite(Notification("turn/started", new { threadId, turnId = "turn-wake-1" }));
            });
        }
        else if (outcome == FakeAppServerOutcome.HoldBackgroundWork)
        {
            _backgroundWork = true;
            _notifications.Writer.TryWrite(Notification("turn/completed", new { threadId, turnId, summary = "Waiting for the background reviewer." }));
        }
        else if (outcome == FakeAppServerOutcome.SubmitReviewDraftAfterBackgroundWork)
        {
            _backgroundWork = true;
            _notifications.Writer.TryWrite(Notification("turn/completed", new { threadId, turnId, summary = "Waiting for the background reviewer." }));
            _ = Task.Run(async () =>
            {
                await _backgroundWorkObserved.Task;
                _backgroundWork = false;
                const string wakeTurnId = "turn-wake-1";
                _notifications.Writer.TryWrite(Notification("turn/started", new { threadId, turnId = wakeTurnId }));
                await wakeTurnStarted(wakeTurnId);
                await SubmitSummaryOnlyDraftAsync(threadId, wakeTurnId, "call-review-1", "Review summary after background work.");
                _notifications.Writer.TryWrite(Notification("item/agentMessage/delta", new { delta = "DotCraft review draft submitted. " }));
                _notifications.Writer.TryWrite(Notification("turn/completed", new { threadId, turnId = wakeTurnId, summary = "DotCraft review draft submitted." }));
                _notifications.Writer.TryComplete();
            });
        }
        else if (outcome == FakeAppServerOutcome.SubmitImplementationDraft)
        {
            _ = Task.Run(async () =>
            {
                await SubmitImplementationDraftAsync(threadId, turnId);

                _notifications.Writer.TryWrite(Notification("item/agentMessage/delta", new { delta = "DotCraft implementation draft submitted. " }));
                _notifications.Writer.TryWrite(Notification("turn/completed", new { threadId, turnId, summary = "DotCraft implementation draft submitted." }));
                _notifications.Writer.TryComplete();
            });
        }
        else if (outcome == FakeAppServerOutcome.SubmitFollowUpDraft)
        {
            _ = Task.Run(async () =>
            {
                if (_dynamicToolHandler is not null)
                {
                    var reviewArguments = JsonSerializer.SerializeToElement(new
                    {
                        summary = new
                        {
                            majorCount = 0,
                            minorCount = 0,
                            suggestionCount = 0,
                            body = "Verbose clean review summary from DotCraft."
                        },
                        comments = Array.Empty<object>()
                    }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
                    var toolThreadId = useMismatchedToolThreadId() ? "thread-mismatch" : threadId;
                    var reviewResult = await _dynamicToolHandler(CreateCall(toolThreadId, turnId, "call-review-1", "oratorio_run", "SubmitReviewDraft", reviewArguments), CancellationToken.None);
                    captureToolResult(reviewResult);

                    var arguments = JsonSerializer.SerializeToElement(new
                    {
                        proposals = new object[]
                        {
                            new
                            {
                                title = "Split out migration cleanup",
                                body = "The migration cleanup is useful but should be handled separately from this review.",
                                rationale = "It touches a different risk area.",
                                repository = getFollowUpDraftRepository(),
                                assignee = "maintainer",
                                branch = getFollowUpDraftBranch(),
                                labels = new[] { "follow-up", "backend" }
                            }
                        }
                    }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
                    var result = await _dynamicToolHandler(CreateCall(toolThreadId, turnId, "call-follow-up-1", "oratorio_run", "SubmitFollowUpDraft", arguments), CancellationToken.None);
                    captureToolResult(result);
                }

                _notifications.Writer.TryWrite(Notification("item/agentMessage/delta", new { delta = "DotCraft follow-up draft submitted. " }));
                _notifications.Writer.TryWrite(Notification("turn/completed", new { threadId, turnId, summary = "DotCraft follow-up draft submitted." }));
                _notifications.Writer.TryComplete();
            });
        }
        else if (outcome == FakeAppServerOutcome.SubmitDiscussionReply)
        {
            return ReplyBeforeTurnStartReturnsAsync(threadId, turnId, prompt);
        }
        else if (outcome == FakeAppServerOutcome.DismissFindingThenReply)
        {
            _ = Task.Run(async () =>
            {
                var findingId = ExtractTickedValue(prompt, "findingId `");
                if (findingId is not null)
                {
                    var arguments = JsonSerializer.SerializeToElement(new
                    {
                        findingId,
                        resolutionKind = "dismissed"
                    }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
                    captureToolResult(await _dynamicToolHandler!(CreateCall(threadId, turnId, "call-resolve-1", "oratorio_run", "ResolveReviewFinding", arguments), CancellationToken.None));
                }

                await SubmitDiscussionReplyAsync(threadId, turnId, "call-discussion-1", ExtractDiscussionTurnId(prompt, _lastResumeRequest?.RuntimeAdditionalContext));
                _notifications.Writer.TryWrite(Notification("turn/completed", new { threadId, turnId }));
                _notifications.Writer.TryComplete();
            });
        }
        else if (outcome == FakeAppServerOutcome.SubmitDiscussionReplyTwice)
        {
            _ = Task.Run(async () =>
            {
                var discussionTurnId = ExtractDiscussionTurnId(prompt, null);
                await SubmitDiscussionReplyAsync(threadId, turnId, "call-discussion-1", discussionTurnId);
                await SubmitDiscussionReplyAsync(threadId, turnId, "call-discussion-2", discussionTurnId);
                _notifications.Writer.TryWrite(Notification("turn/completed", new { threadId, turnId }));
                _notifications.Writer.TryComplete();
            });
        }
        else if (outcome == FakeAppServerOutcome.ReplyAfterBackgroundWork)
        {
            _backgroundWork = true;
            _notifications.Writer.TryWrite(Notification("turn/completed", new { threadId, turnId }));
            _ = Task.Run(async () =>
            {
                await _backgroundWorkObserved.Task;
                _backgroundWork = false;
                const string wakeTurnId = "turn-wake-1";
                _notifications.Writer.TryWrite(Notification("turn/started", new { threadId, turnId = wakeTurnId }));
                await SubmitDiscussionReplyAsync(threadId, wakeTurnId, "call-discussion-1", ExtractDiscussionTurnId(prompt, null));
                _notifications.Writer.TryWrite(Notification("turn/completed", new { threadId, turnId = wakeTurnId }));
                _notifications.Writer.TryComplete();
            });
        }
        else if (outcome == FakeAppServerOutcome.SubmitReviewDraftOnSteer)
        {
            _ = Task.Run(async () =>
            {
                await _steered.Task;
                await SteeredRunRelease();
                await SubmitSummaryOnlyDraftAsync(threadId, turnId, "call-review-1", "Review summary before the question is answered.");
                _notifications.Writer.TryWrite(Notification("turn/completed", new { threadId, turnId }));
                _notifications.Writer.TryComplete();
            });
        }
        else if (outcome is FakeAppServerOutcome.ReplyOnSteer or FakeAppServerOutcome.SubmitReviewDraftAfterSteer or FakeAppServerOutcome.SubmitImplementationDraftAfterSteer)
        {
            _ = ReplyOnSteerAsync(threadId, turnId);
        }
        else if (outcome == FakeAppServerOutcome.Fail)
        {
            _notifications.Writer.TryWrite(Notification("turn/failed", new { threadId, turnId, error = new { message = "Synthetic DotCraft failure." } }));
            _notifications.Writer.TryComplete();
        }
        else if (outcome == FakeAppServerOutcome.Disconnect)
        {
            _notifications.Writer.TryComplete();
        }

        return Task.FromResult<string?>(turnId);
    }

    private async Task<string?> ReplyBeforeTurnStartReturnsAsync(string threadId, string turnId, string prompt)
    {
        if (_dynamicToolHandler is not null)
        {
            var arguments = JsonSerializer.SerializeToElement(new
            {
                discussionTurnId = ExtractDiscussionTurnId(prompt, _lastResumeRequest?.RuntimeAdditionalContext),
                body = "Agent answer from DotCraft."
            }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            var toolThreadId = useMismatchedToolThreadId() ? "thread-mismatch" : threadId;
            var result = await _dynamicToolHandler(CreateCall(toolThreadId, turnId, "call-discussion-1", "oratorio_run", "SubmitDiscussionReply", arguments), CancellationToken.None);
            captureToolResult(result);
        }

        _notifications.Writer.TryWrite(Notification("item/agentMessage/delta", new { delta = "DotCraft discussion reply submitted. " }));
        _notifications.Writer.TryWrite(Notification("turn/completed", new { threadId, turnId, summary = "DotCraft discussion reply submitted." }));
        _notifications.Writer.TryComplete();
        return turnId;
    }

    public Task<string?> StartTurnAsync(string threadId, IReadOnlyList<TurnInputPartDto> input, string? modelId, CancellationToken ct) =>
        StartTurnAsync(threadId, input.FirstOrDefault()?.Text ?? "", ct);

    public Task<string?> EnqueueTurnAsync(string threadId, IReadOnlyList<TurnInputPartDto> input, CancellationToken ct) =>
        Task.FromResult<string?>("queued-test-1");

    public Task SteerTurnAsync(string threadId, string expectedTurnId, IReadOnlyList<TurnInputPartDto> input, CancellationToken ct)
    {
        var prompt = input.FirstOrDefault()?.Text ?? "";
        captureSteer(new FakeAppServerSteer(threadId, expectedTurnId, prompt));
        _steered.TrySetResult(prompt);
        return Task.CompletedTask;
    }

    private async Task SubmitDiscussionReplyAsync(string threadId, string turnId, string callId, string discussionTurnId)
    {
        var arguments = JsonSerializer.SerializeToElement(new
        {
            discussionTurnId,
            body = "Agent answer from DotCraft."
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        captureToolResult(await _dynamicToolHandler!(CreateCall(threadId, turnId, callId, "oratorio_run", "SubmitDiscussionReply", arguments), CancellationToken.None));
    }

    public Task InterruptTurnAsync(string threadId, string turnId, CancellationToken ct)
    {
        interruptTurn(threadId, turnId);
        _interrupted = true;
        if (outcome is not (FakeAppServerOutcome.TimeoutAfterTurnStartedWithoutTerminal or FakeAppServerOutcome.HoldInterruptWithoutTerminal))
        {
            _notifications.Writer.TryWrite(Notification("turn/cancelled", new { threadId, turnId }));
        }

        return Task.CompletedTask;
    }

    public Task<AppServerThreadReadResult> ReadThreadAsync(string threadId, CancellationToken ct) =>
        Task.FromResult(new AppServerThreadReadResult(threadId, []));

    public Task<IReadOnlyList<ModelInfoDto>> ListModelsAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<ModelInfoDto>>([new("fake-model", "Fake Model", "test")]);

    public async Task<bool> HasBackgroundWorkAsync(string threadId, CancellationToken ct)
    {
        if (outcome == FakeAppServerOutcome.HoldBackgroundWork && Interlocked.Increment(ref _backgroundWorkChecks) > 1)
        {
            await Task.Delay(Timeout.Infinite, ct);
        }

        var running = _backgroundWork;
        if (running)
        {
            _backgroundWorkObserved.TrySetResult();
        }

        return running;
    }

    private async Task SubmitSummaryOnlyDraftAsync(string threadId, string turnId, string callId, string body)
    {
        var arguments = JsonSerializer.SerializeToElement(new
        {
            summary = new { majorCount = 0, minorCount = 0, suggestionCount = 0, body },
            comments = Array.Empty<object>()
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        captureToolResult(await _dynamicToolHandler!(CreateCall(threadId, turnId, callId, "oratorio_run", "SubmitReviewDraft", arguments), CancellationToken.None));
    }

    public IAsyncEnumerable<DotCraftRunEvent> ReadEventsAsync(CancellationToken ct) =>
        outcome is FakeAppServerOutcome.TimeoutAfterTurnStarted or FakeAppServerOutcome.TimeoutAfterTurnStartedWithoutTerminal ||
        outcome == FakeAppServerOutcome.HoldInterruptWithoutTerminal && _interrupted
            ? ReadEventsThenCancelAsync(ct)
            : _notifications.Reader.ReadAllAsync(ct);

    public ValueTask DisposeAsync()
        => ValueTask.CompletedTask;

    private static DotCraftRunEvent Notification(string method, object parameters)
    {
        var json = JsonSerializer.SerializeToElement(parameters, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var raw = new AppServerNotification(method, json);
        var threadId = String(json, "threadId") ?? "";
        var turnId = String(json, "turnId");
        if (method.StartsWith("turn/", StringComparison.Ordinal))
        {
            var turn = new SessionTurn
            {
                Id = turnId ?? "turn-test",
                ThreadId = threadId,
                Status = method["turn/".Length..],
                StartedAt = DateTimeOffset.UnixEpoch,
                Error = Error(json)
            };
            var value = new TurnNotification { Turn = turn, Error = Error(json) };
            return new DotCraftRunEvent<TurnNotification>(method, threadId, turn.Id, value, raw);
        }

        if (method.Contains("/delta", StringComparison.OrdinalIgnoreCase)
            || method.EndsWith("Delta", StringComparison.OrdinalIgnoreCase))
        {
            var value = new ItemDeltaNotification
            {
                ThreadId = threadId,
                TurnId = turnId,
                ItemId = String(json, "itemId"),
                DeltaKind = String(json, "deltaKind"),
                Delta = String(json, "delta") ?? ""
            };
            return new DotCraftRunEvent<ItemDeltaNotification>(method, threadId, turnId, value, raw);
        }

        return new DotCraftRunEvent(method, threadId, turnId, raw);
    }

    private static DynamicToolCall CreateCall(
        string threadId,
        string turnId,
        string callId,
        string? toolNamespace,
        string tool,
        JsonElement arguments) =>
        new()
        {
            ThreadId = threadId,
            TurnId = turnId,
            CallId = callId,
            Namespace = toolNamespace,
            Tool = tool,
            Arguments = arguments
        };

    private async IAsyncEnumerable<DotCraftRunEvent> ReadEventsThenCancelAsync([EnumeratorCancellation] CancellationToken ct)
    {
        while (_notifications.Reader.TryRead(out var notification))
        {
            yield return notification;

            if (notification.Type is "turn/completed" or "turn/failed" or "turn/cancelled")
            {
                yield break;
            }
        }

        await Task.Yield();
        throw new OperationCanceledException(ct);
    }

    private static string? ExtractTickedValue(string source, string marker)
    {
        var start = source.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0)
        {
            return null;
        }

        start += marker.Length;
        var end = source.IndexOf('`', start);
        return end > start ? source[start..end] : null;
    }

    private static string ExtractDiscussionTurnId(
        string prompt,
        IReadOnlyDictionary<string, AppServerRuntimeAdditionalContextEntry>? runtimeAdditionalContext)
    {
        const string marker = "discussionTurnId `";
        var context = runtimeAdditionalContext is not null &&
            runtimeAdditionalContext.TryGetValue("oratorio.discussionTurn", out var entry)
            ? entry.Value
            : null;
        var sources = string.IsNullOrWhiteSpace(context)
            ? new[] { prompt }
            : new[] { context!, prompt };
        foreach (var source in sources)
        {
            var start = source.IndexOf(marker, StringComparison.Ordinal);
            if (start < 0)
            {
                continue;
            }

            start += marker.Length;
            var end = source.IndexOf('`', start);
            return end > start ? source[start..end] : "missing-discussion-turn";
        }

        return "missing-discussion-turn";
    }

    private static DotCraftRunEvent DisposedNotification(string method)
    {
        var document = JsonDocument.Parse("""{"text":"unavailable"}""");
        var payload = document.RootElement;
        document.Dispose();
        var raw = new AppServerNotification(method, JsonSerializer.SerializeToElement(new { }));
        var parameters = new ItemNotification
        {
            ThreadId = "thread-disposed",
            TurnId = "turn-disposed",
            Item = new SessionItem
            {
                Id = "item-disposed",
                TurnId = "turn-disposed",
                Type = "agentMessage",
                Status = "completed",
                CreatedAt = DateTimeOffset.UnixEpoch,
                PayloadKind = "agentMessage",
                Payload = payload
            }
        };
        return new DotCraftRunEvent<ItemNotification>(method, parameters.ThreadId, parameters.TurnId, parameters, raw);
    }

    private static string? String(JsonElement value, string name) =>
        value.ValueKind == JsonValueKind.Object
        && value.TryGetProperty(name, out var property)
        && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static string? Error(JsonElement value)
    {
        if (String(value, "error") is { } direct)
            return direct;
        if (value.ValueKind == JsonValueKind.Object
            && value.TryGetProperty("error", out var error)
            && error.ValueKind == JsonValueKind.Object)
            return String(error, "message");
        return null;
    }
}
