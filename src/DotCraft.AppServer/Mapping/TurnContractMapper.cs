using Contract = DotCraft.Protocol.AppServer;
using DotCraft.Sessions;

namespace DotCraft.AppServer;

public static class TurnContractMapper
{
    public static IReadOnlyList<SessionInputPart> ToDomain(IReadOnlyList<Contract.InputPart> values) =>
        values.Select(ToDomain).ToArray();

    public static SessionInputPart ToDomain(Contract.InputPart value) => new()
    {
        Type = value.Type,
        Text = value.Text,
        Name = value.Name,
        ArgsText = value.ArgsText,
        RawText = value.RawText,
        Path = value.Path,
        DisplayPath = value.DisplayPath,
        Url = value.Url,
        MimeType = value.MimeType,
        FileName = value.FileName
    };

    public static SenderContext? ToDomain(Contract.SenderContext? value) => value is null
        ? null
        : new SenderContext
        {
            SenderId = value.SenderId,
            SenderName = value.SenderName,
            SenderRole = value.SenderRole,
            GroupId = value.GroupId
        };

    public static Contract.QueuedTurnInput ToContract(QueuedTurnInput value) => new()
    {
        Id = value.Id,
        ThreadId = value.ThreadId,
        NativeInputParts = value.NativeInputParts.Select(ToContract).ToArray(),
        MaterializedInputParts = value.MaterializedInputParts.Select(ToContract).ToArray(),
        DisplayText = value.DisplayText,
        Sender = ToContract(value.Sender),
        Status = value.Status,
        CreatedAt = value.CreatedAt,
        ReadyAfterTurnId = value.ReadyAfterTurnId,
        TriggerKind = value.TriggerKind,
        TriggerLabel = value.TriggerLabel,
        TriggerRefId = value.TriggerRefId,
        DeliveryBindingId = value.DeliveryBindingId,
        SentAsGoal = value.SentAsGoal
    };

    public static IReadOnlyList<Contract.QueuedTurnInput> ToContract(
        IReadOnlyList<QueuedTurnInput> values) =>
        values.Select(ToContract).ToArray();

    private static Contract.InputPart ToContract(SessionInputPart value) => new()
    {
        Type = value.Type,
        Text = value.Text,
        Name = value.Name,
        ArgsText = value.ArgsText,
        RawText = value.RawText,
        Path = value.Path,
        DisplayPath = value.DisplayPath,
        Url = value.Url,
        MimeType = value.MimeType,
        FileName = value.FileName
    };

    private static Contract.SenderContext? ToContract(SenderContext? value) => value is null
        ? null
        : new Contract.SenderContext
        {
            SenderId = value.SenderId,
            SenderName = value.SenderName,
            SenderRole = value.SenderRole,
            GroupId = value.GroupId
        };
}
