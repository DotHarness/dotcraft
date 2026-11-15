using DotCraft.Configuration;
using DotCraft.Protocol.AppServer;
using Microsoft.Extensions.AI;
using Xunit;
using Domain = DotCraft.Sessions;

namespace DotCraft.Tests.Sessions.Protocol.AppServer;

public sealed class AppServerTurnAdmissionTests
{
    [Fact]
    public async Task Start_SubmitsSparseSettingsWithInput()
    {
        using var harness = new AppServerTestHarness();
        await harness.InitializeAsync();
        var thread = await harness.Service.CreateThreadAsync(harness.Identity);
        harness.Service.EnqueueSubmitEvents(thread.Id, AppServerTestHarness.BuildTurnEventSequence(thread.Id));

        await harness.ExecuteRequestAsync(harness.BuildRequest(AppServerMethodNames.TurnStart, new
        {
            threadId = thread.Id,
            model = "selected-model",
            providerId = "selected-provider",
            reasoning = new { effort = "high" },
            speed = "fast",
            approvalPolicy = "autoApprove",
            input = new[] { new { type = "text", text = "Run with these settings." } }
        }));

        using var response = await harness.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(response);
        var settings = Assert.IsType<Domain.TurnSettingsOverrides>(harness.Service.LastSubmittedTurnSettings);
        Assert.Equal("selected-provider", settings.ProviderId);
        Assert.Equal("selected-model", settings.Model);
        Assert.Equal(InferenceSpeed.Fast, settings.Speed);
        Assert.Equal(Domain.ApprovalPolicy.AutoApprove, settings.ApprovalPolicy);
        Assert.Equal(ModelReasoningEffort.High, settings.Reasoning!.Effort);
        Assert.Null(settings.Reasoning.Enabled);
        Assert.Null(settings.Reasoning.Output);
        Assert.Equal("Run with these settings.", Assert.IsType<TextContent>(Assert.Single(harness.Service.LastSubmittedContent)).Text);
    }

    [Fact]
    public async Task Start_RejectsInvalidApprovalPolicyBeforeInputSubmission()
    {
        using var harness = new AppServerTestHarness();
        await harness.InitializeAsync();
        var thread = await harness.Service.CreateThreadAsync(harness.Identity);
        await harness.ExecuteRequestAsync(harness.BuildRequest(AppServerMethodNames.TurnStart, new
        {
            threadId = thread.Id, approvalPolicy = "never",
            input = new[] { new { type = "text", text = "must not run" } }
        }));
        using var response = await harness.Transport.ReadNextSentAsync();
        Assert.Equal(-32602, response.RootElement.GetProperty("error").GetProperty("code").GetInt32());
        Assert.Null(harness.Service.LastSubmittedTurnSettings);
        Assert.Empty(harness.Service.LastSubmittedContent);
    }

    [Fact]
    public async Task Start_ReturnsExistingTurnWithoutWaitingForAnotherStartedEvent()
    {
        using var harness = new AppServerTestHarness();
        await harness.InitializeAsync();
        var thread = await harness.Service.CreateThreadAsync(harness.Identity);
        harness.Service.TurnAdmissionHandler = id => new Domain.TurnInputAdmission(
            new Domain.SessionTurn { Id = "turn_007", ThreadId = id, Status = Domain.TurnStatus.Running }, false);

        await harness.ExecuteRequestAsync(harness.BuildRequest(AppServerMethodNames.TurnStart, new
        {
            threadId = thread.Id,
            clientUserMessageId = "follow-up",
            input = new[] { new { type = "text", text = "Also check the tests." } }
        })).WaitAsync(TimeSpan.FromSeconds(10));

        using var response = await harness.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(response);
        var turn = response.RootElement.GetProperty("result").GetProperty("turn");
        Assert.Equal("turn_007", turn.GetProperty("id").GetString());
        Assert.Empty(turn.GetProperty("items").EnumerateArray());
        Assert.Equal("follow-up", harness.Service.LastSubmittedInputSnapshot!.ClientUserMessageId);
        Assert.Empty(harness.Service.YieldedSubmitEventTypes);
    }
}
