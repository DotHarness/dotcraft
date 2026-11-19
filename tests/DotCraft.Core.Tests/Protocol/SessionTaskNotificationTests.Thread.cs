using DotCraft.Configuration;
using DotCraft.Hooks;
using DotCraft.Sessions;
using Xunit;

namespace DotCraft.Tests.Sessions.Protocol;

public sealed partial class SessionTaskNotificationTests
{
    private static ThreadTaskReport ChatReport => new(
        "report-1", "thread_other_workspace", "Review", BackgroundTaskStatus.WaitingForInput,
        "The review needs a decision.", "Docs & Tools", "Choose <draft> or <publish>.");

    [Fact]
    public async Task ThreadReport_WakesIdleThreadWithoutUserPromptHook()
    {
        var hook = CreateUserPromptHook(TestHookCommands.StderrAndExit("prompt denied", 2));
        await using var harness = await CreateHarnessAsync(hookRunner: hook);

        Assert.True(await harness.Service.NotifyThreadAsync(harness.ThreadId, ChatReport));
        var thread = await WaitForThreadAsync(harness, t => t.Turns.Count == 1 && t.Turns[0].Status == TurnStatus.Completed);

        var input = Assert.Single(thread.Turns).Input!.AsUserMessage!;
        Assert.Equal("thread", input.TriggerKind);
        Assert.Equal(ChatReport.Title, input.TriggerLabel);
        Assert.Equal(ChatReport.ReportedThreadId, input.TriggerRefId);
        Assert.Equal("waitingForInput", input.TriggerStatus);
        Assert.Empty(thread.QueuedInputs);

        var envelope = Assert.Single(harness.Client.Notifications);
        Assert.Contains("<task_id>thread_other_workspace</task_id>", envelope);
        Assert.Contains("<status>waitingForInput</status>", envelope);
        Assert.Contains("<summary>The review needs a decision.</summary>", envelope);
        Assert.Contains("<project>Docs &amp; Tools</project>", envelope);
        Assert.Contains("Choose &lt;draft&gt; or &lt;publish&gt;.", envelope);
        Assert.DoesNotContain("output_file", envelope);
        Assert.DoesNotContain("WriteStdin", envelope);
    }

    [Fact]
    public async Task ThreadReport_JoinsRunningTurnAsGuidanceOnceWithoutUserPromptHook()
    {
        const string hookLog = "prompt-hook-runs.txt";
        var hook = CreateUserPromptHook(TestHookCommands.AppendLine(hookLog, "run"));
        await using var harness = await CreateHarnessAsync(hookRunner: hook);
        harness.Client.HoldFirstRequest();
        var turn = harness.RunUserTurnAsync("work");
        await harness.Client.FirstRequestStarted.WaitAsync(TimeSpan.FromSeconds(10));

        var queued = await Task.WhenAll(
            harness.Service.NotifyThreadAsync(harness.ThreadId, ChatReport),
            harness.Service.NotifyThreadAsync(harness.ThreadId, ChatReport));
        Assert.Single(queued, static added => added);
        var running = await harness.Service.GetThreadAsync(harness.ThreadId);
        Assert.Equal("guidancePending", Assert.Single(running.QueuedInputs).Status);

        harness.Client.ReleaseFirstRequest();
        await turn;

        Assert.False(await harness.Service.NotifyThreadAsync(harness.ThreadId, ChatReport));
        var completed = await harness.Service.GetThreadAsync(harness.ThreadId);
        var report = Assert.Single(Assert.Single(completed.Turns).Items, item => item.AsUserMessage?.TriggerKind == "thread");
        Assert.Equal("guidance", report.AsUserMessage!.DeliveryMode);
        Assert.Single(harness.Client.Notifications);
        Assert.Empty(completed.QueuedInputs);
        Assert.Single(await File.ReadAllLinesAsync(Path.Combine(_tempDir, hookLog)));
    }

    [Fact]
    public async Task ThreadReport_DeduplicatesAcrossReloadBeforeAndAfterDelivery()
    {
        string threadId;
        await using (var harness = await CreateHarnessAsync())
        {
            threadId = harness.ThreadId;
            await harness.Service.ArchiveThreadAsync(threadId);
            Assert.True(await harness.Service.NotifyThreadAsync(threadId, ChatReport));
            var archived = await harness.Service.GetThreadAsync(threadId);
            Assert.Empty(archived.Turns);
            Assert.Equal("queued", Assert.Single(archived.QueuedInputs).Status);
        }

        await using (var cold = await CreateHarnessAsync(DisableWake, threadId: threadId))
        {
            Assert.False(await cold.Service.NotifyThreadAsync(threadId, ChatReport with { ReportedThreadId = "other" }));
            Assert.True(await cold.Service.NotifyThreadAsync(threadId, ChatReport with
            {
                NotificationId = "report-2", Status = BackgroundTaskStatus.Completed
            }));
            Assert.Equal(2, (await cold.Service.GetThreadAsync(threadId)).QueuedInputs.Count);

            await cold.Service.UnarchiveThreadAsync(threadId);
            await cold.RunUserTurnAsync("continue");
            var consumed = await cold.Service.GetThreadAsync(threadId);
            Assert.Empty(consumed.QueuedInputs);
            Assert.Equal(2, Assert.Single(consumed.Turns).Items.Count(item => item.AsUserMessage?.TriggerKind == "thread"));
        }

        await using var reloaded = await CreateHarnessAsync(threadId: threadId);
        Assert.False(await reloaded.Service.NotifyThreadAsync(threadId, ChatReport));
        var thread = await reloaded.Service.GetThreadAsync(threadId);
        Assert.Single(thread.Turns);
        Assert.Empty(thread.QueuedInputs);

        static void DisableWake(AppConfig config) => config.Tools.BackgroundTasks.CompletionDelivery = "off";
    }

    private HookRunner CreateUserPromptHook(string command) => new(new HooksFileConfig
    {
        Hooks =
        {
            [nameof(HookEvent.UserPromptSubmit)] =
            [
                new HookMatcherGroup
                {
                    Hooks = [new HookEntry { Type = "command", Command = command, Shell = TestHookCommands.Shell, Timeout = 10 }]
                }
            ]
        }
    }, _tempDir);
}
