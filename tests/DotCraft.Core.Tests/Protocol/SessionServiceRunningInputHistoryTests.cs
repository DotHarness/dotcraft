using System.ClientModel.Primitives;
using System.Runtime.CompilerServices;
using System.Text.Json;
using DotCraft.Agents;
using DotCraft.Hooks;
using DotCraft.Sessions;
using Microsoft.Extensions.AI;
using Xunit;

namespace DotCraft.Tests.Sessions.Protocol;

public sealed partial class SessionServiceRuntimeSignalTests
{
    [Fact]
    public async Task RunningInput_IsDurableBeforeSampling_AndSurvivesNextTurnRestartAndRecovery()
    {
        SessionService service = null!;
        SessionThread thread = null!;
        using var model = new HistoryBoundaryClient(async (call, messages, ct) =>
        {
            if (call == 1)
            {
                using (TurnTriggerScope.Set(new TurnTriggerInfo { Kind = "test", RefId = "source-1" }))
                    await service.SteerTurnAsync(thread.Id, thread.Turns[0].Id, [new TextContent("stable guidance")], ct: ct);
                return HistoryBoundaryClient.Tool();
            }
            await using var reader = new ThreadStore(_tempDir);
            var persisted = await reader.LoadModelHistoryAsync(thread.Id, ct);
            Assert.Single(persisted, message => message.Text.Contains("stable guidance", StringComparison.Ordinal));
            Assert.Single(messages, message => message.Text.Contains("stable guidance", StringComparison.Ordinal));
            var saved = await reader.LoadThreadAsync(thread.Id, ct);
            Assert.Empty(saved!.QueuedInputs);
            AssertResponsesUserMessageIds(messages);
            AssertResponsesUserMessageIds(persisted);
            return new TextContent("done");
        });
        await using var factory = CreateAgentFactory(model, workspacePath: Path.GetDirectoryName(_tempDir));
        service = CreateService(factory, model, useStreamingFunctionInvoker: true);
        thread = await service.CreateThreadAsync(new SessionIdentity
        {
            ChannelName = "test", WorkspacePath = Path.GetDirectoryName(_tempDir)!
        });
        await service.RefreshThreadAgentAsync(thread.Id);
        await DrainAsync(service.SubmitInputAsync(thread.Id, [new TextContent("start")]));
        Assert.Equal(TurnStatus.Completed, Assert.Single(thread.Turns).Status);
        await DrainAsync(service.SubmitInputAsync(thread.Id, [new TextContent("next")]));
        var package = await service.ExportThreadRecoveryAsync(thread.Id);
        await using var coldFactory = CreateAgentFactory(model, workspacePath: Path.GetDirectoryName(_tempDir));
        var cold = CreateService(coldFactory, model, useStreamingFunctionInvoker: true);
        await cold.ResumeThreadAsync(thread.Id);
        await DrainAsync(cold.SubmitInputAsync(thread.Id, [new TextContent("after restart")]));
        Assert.Equal(4, model.Calls);
        await cold.DeleteThreadPermanentlyAsync(thread.Id);
        await cold.RestoreThreadRecoveryAsync(package.PackagePath, thread.Id);
        await cold.ResumeThreadAsync(thread.Id);
        // The exported snapshot deliberately predates the last ordinary Turn.
        await DrainAsync(cold.SubmitInputAsync(thread.Id, [new TextContent("after restore")]));
        Assert.Equal(5, model.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RunningInput_FailureOrCancellationKeepsOnlyTheExactPersistedSequence(bool cancel)
    {
        SessionService service = null!;
        SessionThread thread = null!;
        using var model = new HistoryBoundaryClient(async (call, _, ct) =>
        {
            if (call == 1)
            {
                await service.SteerTurnAsync(thread.Id, thread.Turns[0].Id, [new TextContent("keep this input")], ct: ct);
                return HistoryBoundaryClient.Tool();
            }
            if (cancel) throw new OperationCanceledException("cancel the probe");
            throw new InvalidOperationException("fail the probe");
        });
        await using var factory = CreateAgentFactory(model);
        service = CreateService(factory, model, useStreamingFunctionInvoker: true);
        thread = await service.CreateThreadAsync(MakeIdentity());
        await service.RefreshThreadAgentAsync(thread.Id);
        await DrainAsync(service.SubmitInputAsync(thread.Id, [new TextContent("start")]));
        Assert.Equal(cancel ? TurnStatus.Cancelled : TurnStatus.Failed, thread.Turns[0].Status);
        await using var reader = new ThreadStore(_tempDir);
        var history = await reader.LoadModelHistoryAsync(thread.Id);
        Assert.Single(history, message => message.Text.Contains("keep this input", StringComparison.Ordinal));
        Assert.Single(history.SelectMany(message => message.Contents).OfType<FunctionCallContent>());
        Assert.Single(history.SelectMany(message => message.Contents).OfType<FunctionResultContent>());
        Assert.Empty(thread.QueuedInputs);
    }

    [Fact]
    public async Task RunningInput_ColdLoadingReconcilesHistoryWrittenBeforeQueueConfirmation()
    {
        using var model = new HistoryBoundaryClient((_, _, _) => Task.FromResult<AIContent>(new TextContent("unused")));
        await using var factory = CreateAgentFactory(model);
        var service = CreateService(factory, model, useStreamingFunctionInvoker: true);
        var thread = await service.CreateThreadAsync(MakeIdentity());
        var queued = await service.EnqueueTurnInputAsync(thread.Id, [new TextContent("already incorporated")]);
        thread.Turns.Add(new SessionTurn { Id = "interrupted-turn", Status = TurnStatus.Running });
        thread.QueuedInputs = [queued with
        {
            Status = "guidancePending", ReadyAfterTurnId = "interrupted-turn",
            MaterializedInputParts = [new() { Type = "image", Url = "file:///unavailable-original-image.png" }]
        }];
        await using var store = new ThreadStore(_tempDir);
        var persistence = new SessionPersistenceService(store);
        await persistence.SaveThreadAsync(thread);
        var incorporatedImage = new DataContent(new byte[] { 1, 2, 3 }, "image/png")
        {
            AdditionalProperties = new() { [SessionInputMetadataKeys.LocalImagePath] = "unavailable-original-image.png" }
        };
        await persistence.AppendModelHistoryAsync(thread.Id,
            [new ChatMessage(ChatRole.User, [new TextContent("already incorporated"), incorporatedImage])
            {
                AdditionalProperties = new()
                {
                    ["dotcraft.history.inputs"] = JsonSerializer.SerializeToElement(new[]
                    {
                        new { InputId = queued.Id, ItemId = "admitted-item", TurnId = "interrupted-turn" }
                    })
                }
            }], "interrupted-turn");

        await using var coldFactory = CreateAgentFactory(model);
        var cold = CreateService(coldFactory, model, useStreamingFunctionInvoker: true);
        var restored = await cold.ResumeThreadAsync(thread.Id);
        Assert.Empty(restored.QueuedInputs);
        Assert.Equal(TurnStatus.Cancelled, Assert.Single(restored.Turns).Status);
        Assert.Contains(restored.Turns[0].Items, item => item.Id == "admitted-item" && item.AsUserMessage?.QueuedInputId == queued.Id);
        Assert.Single(Assert.Single(restored.Turns[0].Items, item => item.Id == "admitted-item").AsUserMessage!.Images!);
        Assert.Single(await persistence.LoadModelHistoryAsync(thread.Id));
        Assert.Equal(0, model.Calls);
        await cold.ResumeThreadAsync(thread.Id);
        Assert.Single(await persistence.LoadModelHistoryAsync(thread.Id));
    }

    [Fact]
    public async Task RunningInput_AdmitsEveryPendingSteerAtTheNextBoundaryInQueueOrder()
    {
        SessionService service = null!;
        SessionThread thread = null!;
        using var model = new HistoryBoundaryClient(async (call, messages, ct) =>
        {
            if (call == 1)
            {
                await service.SteerTurnAsync(thread.Id, thread.Turns[0].Id, [new TextContent("first steer")], ct: ct);
                await service.SteerTurnAsync(thread.Id, thread.Turns[0].Id, [new TextContent("second steer")], ct: ct);
                return HistoryBoundaryClient.Tool();
            }
            var users = messages.Where(message => message.Role == ChatRole.User).Select(message => message.Text).ToList();
            var first = users.FindIndex(text => text.Contains("first steer", StringComparison.Ordinal));
            var second = users.FindIndex(text => text.Contains("second steer", StringComparison.Ordinal));
            Assert.True(first >= 0 && second == first + 1);
            return new TextContent("done");
        });
        await using var factory = CreateAgentFactory(model);
        service = CreateService(factory, model, useStreamingFunctionInvoker: true);
        thread = await service.CreateThreadAsync(MakeIdentity());
        await service.RefreshThreadAgentAsync(thread.Id);

        await DrainAsync(service.SubmitInputAsync(thread.Id, [new TextContent("start")]));

        Assert.Equal(2, model.Calls);
        var guidance = thread.Turns[0].Items
            .Select(item => item.AsUserMessage)
            .Where(message => message?.DeliveryMode == "guidance")
            .Select(message => message!.Text)
            .ToArray();
        Assert.Equal(["first steer", "second steer"], guidance);
        Assert.Empty(thread.QueuedInputs);
    }

    [Fact]
    public async Task RunningInput_BlockingUserPromptSubmitHookDropsTheSteer()
    {
        var marker = Path.Combine(_tempDir, "prompt-hook-ran");
        SessionService service = null!;
        SessionThread thread = null!;
        using var model = new HistoryBoundaryClient(async (call, messages, ct) =>
        {
            if (call == 1)
            {
                await service.SteerTurnAsync(thread.Id, thread.Turns[0].Id, [new TextContent("blocked steer")], ct: ct);
                return HistoryBoundaryClient.Tool();
            }
            Assert.DoesNotContain(messages, message => message.Text.Contains("blocked steer", StringComparison.Ordinal));
            return new TextContent("done");
        });
        await using var factory = CreateAgentFactory(model);
        service = CreatePromptHookService(factory, model, BlockAfterFirstRunCommand(marker, "steer denied"));
        thread = await service.CreateThreadAsync(MakeIdentity());
        await service.RefreshThreadAgentAsync(thread.Id);

        var events = await CollectAsync(service.SubmitInputAsync(thread.Id, [new TextContent("start")]));

        Assert.Equal(2, model.Calls);
        Assert.Equal(TurnStatus.Completed, thread.Turns[0].Status);
        Assert.DoesNotContain(thread.Turns[0].Items, item => item.AsUserMessage?.DeliveryMode == "guidance");
        Assert.Empty(thread.QueuedInputs);
        var blocked = Assert.Single(events, evt => evt.SystemEventPayload?.Kind == "guidanceBlocked").SystemEventPayload!;
        Assert.Equal("system.guidanceBlocked", blocked.MessageKey);
        Assert.Contains("steer denied", blocked.Params!["reason"]?.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunningInput_UserPromptSubmitContextFollowsTheSteer()
    {
        SessionService service = null!;
        SessionThread thread = null!;
        using var model = new HistoryBoundaryClient(async (call, messages, ct) =>
        {
            if (call == 1)
            {
                await service.SteerTurnAsync(thread.Id, thread.Turns[0].Id, [new TextContent("steer with context")], ct: ct);
                return HistoryBoundaryClient.Tool();
            }
            var steer = Array.FindIndex(messages.ToArray(), message => message.Text.Contains("steer with context", StringComparison.Ordinal));
            Assert.True(steer >= 0 && steer + 1 < messages.Count);
            Assert.Equal(ChatRole.User, messages[steer + 1].Role);
            Assert.Contains("STEER_HOOK_CONTEXT", messages[steer + 1].Text, StringComparison.Ordinal);
            return new TextContent("done");
        });
        await using var factory = CreateAgentFactory(model);
        service = CreatePromptHookService(factory, model, AdditionalContextCommand("STEER_HOOK_CONTEXT"));
        thread = await service.CreateThreadAsync(MakeIdentity());
        await service.RefreshThreadAgentAsync(thread.Id);

        await DrainAsync(service.SubmitInputAsync(thread.Id, [new TextContent("start")]));

        Assert.Equal(2, model.Calls);
        Assert.Single(thread.Turns[0].Items, item => item.AsUserMessage?.DeliveryMode == "guidance");
    }

    private SessionService CreatePromptHookService(AgentFactory factory, IChatClient model, string command) =>
        new(
            factory,
            new StreamingFunctionInvokingChatClient(model).AsAIAgent(),
            new SessionPersistenceService(new ThreadStore(_tempDir)),
            new SessionGate(),
            hookRunner: new HookRunner(new HooksFileConfig
            {
                Hooks =
                {
                    [nameof(HookEvent.UserPromptSubmit)] =
                    [
                        new HookMatcherGroup
                        {
                            Hooks = [new HookEntry { Type = "command", Command = command, Timeout = 10 }]
                        }
                    ]
                }
            }, _tempDir));

    private static string BlockAfterFirstRunCommand(string markerPath, string reason) =>
        OperatingSystem.IsWindows()
            ? $"if (Test-Path '{markerPath}') {{ [Console]::Error.WriteLine('{reason}'); exit 2 }} else {{ New-Item -ItemType File '{markerPath}' | Out-Null }}"
            : $"if [ -f '{markerPath}' ]; then printf '%s\\n' '{reason}' >&2; exit 2; else touch '{markerPath}'; fi";

    private static string AdditionalContextCommand(string context)
    {
        var json = "{\"hookSpecificOutput\":{\"hookEventName\":\"UserPromptSubmit\",\"additionalContext\":\"" + context + "\"}}";
        return OperatingSystem.IsWindows()
            ? $"Write-Output '{json}'"
            : $"printf '%s\\n' '{json}'";
    }

    private static void AssertResponsesUserMessageIds(IReadOnlyList<ChatMessage> messages)
    {
        var options = ResponsesToolSearchMapper.CreateResponseOptions("gpt-test", messages, new ChatOptions());
        using var body = JsonDocument.Parse(ModelReaderWriter.Write(options).ToString());
        var users = body.RootElement.GetProperty("input").EnumerateArray()
            .Where(item => item.GetProperty("type").GetString() == "message"
                && item.GetProperty("role").GetString() == "user").ToArray();
        Assert.NotEmpty(users);
        Assert.All(users, item => Assert.StartsWith("msg_", item.GetProperty("id").GetString()));
    }

    private sealed class HistoryBoundaryClient(
        Func<int, IReadOnlyList<ChatMessage>, CancellationToken, Task<AIContent>> respond) : IChatClient
    {
        public int Calls { get; private set; }
        public static FunctionCallContent Tool() => new("sleep-call", "clock__Sleep",
            new Dictionary<string, object?> { ["durationMs"] = 1 });
        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
            ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            yield return new ChatResponseUpdate(ChatRole.Assistant,
                [await respond(++Calls, messages.ToArray(), cancellationToken)]);
        }
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }
}
