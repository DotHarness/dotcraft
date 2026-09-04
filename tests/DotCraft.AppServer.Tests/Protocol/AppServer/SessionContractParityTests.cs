using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using DotCraft.AppBinding;
using Contract = DotCraft.Protocol.AppServer;
using DotCraft.AppServer;
using DotCraft.Sessions;
using DotCraft.Sessions.Wire;
using QueuedTurnInput = DotCraft.Sessions.QueuedTurnInput;
using AgentMessagePayload = DotCraft.Sessions.AgentMessagePayload;
using ContextUsageSnapshot = DotCraft.Sessions.Wire.ContextUsageSnapshot;
using McpAppViewHintSnapshot = DotCraft.Sessions.Wire.McpAppViewHintSnapshot;
using SenderContext = DotCraft.Sessions.SenderContext;
using SocialChannelBoundBy = DotCraft.AppBinding.SocialChannelBoundBy;
using SocialChannelTarget = DotCraft.AppBinding.SocialChannelTarget;
using SubAgentThreadSource = DotCraft.Sessions.SubAgentThreadSource;
using ThreadConfiguration = DotCraft.Sessions.ThreadConfiguration;
using ThreadGoalSnapshot = DotCraft.Sessions.ThreadGoalSnapshot;
using ThreadOriginAppSnapshot = DotCraft.Sessions.ThreadOriginAppSnapshot;
using ThreadOriginPresentationSnapshot = DotCraft.Sessions.ThreadOriginPresentationSnapshot;
using ThreadSource = DotCraft.Sessions.ThreadSource;
using ThreadWorktreeDirtyHandoffInfo = DotCraft.Sessions.ThreadWorktreeDirtyHandoffInfo;
using ThreadWorktreeInfo = DotCraft.Sessions.ThreadWorktreeInfo;
using TokenUsageInfo = DotCraft.Sessions.TokenUsageInfo;
using TurnInitiatorContext = DotCraft.Sessions.TurnInitiatorContext;
using Xunit;

namespace DotCraft.Tests.Protocol.AppServer;

public sealed class SessionContractParityTests
{
    [Theory]
    [InlineData(typeof(SessionWireThread), typeof(Contract.SessionThread))]
    [InlineData(typeof(SessionWireTurn), typeof(Contract.SessionTurn))]
    [InlineData(typeof(SessionWireItem), typeof(Contract.SessionItem))]
    public void Contract_Session_Snapshots_Explicitly_Declare_Every_Wire_Field(Type wireType, Type contractType)
    {
        var contractFields = SerializedProperties(contractType).ToHashSet(StringComparer.Ordinal);
        var missing = SerializedProperties(wireType)
            .Where(field => !contractFields.Contains(field))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(missing);
    }

    [Fact]
    public void Canonical_Payload_Contracts_Declare_The_Complete_Runtime_Payload_Shape()
    {
        var runtimeAssembly = typeof(AgentMessagePayload).Assembly;
        foreach (var registration in DotCraft.Protocol.SessionItemPayloadCatalog.All)
        {
            var runtimeType = runtimeAssembly.GetType($"DotCraft.Sessions.{registration.PayloadType.Name}");
            Assert.NotNull(runtimeType);
            Assert.Equal(
                SerializedProperties(runtimeType!).Order(StringComparer.Ordinal),
                SerializedProperties(registration.PayloadType).Order(StringComparer.Ordinal));
        }
    }

