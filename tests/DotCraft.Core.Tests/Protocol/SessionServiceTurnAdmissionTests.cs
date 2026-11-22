using DotCraft.Configuration;
using DotCraft.Sessions;
using Microsoft.Extensions.AI;
using Xunit;

namespace DotCraft.Tests.Sessions.Protocol;

public sealed partial class SessionServiceTurnAdmissionTests
{
    [Fact]
    public async Task IdleSubmission_CapturesAndPersistsSparseModelSettings()
    {
        await using var h = new AdmissionHarness();
        var thread = await h.CreateThreadAsync(new ThreadConfiguration
        {
            DeveloperInstructions = "Keep this instruction.",
            Reasoning = new() { Enabled = false, Effort = ModelReasoningEffort.Medium, Output = ReasoningOutput.Full }
        });
        var admitted = await h.Service.StartOrSteerTurnAsync(thread.Id, [new TextContent("first")], settings: new()
        {
            Model = "selected-model",
            Reasoning = new() { Effort = ModelReasoningEffort.High },
            Speed = InferenceSpeed.Fast
        });
        Assert.True(admitted.Started);
        Assert.Equal(TurnStatus.Running, admitted.Turn.Status);
        Assert.Null(admitted.Turn.CompletedAt);
        Assert.NotNull(admitted.Events);
        Assert.Empty(admitted.Turn.Items);
        var events = await DrainAsync(admitted.Events!);
        Assert.Single(events.Where(e => e.EventType == SessionEventType.TurnStarted));
        Assert.Contains(events, e => e.EventType == SessionEventType.TurnCompleted);
        Assert.Equal("selected-model", Assert.Single(h.Provider.Requests).Model);
        var saved = await h.Store.LoadThreadAsync(thread.Id);
        Assert.Equal("selected-model", saved!.Configuration!.Model);
        Assert.Equal(ModelReasoningEffort.High, saved.Configuration.Reasoning!.Effort);
        Assert.False(saved.Configuration.Reasoning.Enabled);
        Assert.Equal(ReasoningOutput.Full, saved.Configuration.Reasoning.Output);
        Assert.Equal(InferenceSpeed.Fast, saved.Configuration.Speed);
        Assert.Equal("Keep this instruction.", saved.Configuration.DeveloperInstructions);

        var inherited = await h.Service.StartOrSteerTurnAsync(thread.Id, []);
        var continued = await DrainAsync(inherited.Events!);
        Assert.DoesNotContain(continued, e => e.ItemPayload?.Type == ItemType.UserMessage);
        Assert.All(h.Provider.Requests, request => Assert.Equal("selected-model", request.Model));

        var switched = await h.Service.StartOrSteerTurnAsync(thread.Id, [new TextContent("switch")],
            settings: new() { ProviderId = "target", Model = "selected-target" });
        await DrainAsync(switched.Events!);
        Assert.Equal("target", h.Provider.Requests.Last().ProviderId);
        var configuration = (await h.Store.LoadThreadAsync(thread.Id))!.Configuration!;
        Assert.Equal("selected-target", configuration.Model);
        Assert.Equal(ModelReasoningEffort.Low, configuration.Reasoning!.Effort);
        Assert.Equal(InferenceSpeed.Fast, configuration.Speed);
        Assert.Equal("Keep this instruction.", configuration.DeveloperInstructions);
        Assert.Equal("target-model", h.Config.ProviderPreferences["target"].Model);
    }

