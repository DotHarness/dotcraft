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
                thread.Id, [], settings: new() { Model = "must-not-apply", Mode = "plan" }));
            Assert.Empty(thread.QueuedInputs);
            Assert.Equal("initial-model", thread.Configuration!.Model);
            Assert.Equal("agent", thread.Configuration.Mode);
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
        var drain = DrainAsync(accepted.Events!);
        try
        {
            await update.WaitAsync(TimeSpan.FromSeconds(10));
            await h.Provider.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal("later-model", thread.Configuration!.Model);
            Assert.All(h.Provider.Requests, request => Assert.Equal("admitted-model", request.Model));
        }
        finally { h.Provider.Release.TrySetResult(); }
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
            settings: new() { Model = "rejected-model", Mode = "plan" }));
        Assert.Empty(thread.Turns);
        Assert.Empty(h.Provider.Requests);
        Assert.Equal("initial-model", thread.Configuration!.Model);
        Assert.Equal("agent", thread.Configuration.Mode);
        var saved = await h.Store.LoadThreadAsync(thread.Id);
        Assert.Equal("initial-model", saved!.Configuration!.Model);
        Assert.Equal("agent", saved.Configuration.Mode);
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
}
