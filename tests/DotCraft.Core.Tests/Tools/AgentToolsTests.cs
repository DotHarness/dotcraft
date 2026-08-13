using System.Text.Json;
using DotCraft.Agents;
using DotCraft.Tests.Sessions.Protocol.AppServer;
using TestableSessionService = DotCraft.Tests.Sessions.Protocol.AppServer.CoreTestableSessionService;
using DotCraft.Tools;
using Microsoft.Extensions.AI;
using DotCraft.Sessions;
using SessionIdentity = DotCraft.Sessions.SessionIdentity;
using SessionTurn = DotCraft.Sessions.SessionTurn;
using SubAgentThreadSource = DotCraft.Sessions.SubAgentThreadSource;
using ThreadSource = DotCraft.Sessions.ThreadSource;
using ThreadSpawnEdge = DotCraft.Sessions.ThreadSpawnEdge;
using Xunit;

namespace DotCraft.Tests.Tools;

public sealed class AgentToolsTests
{
    [Fact]
    public void SubAgentTools_ReturnJsonStrings()
    {
        var methods = new[]
        {
            nameof(AgentTools.SpawnAgent),
            nameof(AgentTools.SendMessage),
            nameof(AgentTools.FollowupTask),
            nameof(AgentTools.WaitAgent),
            nameof(AgentTools.ListAgents),
            nameof(AgentTools.CloseAgent)
        };

        foreach (var methodName in methods)
        {
            var method = typeof(AgentTools).GetMethod(methodName)!;
            Assert.Equal(typeof(Task<string>), method.ReturnType);
        }
    }

    [Fact]
    public void SpawnAgentFunction_ReturnSchemaIsString()
    {
        var agentTools = new AgentTools();
        var function = AIFunctionFactory.Create(agentTools.SpawnAgent);
        var returnSchema = Assert.NotNull(function.ReturnJsonSchema);
        var rawSchema = returnSchema.GetRawText();

        Assert.Contains("\"string\"", rawSchema, StringComparison.Ordinal);
        Assert.DoesNotContain("childThreadId", rawSchema, StringComparison.Ordinal);
        Assert.DoesNotContain("profileName", rawSchema, StringComparison.Ordinal);
        Assert.DoesNotContain("supportsSendInput", rawSchema, StringComparison.Ordinal);
    }

    [Fact]
    public void FollowupTaskFunction_ExposesDeliveryModeParameter()
    {
        var agentTools = new AgentTools();
        var function = AIFunctionFactory.Create(agentTools.FollowupTask);
        var deliveryMode = GetPropertySchema(function.JsonSchema, "deliveryMode");
        var rawSchema = deliveryMode.GetRawText();

        Assert.Equal("string", deliveryMode.GetProperty("type").GetString());
        Assert.Contains("queue", rawSchema, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("steer", rawSchema, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void WaitAgentFunction_DoesNotHardcodeLegacyDefaultTimeout()
    {
        var agentTools = new AgentTools();
        var function = AIFunctionFactory.Create(agentTools.WaitAgent);
        var timeoutMs = GetPropertySchema(function.JsonSchema, "timeoutMs");
        var rawSchema = timeoutMs.GetRawText();

        Assert.Contains("configured default", rawSchema, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("30000", rawSchema, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FollowupTaskFunction_AcceptsLowercaseSteerDeliveryMode()
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
            var child = await sessionService.CreateThreadAsync(
                new SessionIdentity
                {
                    WorkspacePath = tempDir,
                    UserId = "user",
                    ChannelName = SubAgentThreadOrigin.ChannelName,
                    ChannelContext = parent.Id
                },
                displayName: "Inspect",
                source: ThreadSource.ForSubAgent(new SubAgentThreadSource
                {
                    ParentThreadId = parent.Id,
                    ParentTurnId = "turn_parent",
                    RootThreadId = parent.Id,
                    Depth = 1,
                    AgentPath = "/root/inspect",
                    TaskName = "inspect",
                    AgentNickname = "Inspect",
                    RuntimeType = NativeSubAgentRuntime.RuntimeTypeName,
                    SupportsSendMessage = true,
                    SupportsFollowupTask = true,
                    SupportsClose = true
                }));
            child.Turns.Add(new SessionTurn
            {
                Id = "turn_active",
                ThreadId = child.Id,
                Status = TurnStatus.Running,
                StartedAt = DateTimeOffset.UtcNow
            });
            await store.SaveThreadAsync(child);
            await sessionService.UpsertThreadSpawnEdgeAsync(new ThreadSpawnEdge
            {
                ParentThreadId = parent.Id,
                ChildThreadId = child.Id,
                ParentTurnId = "turn_parent",
                Depth = 1,
                AgentPath = "/root/inspect",
                TaskName = "inspect",
                AgentNickname = "Inspect",
                RuntimeType = NativeSubAgentRuntime.RuntimeTypeName,
                SupportsSendMessage = true,
                SupportsFollowupTask = true,
                SupportsClose = true,
                Status = ThreadSpawnEdgeStatus.Open
            });
            using var scope = SubAgentSessionScope.Set(new SubAgentSessionContext
            {
                SessionService = sessionService,
                ParentThread = parent,
                ParentTurnId = "turn_parent",
                RootThreadId = parent.Id,
                Depth = 0
            });
            var function = AIFunctionFactory.Create(new AgentTools().FollowupTask);

            var result = await function.InvokeAsync(new AIFunctionArguments
            {
                ["target"] = "/root/inspect",
                ["message"] = "continue work",
                ["deliveryMode"] = "steer"
            });

            var resultJson = result is JsonElement element ? element.GetString() : Assert.IsType<string>(result);
            Assert.False(string.IsNullOrWhiteSpace(resultJson));
            using var doc = JsonDocument.Parse(resultJson!);
            Assert.Equal("guidancePending", doc.RootElement.GetProperty("status").GetString());
            child = await sessionService.GetThreadAsync(child.Id);
            var queued = Assert.Single(child.QueuedInputs);
            Assert.Equal("guidancePending", queued.Status);
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
                // Best-effort cleanup.
            }
        }
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
                // Best-effort cleanup.
            }
        }
    }

    private static JsonElement GetPropertySchema(JsonElement schema, string propertyName) =>
        schema.GetProperty("properties").GetProperty(propertyName);
}