    [Fact]
    public async Task ActiveSubmission_SteersOnceAndRetainsTheActiveConfiguration()
    {
        await using var h = new AdmissionHarness(blockRequests: true);
        var thread = await h.CreateThreadAsync();
        var first = await h.Service.StartOrSteerTurnAsync(thread.Id, [new TextContent("first")]);
        var drain = DrainAsync(first.Events!);
        await h.Provider.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var updates = 0;
        h.Service.ThreadUpdatedForBroadcast += _ => updates++;
        try
        {
            var second = await h.Service.StartOrSteerTurnAsync(thread.Id, [new TextContent("guidance")],
                inputSnapshot: new() { ClientUserMessageId = "guidance-id" },
                settings: new() { Model = "future-model", Mode = "plan", Cwd = h.Root, RuntimeWorkspaceRoots = [] });
            Assert.False(second.Started);
            Assert.Null(second.Events);
            Assert.Equal(first.Turn.Id, second.Turn.Id);
            Assert.Single(thread.Turns);
            var queued = Assert.Single(thread.QueuedInputs);
            Assert.Equal("guidancePending", queued.Status);
            Assert.Equal("guidance-id", queued.ClientUserMessageId);
            Assert.Equal("future-model", thread.Configuration!.Model);
            Assert.Equal("plan", thread.Configuration.Mode);
            Assert.Empty(thread.Configuration.RuntimeWorkspaceRoots!);
            Assert.Equal(1, updates);
            Assert.All(h.Provider.Requests, request => Assert.Equal("initial-model", request.Model));
        }
        finally { h.Provider.Release.TrySetResult(); }
        var events = await drain.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Single(events.Where(e => e.EventType == SessionEventType.TurnStarted));
        Assert.Contains(events, e => e.ItemPayload?.AsUserMessage?.ClientUserMessageId == "guidance-id");
        Assert.All(h.Provider.Requests, request => Assert.Equal("initial-model", request.Model));

        var next = await h.Service.StartOrSteerTurnAsync(thread.Id, [new TextContent("next")]);
        await DrainAsync(next.Events!);
        Assert.Equal("future-model", h.Provider.Requests.Last().Model);
        Assert.Equal("future-model", (await h.Store.LoadThreadAsync(thread.Id))!.Configuration!.Model);
    }

