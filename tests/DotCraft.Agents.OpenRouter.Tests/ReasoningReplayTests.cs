using System.Text.Json.Nodes;
using DotCraft.Agents;
using DotCraft.Persistence;
using DotCraft.Sessions;
using Microsoft.Extensions.AI;
using Xunit;

namespace DotCraft.Tests.OpenRouter;

public sealed class ReasoningReplayTests
{
    [Theory]
    [InlineData("anthropic", false)]
    [InlineData("openai-chat-completions", false)]
    [InlineData("openai-responses", false)]
    [InlineData("anthropic", true)]
    [InlineData("openai-chat-completions", true)]
    [InlineData("openai-responses", true)]
    public async Task ToolRoundTripPreservesOpaqueReasoningAfterColdHistoryRead(string protocol, bool streaming)
    {
        using var server = new RequestTests.LocalServer();
        var runtime = RoutingTests.Runtime() with { Protocol = protocol, ConfiguredProtocol = protocol,
            EndPoint = server.Root, ConfiguredEndPoint = server.Root, Model = "anthropic/claude-opus-5.5" };
        var provider = RoutingTests.Create(runtime);
        using var client = provider.CreateChatClient(runtime);
        var first = server.Reply(protocol, streaming ? ProtocolEvents.Build(protocol, Response(protocol)) : Response(protocol), streaming);
        var options = new ChatOptions { Reasoning = new ReasoningOptions { Effort = ReasoningEffort.Medium, Output = ReasoningOutput.Full } };
        ChatMessage[] initial = [new(ChatRole.User, "inspect")];
        ChatResponse response;
        if (streaming)
        {
            var updates = new List<ChatResponseUpdate>();
            await foreach (var update in client.GetStreamingResponseAsync(initial, options)) updates.Add(update);
            Assert.Contains(updates, update => update.AdditionalProperties?.GetValueOrDefault("openrouter.costUsd") is decimal cost && cost == 0.00001m);
            response = updates.ToAgentResponse();
        }
        else
        {
            response = await client.GetResponseAsync(initial, options);
            Assert.Equal(0.00001m, response.AdditionalProperties!["openrouter.costUsd"]);
        }
        await first;
        var restored = await ColdHistoryAsync(response.Messages);
        var history = new List<ChatMessage> { new(ChatRole.User, "inspect") };
        history.AddRange(restored);
        history.Add(new ChatMessage(ChatRole.Tool, [new FunctionResultContent("call_1", "ok")]));
        var second = server.Reply(protocol);
        await client.GetResponseAsync(history, options);
        var sent = (await second).Body;
        if (protocol == "anthropic")
        {
            var contents = sent["messages"]!.AsArray().OfType<JsonObject>().SelectMany(message => message["content"] as JsonArray ?? []);
            Assert.Contains(contents, content => content?["type"]?.GetValue<string>() == "thinking" && content["signature"]?.GetValue<string>() == "opaque-signature");
        }
        else if (protocol == "openai-chat-completions")
        {
            var message = sent["messages"]!.AsArray().OfType<JsonObject>().Single(item => item["role"]?.GetValue<string>() == "assistant");
            Assert.Equal("opaque-reasoning-推理", message["reasoning_details"]![0]!["data"]!.GetValue<string>());
            Assert.True(message["reasoning_details"]![0]!["provider_extension"]!["preserve"]!.GetValue<bool>());
        }
        else Assert.Contains(sent["input"]!.AsArray(), item => item?["type"]?.GetValue<string>() == "reasoning"
            && item["encrypted_content"]?.GetValue<string>() == "opaque-reasoning");
    }

    internal static string Response(string protocol) => protocol switch
    {
        "anthropic" => """{"id":"msg_1","type":"message","role":"assistant","model":"anthropic/claude-opus-5.5","content":[{"type":"thinking","thinking":"inspect","signature":"opaque-signature"},{"type":"tool_use","id":"call_1","name":"probe","input":{}}],"stop_reason":"tool_use","usage":{"input_tokens":5,"output_tokens":2},"openrouter_metadata":{"usage":{"cost":0.00001}}}""",
        "openai-chat-completions" => """{"id":"chatcmpl_1","object":"chat.completion","created":1,"model":"anthropic/claude-opus-5.5","choices":[{"index":0,"message":{"role":"assistant","content":null,"reasoning_details":[{"type":"reasoning.encrypted","data":"opaque-reasoning-推理","format":"anthropic-claude-v1","index":0,"provider_extension":{"preserve":true}}],"tool_calls":[{"id":"call_1","type":"function","function":{"name":"probe","arguments":"{}"}}]},"finish_reason":"tool_calls"}],"usage":{"prompt_tokens":5,"completion_tokens":2,"total_tokens":7,"cost":0.00001}}""",
        _ => """{"id":"resp_1","object":"response","created_at":1,"status":"completed","model":"anthropic/claude-opus-5.5","output":[{"id":"rs_1","type":"reasoning","encrypted_content":"opaque-reasoning","summary":[]},{"id":"fc_1","type":"function_call","call_id":"call_1","name":"probe","arguments":"{}","status":"completed"}],"usage":{"input_tokens":5,"output_tokens":2,"total_tokens":7,"cost":0.00001}}"""
    };

    private static async Task<IReadOnlyList<ChatMessage>> ColdHistoryAsync(IList<ChatMessage> messages)
    {
        var directory = Path.Combine(Path.GetTempPath(), "openrouter_history_" + Guid.NewGuid().ToString("N"));
        var store = new ThreadStore(directory);
        try
        {
            await store.SaveThreadAsync(new SessionThread { Id = "thread", WorkspacePath = directory, OriginChannel = "test",
                Turns = [new SessionTurn { Id = "turn", ThreadId = "thread", Status = TurnStatus.Completed, StartedAt = DateTimeOffset.UtcNow }] });
            await store.AppendCompactionCheckpointAsync("thread", "turn", messages.ToArray(), "auto", "partial", 1000, 100);
            await store.FlushAndCloseAsync();
            var cold = new ThreadStore(directory);
            try { return await cold.LoadModelHistoryAsync((await cold.LoadThreadAsync("thread"))!); }
            finally { await cold.FlushAndCloseAsync(); }
        }
        finally
        {
            await store.FlushAndCloseAsync();
            try { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
            catch (IOException) { }
        }
    }
}
