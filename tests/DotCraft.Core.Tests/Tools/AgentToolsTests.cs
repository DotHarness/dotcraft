using System.Text.Json;
using DotCraft.Protocol;
using DotCraft.Tests.Sessions.Protocol.AppServer;
using DotCraft.Tools;
using Microsoft.Extensions.AI;

namespace DotCraft.Tests.Tools;

public sealed class AgentToolsTests
{
    [Fact]
    public void SubAgentTools_ReturnJsonStrings()
    {
        var methods = new[]
        {
            nameof(AgentTools.SpawnAgent),
            nameof(AgentTools.SendInput),
            nameof(AgentTools.WaitAgent),
            nameof(AgentTools.ResumeAgent),
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
                "inspect code",
                agentNickname: "Inspect",
                profile: "native",
                cancellationToken: CancellationToken.None);

            using var doc = JsonDocument.Parse(resultJson);
            var root = doc.RootElement;
            Assert.Equal("running", root.GetProperty("status").GetString());
            Assert.Equal("Inspect", root.GetProperty("agentNickname").GetString());
            Assert.Equal("native", root.GetProperty("profileName").GetString());
            Assert.Equal("native", root.GetProperty("runtimeType").GetString());
            Assert.True(root.GetProperty("supportsSendInput").GetBoolean());
            Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("childThreadId").GetString()));
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
}