    [Fact]
    public async Task FollowupAfterAnotherTurnStarts_QueuesWithoutSteeringThatTurn()
    {
        await using var h = new AdmissionHarness(blockRequests: true);
        var thread = await h.CreateThreadAsync(new ThreadConfiguration
        {
            Reasoning = new() { Enabled = false, Output = ReasoningOutput.Summary }
        });
        var first = await h.Service.StartOrSteerTurnAsync(thread.Id, [new TextContent("first")]);
        var drain = DrainAsync(first.Events!);
        await h.Provider.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));

        try
        {
            var followup = await h.Service.StartOrEnqueueTurnAsync(thread.Id, [new TextContent("follow up")],
                model: "future-model", reasoning: new() { Enabled = true, Effort = ModelReasoningEffort.High });

            Assert.Null(followup.Turn);
            Assert.Null(followup.Events);
            var queued = Assert.Single(thread.QueuedInputs);
            Assert.Equal(followup.QueuedInput?.Id, queued.Id);
            Assert.Equal("queued", queued.Status);
            Assert.Equal(first.Turn.Id, queued.ReadyAfterTurnId);
            Assert.Equal("initial-model", Assert.Single(h.Provider.Requests).Model);
            var saved = await h.Store.LoadThreadAsync(thread.Id);
            Assert.Equal("future-model", saved!.Configuration!.Model);
            Assert.True(saved.Configuration.Reasoning!.Enabled);
            Assert.Equal(ModelReasoningEffort.High, saved.Configuration.Reasoning.Effort);
            Assert.Equal(ReasoningOutput.Summary, saved.Configuration.Reasoning.Output);
        }
        finally { h.Provider.Release.TrySetResult(); }

        var events = await drain.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.DoesNotContain(events, e => e.ItemPayload?.AsUserMessage?.Text == "follow up");
    }

    [Fact]
    public async Task FollowupAfterPriorQueueDrain_StartsItsOwnTurn()
    {
        await using var h = new AdmissionHarness();
        var thread = await h.CreateThreadAsync();
        var first = await h.Service.StartOrSteerTurnAsync(thread.Id, [new TextContent("first")]);
        await DrainAsync(first.Events!);
        await h.Service.TryStartNextQueuedTurnAsync(thread.Id);

        var followup = await h.Service.StartOrEnqueueTurnAsync(thread.Id, [new TextContent("follow up")],
            model: "followup-model");

        Assert.Null(followup.QueuedInput);
        Assert.NotNull(followup.Turn);
        Assert.NotEqual(first.Turn.Id, followup.Turn.Id);
        Assert.Empty(thread.QueuedInputs);
        var events = await DrainAsync(followup.Events!);
        Assert.Contains(events, e => e.ItemPayload?.AsUserMessage?.Text == "follow up");
        Assert.Equal("followup-model", h.Provider.Requests.Last().Model);
    }

    [Fact]
    public async Task EmptyActiveInput_RejectsWithoutApplyingSettings()
    {
        await using var h = new AdmissionHarness(blockRequests: true);
        var thread = await h.CreateThreadAsync();
        var first = await h.Service.StartOrSteerTurnAsync(thread.Id, [new TextContent("first")]);
        var drain = DrainAsync(first.Events!);
        await h.Provider.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            await Assert.ThrowsAsync<ArgumentException>(() => h.Service.StartOrSteerTurnAsync(
                thread.Id, [], settings: new()
                {
                    Model = "must-not-apply", Mode = "plan", ApprovalPolicy = ApprovalPolicy.AutoApprove
                }));
            Assert.Empty(thread.QueuedInputs);
            Assert.Equal("initial-model", thread.Configuration!.Model);
            Assert.Equal("agent", thread.Configuration.Mode);
            Assert.Equal(ApprovalPolicy.Default, thread.Configuration.ApprovalPolicy);
        }
        finally { h.Provider.Release.TrySetResult(); }
        await drain.WaitAsync(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task ResourcePreparationAndConcurrentConfigurationUpdate_DoNotMixTurnModels()
    {
        var barrier = new AdmissionRefresher();
        await using var h = new AdmissionHarness(blockRequests: true, refresher: barrier);
        var thread = await h.CreateThreadAsync();
        barrier.Block = true;
        Task<List<SessionEvent>> drain;
        try
        {
            var admission = h.Service.StartOrSteerTurnAsync(thread.Id, [new TextContent("first")],
                settings: new() { Model = "admitted-model" });
            await barrier.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var update = h.Service.UpdateThreadConfigurationAsync(thread.Id, new ThreadConfiguration
            {
                ProviderId = "source", Model = "later-model", DeveloperInstructions = "later settings"
            });
            Assert.False(update.IsCompleted);
            barrier.Release.TrySetResult();
            var accepted = await admission.WaitAsync(TimeSpan.FromSeconds(10));
            drain = DrainAsync(accepted.Events!);
            await update.WaitAsync(TimeSpan.FromSeconds(10));
            await h.Provider.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal("later-model", thread.Configuration!.Model);
            Assert.All(h.Provider.Requests, request => Assert.Equal("admitted-model", request.Model));
        }
        finally
        {
            barrier.Release.TrySetResult();
            h.Provider.Release.TrySetResult();
        }
        await drain.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.All(h.Provider.Requests, request => Assert.Equal("admitted-model", request.Model));
    }

    [Fact]
    public async Task ConfigurationPersistenceFailure_DoesNotAdmitOrPublishTheCandidate()
    {
        var writer = new AdmissionWriter();
        await using var h = new AdmissionHarness(writer: writer);
        var thread = await h.CreateThreadAsync();
        writer.RejectModel = "rejected-model";
        await Assert.ThrowsAsync<RolloutPersistenceException>(() => h.Service.StartOrSteerTurnAsync(
            thread.Id, [new TextContent("must not run")],
            settings: new()
            {
                Model = "rejected-model", Mode = "plan", ApprovalPolicy = ApprovalPolicy.AutoApprove
            }));
        Assert.Empty(thread.Turns);
        Assert.Empty(h.Provider.Requests);
        Assert.Equal("initial-model", thread.Configuration!.Model);
        Assert.Equal("agent", thread.Configuration.Mode);
        var saved = await h.Store.LoadThreadAsync(thread.Id);
        Assert.Equal("initial-model", saved!.Configuration!.Model);
        Assert.Equal("agent", saved.Configuration.Mode);
        Assert.Equal(ApprovalPolicy.Default, saved.Configuration.ApprovalPolicy);
        Assert.Equal(ApprovalPolicy.Default, thread.Configuration.ApprovalPolicy);
    }

    [Fact]
    public async Task IdleApprovalOverride_PersistsBeforeExecutionAndInheritsOnTheNextTurn()
    {
        await using var h = new AdmissionHarness(requestApprovals: true);
        var thread = await h.CreateThreadAsync(new ThreadConfiguration { ApprovalPolicy = ApprovalPolicy.Prompt });
        var admitted = await h.Service.StartOrSteerTurnAsync(thread.Id, [new TextContent("first")],
            settings: new() { ApprovalPolicy = ApprovalPolicy.AutoApprove });
        var events = await DrainAsync(admitted.Events!).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.DoesNotContain(events, item => item.EventType == SessionEventType.ApprovalRequested);
        Assert.Equal(ApprovalPolicy.AutoApprove, (await h.Store.LoadThreadAsync(thread.Id))!.Configuration!.ApprovalPolicy);

        var inherited = await h.Service.StartOrSteerTurnAsync(thread.Id, [new TextContent("next")]);
        var nextEvents = await DrainAsync(inherited.Events!).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.DoesNotContain(nextEvents, item => item.EventType == SessionEventType.ApprovalRequested);
    }

    [Theory]
    [InlineData(ApprovalPolicy.AutoApprove, true)]
    [InlineData(ApprovalPolicy.Deny, false)]
    public async Task ActiveApprovalOverride_SettlesPendingAndLaterRequests(ApprovalPolicy policy, bool approved)
    {
        await using var h = new AdmissionHarness(requestApprovals: true);
        var thread = await h.CreateThreadAsync(new ThreadConfiguration { ApprovalPolicy = ApprovalPolicy.Prompt });
        var admitted = await h.Service.StartOrSteerTurnAsync(thread.Id, [new TextContent("first")]);
        var pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var drain = ObserveAsync();
        await pending.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var steered = await h.Service.StartOrSteerTurnAsync(thread.Id, [new TextContent("continue")],
            settings: new() { ApprovalPolicy = policy });
        Assert.False(steered.Started);
        Assert.Equal(admitted.Turn.Id, steered.Turn.Id);
        var events = await drain.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Single(events.Where(item => item.EventType == SessionEventType.ApprovalRequested));
        var response = Assert.Single(events, item => item.EventType == SessionEventType.ItemCompleted
            && item.ItemPayload?.Type == ItemType.ApprovalResponse);
        Assert.Equal(approved, response.ItemPayload!.AsApprovalResponse!.Decision.IsApproved());
        Assert.Equal(policy, (await h.Store.LoadThreadAsync(thread.Id))!.Configuration!.ApprovalPolicy);

        async Task<List<SessionEvent>> ObserveAsync()
        {
            var events = new List<SessionEvent>();
            await foreach (var item in admitted.Events!)
            {
                events.Add(item);
                if (item.EventType == SessionEventType.ApprovalRequested)
                    pending.TrySetResult();
            }
            Assert.DoesNotContain(events, item => item.EventType == SessionEventType.TurnFailed);
            return events;
        }
    }

    [Fact]
    public async Task InvalidApprovalOverride_DoesNotAcceptInputOrChangeConfiguration()
    {
        await using var h = new AdmissionHarness();
        var thread = await h.CreateThreadAsync(new ThreadConfiguration { ApprovalPolicy = ApprovalPolicy.Prompt });
        await Assert.ThrowsAsync<ArgumentException>(() => h.Service.StartOrSteerTurnAsync(
            thread.Id, [new TextContent("must not run")],
            settings: new() { Model = "must-not-apply", ApprovalPolicy = (ApprovalPolicy)999 }));
        Assert.Empty(thread.Turns);
        Assert.Empty(h.Provider.Requests);
        var saved = (await h.Store.LoadThreadAsync(thread.Id))!.Configuration!;
        Assert.Equal("initial-model", saved.Model);
        Assert.Equal(ApprovalPolicy.Prompt, saved.ApprovalPolicy);
    }

    [Theory]
    [InlineData("target", null)]
    [InlineData("missing-provider", "selected-model")]
    public async Task RejectedProviderSelection_DoesNotAcceptInputOrChangeConfiguration(string providerId, string? model)
    {
        await using var h = new AdmissionHarness();
        var thread = await h.CreateThreadAsync();
        var settings = new TurnSettingsOverrides { ProviderId = providerId, Model = model };
        await Assert.ThrowsAsync<ArgumentException>(() => h.Service.StartOrSteerTurnAsync(
            thread.Id, [new TextContent("must not run")], settings: settings));
        Assert.Empty(thread.Turns);
        Assert.Empty(h.Provider.Requests);
        var saved = await h.Store.LoadThreadAsync(thread.Id);
        Assert.Equal("initial-model", saved!.Configuration!.Model);
        Assert.Equal("source", saved.Configuration.ProviderId);
        Assert.Equal("agent", saved.Configuration.Mode);
    }

    [Fact]
    public async Task KickoffTurn_IsolatesQueuedFollowupAndExcludesPromptFromTitles()
    {
        await using var h = new AdmissionHarness(blockRequests: true);
        var thread = await h.CreateThreadAsync();

        TurnStartOrEnqueueAdmission kickoff;
        Task<List<SessionEvent>> kickoffDrain;
        using (TurnTriggerScope.Set(new TurnTriggerInfo { Kind = TurnTriggerInfo.KickoffKind }))
        {
            kickoff = await h.Service.StartOrEnqueueTurnAsync(thread.Id, [new TextContent("Greet them in zh-Hans.")]);
            kickoffDrain = DrainAsync(kickoff.Events!);
        }
        await h.Provider.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));

        var followup = await h.Service.StartOrEnqueueTurnAsync(thread.Id, [new TextContent("Ship the conductor")]);
        Assert.NotNull(followup.QueuedInput);
        Assert.Null(followup.QueuedInput.TriggerKind);

        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        h.Service.ThreadRuntimeSignalForBroadcast = (id, signal, turn) =>
        {
            if (id == thread.Id && signal == SessionThreadRuntimeSignal.TurnCompleted && turn?.Id != kickoff.Turn!.Id)
                completed.TrySetResult();
        };

        h.Provider.Release.TrySetResult();
        await kickoffDrain.WaitAsync(TimeSpan.FromSeconds(10));
        await completed.Task.WaitAsync(TimeSpan.FromSeconds(10));

        var saved = (await h.Store.LoadThreadAsync(thread.Id))!;
        Assert.Equal(2, saved.Turns.Count);
        Assert.Equal(TurnTriggerInfo.KickoffKind, saved.Turns[0].Input?.AsUserMessage?.TriggerKind);
        Assert.Null(saved.Turns[1].Input?.AsUserMessage?.TriggerKind);
        Assert.Equal("Ship the conductor", saved.DisplayName);

        var kickoffOnlyFork = await h.Service.ForkThreadAsync(thread.Id, new ThreadForkOptions
        {
            ForkPoint = new ThreadForkPoint { TurnId = kickoff.Turn!.Id, Position = "after" }
        });
        var unnamedSource = await h.CreateThreadAsync();
        using (TurnTriggerScope.Set(new TurnTriggerInfo { Kind = TurnTriggerInfo.KickoffKind }))
        {
            var onlyKickoff = await h.Service.StartOrEnqueueTurnAsync(unnamedSource.Id, [new TextContent("Greet them.")]);
            await DrainAsync(onlyKickoff.Events!);
        }
        var unnamedFork = await h.Service.ForkThreadAsync(unnamedSource.Id);
        Assert.Equal("Ship the conductor", kickoffOnlyFork.DisplayName);
        Assert.True(string.IsNullOrEmpty(unnamedFork.DisplayName));
    }
}
