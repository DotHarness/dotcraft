using System.Text.Json;
using Contract = DotCraft.Protocol.AppServer;
using DotCraft.Sessions.Wire;
using Xunit;

namespace DotCraft.Core.Tests.Protocol.AppServer;

public sealed class AppServerWireDtoSerializationTests
{
    [Fact]
    public void RpcEmpty_SerializesAsEmptyObject()
    {
        Assert.Equal("{}", Serialize(new DotCraft.Protocol.RpcEmpty()));
    }

    [Fact]
    public void ChannelRejectedSystemEventNotification_PreservesCurrentWireShape()
    {
        var json = Serialize(new Contract.SystemEventNotification
        {
            Kind = "channelRejected",
            ChannelName = "test-channel",
            Message = "Channel is not available."
        });

        Assert.Equal(
            "{\"kind\":\"channelRejected\",\"channelName\":\"test-channel\",\"message\":\"Channel is not available.\"}",
            json);
    }

    [Fact]
    public void ItemDeltaNotification_OmitsUnusedVariantFieldsAndKeepsWireOrder()
    {
        var json = Serialize(new Contract.ItemDeltaNotification
        {
            ThreadId = "thread-1",
            TurnId = "turn-1",
            ItemId = "item-1",
            Delta = "chunk"
        });

        Assert.Equal(
            "{\"threadId\":\"thread-1\",\"turnId\":\"turn-1\",\"itemId\":\"item-1\",\"delta\":\"chunk\"}",
            json);
    }

    [Fact]
    public void SystemJobResultNotification_PreservesCurrentOptionalFieldShape()
    {
        var json = Serialize(new Contract.SystemJobResultNotification
        {
            Source = "cron",
            JobId = "job-1",
            Result = "done",
            TokenUsage = new Contract.SystemJobTokenUsage { InputTokens = 4, OutputTokens = 2 }
        });

        Assert.Equal(
            "{\"source\":\"cron\",\"jobId\":\"job-1\",\"result\":\"done\",\"tokenUsage\":{\"inputTokens\":4,\"outputTokens\":2}}",
            json);
    }

    [Fact]
    public void AppConnectionRequestGetResult_MatchesExistingServerWire()
    {
        var json = Serialize(new Contract.AppConnectionRequestGetResult
        {
            ConnectionRequestId = "request-1",
            AppId = "app-1",
            DisplayName = "Example",
            DeveloperName = "Example Developer",
            UserId = "user-1",
            ExpiresAt = DateTimeOffset.Parse("2026-07-31T01:02:03Z")
        });

        Assert.Equal(
            "{\"connectionRequestId\":\"request-1\",\"appId\":\"app-1\",\"displayName\":\"Example\",\"developerName\":\"Example Developer\",\"userId\":\"user-1\",\"expiresAt\":\"2026-07-31T01:02:03+00:00\"}",
            json);
    }

    [Fact]
    public void AppBindingRequestedNotification_RepresentsBothExistingShapesWithoutExtraFields()
    {
        var appRequest = Serialize(new Contract.AppBindingRequestedNotification
        {
            BindingRequestId = "request-1",
            BindingId = "binding-1",
            ThreadId = "thread-1",
            AppId = "app-1"
        });
        var socialRequest = Serialize(new Contract.AppBindingRequestedNotification
        {
            BindingRequestId = "request-2",
            BindingId = "binding-2",
            Code = "ABC123",
            ChannelName = "test-channel",
            ExpiresAt = DateTimeOffset.Parse("2026-07-31T01:02:03Z")
        });

        Assert.Equal(
            "{\"bindingRequestId\":\"request-1\",\"bindingId\":\"binding-1\",\"threadId\":\"thread-1\",\"appId\":\"app-1\"}",
            appRequest);
        Assert.Equal(
            "{\"bindingRequestId\":\"request-2\",\"bindingId\":\"binding-2\",\"code\":\"ABC123\",\"channelName\":\"test-channel\",\"expiresAt\":\"2026-07-31T01:02:03+00:00\"}",
            socialRequest);
    }

    [Fact]
    public void McpRuntimeToolWire_PreservesOpaqueSchemas()
    {
        using var input = JsonDocument.Parse("{\"type\":\"object\",\"x-extra\":true}");
        using var output = JsonDocument.Parse("{\"type\":\"string\"}");
        var json = Serialize(new Contract.McpRuntimeTool
        {
            Name = "lookup",
            Description = "Looks up a value",
            InputSchema = input.RootElement.Clone(),
            OutputSchema = output.RootElement.Clone()
        });

        Assert.Equal(
            "{\"name\":\"lookup\",\"description\":\"Looks up a value\",\"inputSchema\":{\"type\":\"object\",\"x-extra\":true},\"outputSchema\":{\"type\":\"string\"}}",
            json);
    }

    [Fact]
    public void NodeReplEvaluateParams_OmitsNullTurnIdsAtBothLevels()
    {
        var json = Serialize(new Contract.NodeReplEvaluateParams
        {
            ThreadId = "thread-1",
            EvaluationId = "eval-1",
            BrowserSession = new Contract.NodeReplBrowserSessionParams
            {
                ProtocolVersion = 1,
                SessionId = "session-1",
                ThreadId = "thread-1",
                EvaluationId = "eval-1"
            },
            Code = "1 + 1",
            TimeoutMs = 30_000
        });

        Assert.Equal(
            "{\"threadId\":\"thread-1\",\"evaluationId\":\"eval-1\",\"browserSession\":{\"protocolVersion\":1,\"sessionId\":\"session-1\",\"threadId\":\"thread-1\",\"evaluationId\":\"eval-1\"},\"code\":\"1 \\u002B 1\",\"timeoutMs\":30000}",
            json);
    }

    [Fact]
    public void FirstPartyExtensionDtos_PreserveStablePayloads()
    {
        Assert.Equal(
            "{\"ok\":true}",
            Serialize(new Contract.AutomationTaskDeleteResult { Ok = true }));
        Assert.Equal(
            "{\"team\":true,\"missions\":true}",
            Serialize(new Contract.TeamsCapabilities { Team = true, Missions = true }));
        Assert.Equal(
            "{\"reason\":\"missionCreated\",\"missionId\":\"mission-1\"}",
            Serialize(new Contract.TeamsTeamChangedNotification
            {
                Reason = "missionCreated",
                MissionId = "mission-1"
            }));
    }

    private static string Serialize<T>(T value) =>
        JsonSerializer.Serialize(value, SessionWireJsonOptions.Default);
}