    [Fact]
    public void Contract_Session_Snapshots_Serialize_Like_The_Runtime_Wire()
    {
        var item = new SessionWireItem
        {
            Id = "item_001",
            TurnId = "turn_001",
            Type = ItemType.AgentMessage,
            Status = ItemStatus.Completed,
            CreatedAt = DateTimeOffset.Parse("2026-08-03T01:02:03Z"),
            CompletedAt = DateTimeOffset.Parse("2026-08-03T01:02:04Z"),
            PayloadKind = "agentMessage",
            Payload = new AgentMessagePayload { Text = "done" },
            McpApp = new McpAppViewHintSnapshot { Available = true }
        };
        var turn = new SessionWireTurn
        {
            Id = "turn_001",
            ThreadId = "thread_001",
            Status = TurnStatus.Completed,
            StartedAt = DateTimeOffset.Parse("2026-08-03T01:02:03Z"),
            CompletedAt = DateTimeOffset.Parse("2026-08-03T01:02:04Z"),
            OriginChannel = "telegram",
            Initiator = new TurnInitiatorContext
            {
                ChannelName = "telegram",
                UserId = "user_001",
                UserName = "Ada",
                UserRole = "admin",
                ChannelContext = "chat_001",
                GroupId = "group_001"
            },
            TokenUsage = new TokenUsageInfo
            {
                InputTokens = 10,
                OutputTokens = 2,
                TotalTokens = 12,
                LlmCallCount = 1
            },
            Items = [item]
        };
        var thread = new SessionWireThread
        {
            Id = "thread_001",
            SessionId = "session_001",
            WorkspacePath = "C:/workspace",
            Cwd = "C:/workspace",
            RuntimeWorkspaceRoots = ["C:/workspace"],
            EffectiveWorkspacePath = "C:/workspace",
            Path = "C:/workspace/.craft/threads/thread_001.jsonl",
            ForkedFromId = "thread_original",
            ParentThreadId = "thread_parent",
            Ephemeral = false,
            Worktree = new ThreadWorktreeInfo
            {
                Id = "worktree_001",
                SourceThreadId = "thread_original",
                WorkspacePath = "C:/workspace",
                SourceWorkspacePath = "C:/source",
                Path = "C:/workspace",
                BranchName = "codex/parity",
                BaseRef = "main",
                BaseHead = "abc",
                Head = "def",
                OwnerKind = "thread",
                OwnerId = "thread_001",
                CreatedAt = DateTimeOffset.Parse("2026-08-03T01:02:03Z"),
                DirtyHandoff = new ThreadWorktreeDirtyHandoffInfo
                {
                    Requested = true,
                    Status = "succeeded",
                    CopiedFileCount = 2,
                    DeletedFileCount = 1
                }
            },
            UserId = "user_001",
            OriginChannel = "telegram",
            ChannelContext = "chat_001",
            DisplayName = "Parity",
            Source = ThreadSource.ForSubAgent(new SubAgentThreadSource
            {
                ParentThreadId = "thread_parent",
                ParentTurnId = "turn_parent",
                SpawnCallId = "call_spawn",
                RootThreadId = "thread_root",
                Depth = 2,
                AgentPath = "researcher",
                TaskName = "Parity",
                AgentNickname = "Scout",
                AgentRole = "research",
                ProfileName = "default",
                RuntimeType = "session",
                SupportsSendInput = true,
                SupportsResume = true,
                SupportsSendMessage = true,
                SupportsFollowupTask = true,
                SupportsClose = true
            }),
            Status = ThreadStatus.Active,
            CreatedAt = DateTimeOffset.Parse("2026-08-03T01:02:03Z"),
            LastActiveAt = DateTimeOffset.Parse("2026-08-03T01:02:04Z"),
            HistoryMode = HistoryMode.Server,
            Configuration = new ThreadConfiguration
            {
                AgentProfileId = "profile_001",
                AgentProfileSource = "workspace",
                AgentProfileFingerprint = "sha256:fixture",
                Mode = "agent",
                Extensions = ["_fixture"],
                CustomTools = ["fixture_tool"],
                ProviderId = "provider_001",
                Model = "model_001",
                WorkspaceOverride = "C:/runtime",
                Cwd = "C:/runtime/subdir",
                RuntimeWorkspaceRoots = ["C:/runtime"],
                ExecutionWorkspaceOverride = "C:/execution",
                ToolProfile = "fixture",
                UseToolProfileOnly = true,
                AgentInstructions = "Fixture instructions",
                ToolAllowList = ["allowed"],
                ToolDenyList = ["denied"],
                RoleInstructions = "Research",
                OverrideBasePrompt = true,
                ApprovalTimeoutSeconds = 60,
                AutomationTaskDirectory = "C:/automation",
                RequireApprovalOutsideWorkspace = true
            },
            Metadata = new Dictionary<string, string> { ["key"] = "value" },
            Runtime = new SessionRuntimeSnapshot
            {
                Running = true,
                WaitingOnApproval = true,
                WaitingOnInput = true,
                WaitingOnPlanConfirmation = true,
                Busy = true,
                MaintenanceKind = "compacting"
            },
            QueuedInputs =
            [
                new QueuedTurnInput
                {
                    Id = "queue_001",
                    ThreadId = "thread_001",
                    NativeInputParts = [new SessionInputPart { Type = "text", Text = "queued" }],
                    MaterializedInputParts = [new SessionInputPart { Type = "text", Text = "queued" }],
                    DisplayText = "queued",
                    Sender = new SenderContext
                    {
                        SenderId = "user_001",
                        SenderName = "Ada",
                        SenderRole = "admin",
                        GroupId = "group_001"
                    },
                    Status = "queued",
                    CreatedAt = DateTimeOffset.Parse("2026-08-03T01:02:03Z"),
                    ReadyAfterTurnId = "turn_000",
                    TriggerKind = "automation",
                    TriggerLabel = "Fixture",
                    TriggerRefId = "trigger_001",
                    DeliveryBindingId = "binding_001",
                    SentAsGoal = true
                }
            ],
            Goal = new ThreadGoalSnapshot
            {
                ThreadId = "thread_001",
                Objective = "Ship parity",
                Status = "active",
                TokenBudget = 1000,
                TokensUsed = 12,
                TimeUsedSeconds = 3,
                CreatedAt = 1,
                UpdatedAt = 2
            },
            AppBindings =
            [
                new ThreadAppBindingSummarySnapshot
                {
                    BindingRequestId = "request_001",
                    ThreadId = "thread_001",
                    BindingId = "binding_001",
                    AppId = "app_001",
                    DisplayName = "Fixture App",
                    Icon = "fixture-icon",
                    State = "active",
                    Managed = true,
                    RequiresExternalConnection = true,
                    SocialTarget = new SocialChannelTarget
                    {
                        ChannelName = "telegram",
                        AccountId = "account_001",
                        ConversationKind = "group",
                        ConversationId = "group_001",
                        DeliveryTarget = "chat_001",
                        DisplayName = "Fixture chat",
                        BoundBy = new SocialChannelBoundBy
                        {
                            PlatformUserId = "user_001",
                            DisplayName = "Ada"
                        }
                    },
                    AuthorityRevision = 2,
                    ApprovedCapabilityRevision = 3,
                    CandidateCapabilityRevision = 4,
                    ApprovedTools = [],
                    PendingChanges = [],
                    FailureReason = "fixture"
                }
            ],
            OriginApp = new ThreadOriginAppSnapshot
            {
                AppId = "app_001",
                DisplayName = "Fixture App",
                Icon = "fixture-icon",
                MemberId = "member_001"
            },
            OriginPresentation = new ThreadOriginPresentationSnapshot
            {
                SourceId = "example-runtime",
                DisplayName = "Scout",
                Icon = "fixture-avatar",
                SubjectId = "member_001",
                SubjectKind = "member"
            },
            Turns = [turn],
            Plan = new SessionWirePlan
            {
                Title = "Plan",
                Overview = "Overview",
                Content = "Content",
                Todos =
                [
                    new SessionWirePlanTodo
                    {
                        Id = "todo_001",
                        Content = "Ship",
                        Priority = "high",
                        Status = "completed"
                    }
                ]
            },
            ContextUsage = new ContextUsageSnapshot
            {
                Tokens = 12,
                ContextWindow = 100,
                AutoCompactThreshold = 80,
                WarningThreshold = 70,
                ErrorThreshold = 90,
                PercentLeft = 0.88,
                Source = "estimate",
                IsEstimate = true
            }
        };

        AssertJsonEqual(thread, AppServerContractMapper.ToContract(thread));
        AssertJsonEqual(turn, AppServerContractMapper.ToContract(turn));
        AssertJsonEqual(item, AppServerContractMapper.ToContract(item));
    }

    private static IEnumerable<string> SerializedProperties(Type type) =>
        type.GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(static property => property.GetCustomAttribute<JsonExtensionDataAttribute>() is null)
            .Where(static property => property.GetCustomAttribute<JsonIgnoreAttribute>()?.Condition != JsonIgnoreCondition.Always)
            .Select(static property => property.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name
                                       ?? JsonNamingPolicy.CamelCase.ConvertName(property.Name));

    private static void AssertJsonEqual<TWire, TContract>(TWire wire, TContract contract)
    {
        var expected = JsonSerializer.SerializeToNode(wire, SessionWireJsonOptions.Default);
        var actual = JsonSerializer.SerializeToNode(contract, DotCraft.Protocol.AppServerContractJson.Options);
        Assert.True(JsonNode.DeepEquals(expected, actual), $"Expected: {expected}\nActual: {actual}");
    }
}
