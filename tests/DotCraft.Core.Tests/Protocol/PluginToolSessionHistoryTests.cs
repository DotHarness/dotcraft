using System.Text.Json.Nodes;
using DotCraft.Plugins;
using DotCraft.Protocol;
using Microsoft.Extensions.AI;

namespace DotCraft.Core.Tests.Protocol;

public sealed class PluginToolSessionHistoryTests
{
    [Fact]
    public void StandardPluginPair_ReplaysOriginalProviderCallIdWithoutClientOnlyData()
    {
        var turn = new SessionTurn
        {
            Id = "turn_plugin",
            ThreadId = "thread_plugin",
            Status = TurnStatus.Completed,
            StartedAt = DateTimeOffset.UtcNow,
            CompletedAt = DateTimeOffset.UtcNow,
            Items =
            [
                new SessionItem
                {
                    Id = "item_call",
                    TurnId = "turn_plugin",
                    Type = ItemType.ToolCall,
                    Status = ItemStatus.Completed,
                    Payload = new ToolCallPayload
                    {
                        Namespace = "external_channel",
                        ToolName = "send_document",
                        ProviderCallName = "external_channel__send_document",
                        ToolDefinitionId = "PluginNative:external-channel:telegram:send_document",
                        Source = new ToolSourceProvenancePayload
                        {
                            Kind = "pluginNative",
                            SourceId = "external-channel:telegram",
                            SourceToolId = "send_document",
                            PluginId = "external-channel:telegram",
                            FunctionId = "send_document"
                        },
                        CallId = "provider-call-42",
                        Arguments = new JsonObject { ["path"] = "report.pdf" }
                    }
                },
                new SessionItem
                {
                    Id = "item_result",
                    TurnId = "turn_plugin",
                    Type = ItemType.ToolResult,
                    Status = ItemStatus.Completed,
                    Payload = new ToolResultPayload
                    {
                        CallId = "provider-call-42",
                        Success = true,
                        Result = "sent",
                        ContentItems = [new PluginFunctionContentItem { Type = "text", Text = "sent" }],
                        StructuredContent = new JsonObject { ["messageId"] = "private-id" },
                        Meta = new JsonObject { ["token"] = "private-token" }
                    }
                }
            ]
        };

        var history = ThreadStore.BuildModelVisibleHistoryFromTurn(turn);

        var call = Assert.Single(history.SelectMany(message => message.Contents).OfType<FunctionCallContent>());
        Assert.Equal("provider-call-42", call.CallId);
        Assert.Equal("external_channel__send_document", call.Name);
        var result = Assert.Single(history.SelectMany(message => message.Contents).OfType<FunctionResultContent>());
        Assert.Equal("provider-call-42", result.CallId);
        var text = Assert.IsType<string>(result.Result);
        Assert.Equal("sent", text);
        Assert.DoesNotContain("private-id", text, StringComparison.Ordinal);
        Assert.DoesNotContain("private-token", text, StringComparison.Ordinal);
        Assert.DoesNotContain(turn.Items, item => item.Type == ItemType.PluginFunctionCall);
    }
}
