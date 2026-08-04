using System.Text.Json;
using System.Text.Json.Nodes;
using Domain = DotCraft.Sessions;
using Contract = DotCraft.Protocol.AppServer;
using DotCraft.Sessions;
using DotCraft.Sessions.Wire;

namespace DotCraft.AppServer;

internal static class WorktreeContractMapper
{
    public static Domain.SessionIdentity? ToDomain(Contract.SessionIdentity? value) => value is null
        ? null
        : new Domain.SessionIdentity
        {
            ChannelName = value.ChannelName,
            UserId = value.UserId,
            ChannelContext = value.ChannelContext,
            WorkspacePath = value.WorkspacePath ?? string.Empty
        };

    public static Domain.ThreadForkPoint? ToDomain(Contract.ThreadForkPoint? value) => value is null
        ? null
        : new Domain.ThreadForkPoint
        {
            TurnId = ValueOrDefault(value.TurnId),
            ItemId = ValueOrDefault(value.ItemId),
            Position = ValueOrDefault(value.Position) ?? Domain.ThreadForkPositions.After
        };

    public static IReadOnlyList<RuntimeDynamicToolDeclarationSpec>? ToDynamicTools(
        IReadOnlyList<JsonElement>? values) =>
        values?.Select(static value => value.Deserialize<RuntimeDynamicToolDeclarationSpec>(SessionWireJsonOptions.Default)
                                       ?? throw AppServerErrors.InvalidParams("A dynamic tool declaration is required."))
            .ToArray();

    public static IReadOnlyList<RuntimeDynamicToolDeclarationSpec>? ToDynamicTools(
        IReadOnlyList<Contract.RuntimeDynamicToolDeclaration>? values) =>
        values?.Select(ToDynamicTool).ToArray();

    private static RuntimeDynamicToolDeclarationSpec ToDynamicTool(
        Contract.RuntimeDynamicToolDeclaration value) => value switch
    {
        Contract.RuntimeDynamicToolFunction function => new RuntimeDynamicToolFunctionSpec
        {
            Name = function.Name,
            Description = function.Description,
            InputSchema = function.InputSchema is { } schema
                ? JsonNode.Parse(schema.GetRawText())?.AsObject()
                : null,
            DeferLoading = function.DeferLoading,
            Approval = function.Approval is null
                ? null
                : new ChannelToolApprovalSpec
                {
                    Kind = function.Approval.Kind,
                    TargetArgument = function.Approval.TargetArgument,
                    Operation = function.Approval.Operation,
                    OperationArgument = function.Approval.OperationArgument
                }
        },
        Contract.RuntimeDynamicToolNamespace toolNamespace => new RuntimeDynamicToolNamespaceSpec
        {
            Name = toolNamespace.Name,
            Description = toolNamespace.Description,
            Tools = toolNamespace.Tools.Select(ToDynamicTool).ToList()
        },
        _ => throw AppServerErrors.InvalidParams(
            $"Unsupported dynamic tool declaration '{value.GetType().Name}'.")
    };

    public static IReadOnlyDictionary<string, RuntimeAdditionalContextValue>? ToAdditionalContext(
        IReadOnlyDictionary<string, Contract.RuntimeAdditionalContextEntry>? values) =>
        values?.ToDictionary(
            static entry => entry.Key,
            static entry => new RuntimeAdditionalContextValue
            {
                Kind = entry.Value.Kind,
                Value = entry.Value.Value
            },
            StringComparer.Ordinal);

    public static Contract.ThreadWorktreeInfo ToContract(Domain.ThreadWorktreeInfo value) => new()
    {
        Id = value.Id,
        SourceThreadId = value.SourceThreadId,
        WorkspacePath = value.WorkspacePath,
        SourceWorkspacePath = value.SourceWorkspacePath,
        Path = value.Path,
        BranchName = value.BranchName,
        BaseRef = value.BaseRef,
        BaseHead = value.BaseHead,
        Head = value.Head,
        OwnerKind = OmitIfNull(value.OwnerKind),
        OwnerId = OmitIfNull(value.OwnerId),
        CreatedAt = value.CreatedAt,
        DirtyHandoff = value.DirtyHandoff is null
            ? default
            : new DotCraft.Protocol.Optional<Contract.ThreadWorktreeDirtyHandoffInfo?>(
                ToContract(value.DirtyHandoff))
    };

    public static Contract.ThreadWorktreeDirtyHandoffInfo ToContract(Domain.ThreadWorktreeDirtyHandoffInfo value) => new()
    {
        Requested = value.Requested,
        Status = value.Status,
        CopiedFileCount = value.CopiedFileCount,
        DeletedFileCount = value.DeletedFileCount
    };

    public static Contract.ThreadWorktreeStatus ToContract(Domain.ThreadWorktreeStatus value) => new()
    {
        ThreadId = value.ThreadId,
        Worktree = ToContract(value.Worktree),
        Path = value.Path,
        BranchName = value.BranchName,
        Head = OmitIfNull(value.Head),
        Exists = value.Exists,
        IsGitWorktree = value.IsGitWorktree,
        HasUncommittedChanges = value.HasUncommittedChanges,
        HasCommitsAheadOfBase = value.HasCommitsAheadOfBase,
        AheadCount = value.AheadCount
    };

    private static DotCraft.Protocol.Optional<T?> OmitIfNull<T>(T? value) =>
        value is null ? default : new DotCraft.Protocol.Optional<T?>(value);

    private static T? ValueOrDefault<T>(DotCraft.Protocol.Optional<T> value) =>
        value.IsSet ? value.Value : default;
}
