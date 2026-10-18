using DotCraft.Agents;
using DotCraft.Sessions;
using Microsoft.Extensions.AI;
using Xunit;

namespace DotCraft.Tests.Sessions.Protocol;

public sealed partial class SessionServiceAgentInstructionsTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ClientHistory_EachTurnReceivesInstructionsAfterHistoryReplacement(bool coldResume)
    {
        File.WriteAllText(Path.Combine(_workspace, "AGENTS.md"), "client project convention");
        var recorder = new RecordingChatClient();
        await using var factory = CreateAgentFactory();
        var service = new SessionService(factory, recorder.AsAIAgent(), _persistence, new SessionGate());
        var thread = await service.CreateThreadAsync(MakeIdentity(), historyMode: HistoryMode.Client);

        await DrainAsync(service.SubmitInputAsync(thread.Id, [new TextContent("first")],
            messages: [new ChatMessage(ChatRole.User, "client context")]));
        var firstInstructions = Assert.Single(recorder.LastMessages, AgentInstructionsHistory.IsInstructions);
        Assert.Equal(ChatRole.User, firstInstructions.Role);
        Assert.Contains("client project convention", firstInstructions.Text);

        await using var coldFactory = coldResume ? CreateAgentFactory() : null;
        if (coldFactory != null)
        {
            service = new SessionService(coldFactory, recorder.AsAIAgent(), _persistence, new SessionGate());
            await service.ResumeThreadAsync(thread.Id);
        }

        await DrainAsync(service.SubmitInputAsync(thread.Id, [new TextContent("second")],
            messages:
            [
                new ChatMessage(ChatRole.User, "client context"),
                new ChatMessage(ChatRole.User, "first"),
                new ChatMessage(ChatRole.Assistant, "ok")
            ]));
        var secondInstructions = Assert.Single(recorder.LastMessages, AgentInstructionsHistory.IsInstructions);
        Assert.Equal(ChatRole.User, secondInstructions.Role);
        Assert.Equal(firstInstructions.Text, secondInstructions.Text);
    }

    [Fact]
    public async Task ClientHistory_ToolContinuationDoesNotRepeatInstructions()
    {
        File.WriteAllText(Path.Combine(_workspace, "AGENTS.md"), "client tool convention");
        var recorder = new RecordingChatClient();
        await using var factory = CreateAgentFactory();
        using var invokingClient = new StreamingFunctionInvokingChatClient(recorder);
        var agent = invokingClient.AsAIAgent(new ChatOptions
        {
            Tools = [AIFunctionFactory.Create(() => "status result", name: "GetStatus")]
        });
        var service = new SessionService(factory, agent, _persistence, new SessionGate());
        var thread = await service.CreateThreadAsync(MakeIdentity(), historyMode: HistoryMode.Client);
        await DrainAsync(service.SubmitInputAsync(thread.Id, [new TextContent("first")],
            messages: [new ChatMessage(ChatRole.User, "client context")]));

        recorder.Requests.Clear();
        recorder.ToolNextResponse = true;
        await DrainAsync(service.SubmitInputAsync(thread.Id, [new TextContent("check status")],
            messages: [new ChatMessage(ChatRole.User, "first"), new ChatMessage(ChatRole.Assistant, "ok")]));

        Assert.Equal(2, recorder.Requests.Count);
        Assert.All(recorder.Requests, request =>
        {
            var instructions = Assert.Single(request, AgentInstructionsHistory.IsInstructions);
            Assert.Equal(ChatRole.User, instructions.Role);
            Assert.Contains("client tool convention", instructions.Text);
        });
        Assert.Contains(recorder.Requests[^1], message => message.Contents.OfType<FunctionResultContent>().Any());
    }
}
