using System.Text.Json.Nodes;
using DotCraft.Agents;
using DotCraft.Context;
using DotCraft.Tools;
using Xunit;

namespace DotCraft.Tests.Tools.Architecture;

public sealed partial class RemoteToolHostCoreTests
{
    [Fact]
    public void Routing_projection_preserves_the_business_schema()
    {
        var definition = Definition("read", """
            {
              "type": "object",
              "properties": {
                "path": { "type": "string", "minLength": 1 },
                "options": {
                  "type": "object",
                  "properties": { "target": { "type": "string", "default": "artifact" } },
                  "required": ["target"],
                  "additionalProperties": false
                }
              },
              "required": ["path"],
              "additionalProperties": false
            }
            """);
        var nativeSchema = definition.InputSchema.GetRawText();
        var registration = Assert.Single(RemoteToolRegistrationRouter.Wrap(
            [Registration(definition)], new FakeRemoteClient()));
        var projected = JsonNode.Parse(registration.Definition.InputSchema.GetRawText())!.AsObject();
        var properties = projected["properties"]!.AsObject();
        var target = properties["target"]!.AsObject();

        Assert.Equal("string", target["type"]!.GetValue<string>());
        Assert.Equal(new[] { "local", "remote" }, target["enum"]!.AsArray().Select(value => value!.GetValue<string>()));
        properties.Remove("target");
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(nativeSchema), projected));
        Assert.Equal(nativeSchema, definition.InputSchema.GetRawText());
    }

    [Fact]
    public void Routing_projection_rejects_a_native_target_parameter()
    {
        var definition = Definition("read", """
            {"type":"object","properties":{"target":{"type":"string"}},"required":["target"]}
            """);

        var error = Assert.Throws<InvalidOperationException>(() =>
            RemoteToolRegistrationRouter.Wrap([Registration(definition)], new FakeRemoteClient()));

        Assert.Contains(definition.Id.ToString(), error.Message, StringComparison.Ordinal);
        Assert.Contains("target", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Replanning_keeps_the_native_contract_and_a_stable_model_schema()
    {
        var definition = Definition("read", """
            {
              "type":"object",
              "properties":{"options":{"type":"object","properties":{"target":{"type":"string"}}}},
              "required":["options"],
              "additionalProperties":false
            }
            """);
        var native = Registration(definition);
        var client = new FakeRemoteClient();
        var builder = new EffectiveToolSnapshotBuilder();
        var wrapped = RemoteToolRegistrationRouter.Wrap([native], client);
        var fingerprint = PromptRequestFingerprints.ComputeToolFingerprint(
            AgentFactory.ProjectSnapshotTools(builder.Build(wrapped, 1)));

        client.SetRoute("thread-1");
        var connected = RemoteToolRegistrationRouter.Wrap(wrapped, client);
        var snapshot = builder.Build(connected, 2);
        client.UpdateRemoteToolSnapshot("thread-1", snapshot, "agent");
        Assert.Same(wrapped[0], connected[0]);
        Assert.Same(definition, Assert.Single(client.Definitions));
        Assert.Equal(fingerprint, PromptRequestFingerprints.ComputeToolFingerprint(AgentFactory.ProjectSnapshotTools(snapshot)));

        var arguments = new JsonObject
        {
            ["target"] = "remote",
            ["options"] = new JsonObject { ["target"] = "artifact" }
        };
        var result = await new ToolDispatcher().DispatchAsync(snapshot, definition.Name, arguments,
            new ToolInvocationRequest("thread-1", "turn-1", "call-1", ToolInvocationAudience.Model));

        Assert.True(result.Success);
        Assert.NotNull(client.LastInvocation);
        var invocation = client.LastInvocation.Value;
        Assert.Same(definition, invocation.Definition);
        Assert.Equal(RemoteToolContractHasher.Compute(definition), invocation.ContractHash);
        Assert.False(invocation.Arguments.ContainsKey("target"));
        Assert.Equal("artifact", invocation.Arguments["options"]!["target"]!.GetValue<string>());
        Assert.Equal("remote", arguments["target"]!.GetValue<string>());

        await client.DisconnectAsync("thread-1");
        var disconnected = RemoteToolRegistrationRouter.Wrap([native], client);
        Assert.Equal(fingerprint, PromptRequestFingerprints.ComputeToolFingerprint(
            AgentFactory.ProjectSnapshotTools(builder.Build(disconnected, 3))));
    }
}
