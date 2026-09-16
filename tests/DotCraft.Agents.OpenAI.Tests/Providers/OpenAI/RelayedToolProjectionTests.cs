using System.ClientModel.Primitives;
using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Agents;
using DotCraft.Tools;
using Microsoft.Extensions.AI;
using Xunit;

#pragma warning disable OPENAI001, MAAI001, MEAI001

namespace DotCraft.Tests.Agents;

public sealed class RelayedToolProjectionTests
{
    [Fact]
    public void A_relayed_declaration_keeps_the_namespace_a_provider_projects()
    {
        var tools = Project(new RelayedTool(
            new ToolName("universe", "SendMessage"),
            "universe__SendMessage",
            "Reply to the conversation."));

        var group = Assert.Single(tools.AsArray())!.AsObject();
        Assert.Equal("namespace", group["type"]!.GetValue<string>());
        Assert.Equal("universe", group["name"]!.GetValue<string>());
        Assert.Equal("SendMessage", group["tools"]![0]!["name"]!.GetValue<string>());
    }

    [Fact]
    public void A_relayed_declaration_without_a_namespace_stays_top_level()
    {
        var tools = Project(new RelayedTool(new ToolName(null, "SendMessage"), "SendMessage"));

        var tool = Assert.Single(tools.AsArray())!.AsObject();
        Assert.Equal("function", tool["type"]!.GetValue<string>());
        Assert.Equal("SendMessage", tool["name"]!.GetValue<string>());
    }

    /// <summary>What the worker reads off a real tool has to rebuild into the same identity.</summary>
    [Fact]
    public void The_identity_a_relay_reads_round_trips_through_a_declaration()
    {
        var original = AIFunctionFactory.Create(() => string.Empty, name: "Lookup");

        var relayed = new RelayedTool(
            RelayedTool.CanonicalNameOf(original),
            RelayedTool.ProviderFlatNameOf(original));

        Assert.Equal(RelayedTool.CanonicalNameOf(original), RelayedTool.CanonicalNameOf(relayed));
        Assert.Equal(RelayedTool.ProviderFlatNameOf(original), RelayedTool.ProviderFlatNameOf(relayed));
    }

    private static JsonNode Project(AITool tool)
    {
        var options = ResponsesToolSearchMapper.CreateResponseOptions(
            "gpt-test",
            [new ChatMessage(ChatRole.User, "hello")],
            new ChatOptions { Tools = [tool] });
        var body = JsonNode.Parse(ModelReaderWriter.Write(options).ToString())!.AsObject();
        return body["tools"]!;
    }
}
