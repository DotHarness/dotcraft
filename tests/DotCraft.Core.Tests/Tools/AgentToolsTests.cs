using System.Text.Json;
using TestableSessionService = DotCraft.Tests.Sessions.Protocol.AppServer.CoreTestableSessionService;
using DotCraft.Tools;
using Microsoft.Extensions.AI;
using DotCraft.Sessions;
using SessionIdentity = DotCraft.Sessions.SessionIdentity;
using Xunit;

namespace DotCraft.Tests.Tools;

public sealed class AgentToolsTests
{
    [Fact]
    public void SpawnAgentFunction_ExposesOptionalModelOverrides()
    {
        var function = AIFunctionFactory.Create(new AgentTools().SpawnAgent);

        Assert.Contains(
            "string",
            GetPropertySchema(function.JsonSchema, "model").GetProperty("type")
                .EnumerateArray()
                .Select(static item => item.GetString()));
        Assert.Contains(
            "string",
            GetPropertySchema(function.JsonSchema, "reasoningEffort").GetProperty("type")
                .EnumerateArray()
                .Select(static item => item.GetString()));
        var required = function.JsonSchema.GetProperty("required")
            .EnumerateArray()
            .Select(static item => item.GetString())
            .ToArray();
        Assert.DoesNotContain("model", required);
        Assert.DoesNotContain("reasoningEffort", required);
    }

    [Fact]
    public void SendMessageFunction_TakesOnlyTargetAndMessage()
    {
        var function = AIFunctionFactory.Create(new AgentTools().SendMessage);

        Assert.Equal(
            ["target", "message"],
            function.JsonSchema.GetProperty("properties").EnumerateObject().Select(static property => property.Name).ToArray());
    }

    [Fact]
    public async Task SpawnAgent_ReturnsCompactJsonString()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"agent_tools_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var store = new ThreadStore(tempDir);
            var sessionService = new TestableSessionService(store);
            var parent = await sessionService.CreateThreadAsync(new SessionIdentity
            {
                WorkspacePath = tempDir,
                UserId = "user",
                ChannelName = "desktop"
            });
            using var scope = SubAgentSessionScope.Set(new SubAgentSessionContext
            {
                SessionService = sessionService,
                ParentThread = parent,
                ParentTurnId = "turn_parent",
                RootThreadId = parent.Id,
                Depth = 0
            });

            var resultJson = await new AgentTools().SpawnAgent(
                message: "inspect code",
                taskName: "inspect",
                agentNickname: "Inspect",
                profile: "native",
                cancellationToken: CancellationToken.None);

            using var doc = JsonDocument.Parse(resultJson);
            var root = doc.RootElement;
            Assert.Equal("running", root.GetProperty("status").GetString());
            Assert.Equal("/root/inspect", root.GetProperty("agentPath").GetString());
            Assert.Equal("inspect", root.GetProperty("taskName").GetString());
            Assert.Equal("Inspect", root.GetProperty("agentNickname").GetString());
            Assert.Equal("native", root.GetProperty("profileName").GetString());
            Assert.Equal("native", root.GetProperty("runtimeType").GetString());
            Assert.True(root.GetProperty("supportsSendMessage").GetBoolean());
            Assert.True(root.GetProperty("supportsFollowupTask").GetBoolean());
            Assert.False(root.TryGetProperty("childThreadId", out _));
            Assert.False(root.TryGetProperty("supportsSendInput", out _));
        }
        finally
        {
            try
            {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, recursive: true);
            }
            catch
            {
            }
        }
    }

    private static JsonElement GetPropertySchema(JsonElement schema, string propertyName) =>
        schema.GetProperty("properties").GetProperty(propertyName);
}
