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
        Context = ToContext(value.Context),
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
        ClientUserMessageId = value.ClientUserMessageId,
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
        Context = ToContext(value.Context),
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

    private static SessionInputContext? ToContext(Contract.InputContext? value) => value is null ? null : new()
    {
        Id = value.Id,
        Kind = value.Kind,
        Path = value.Path,
        FileName = value.FileName,
        Preview = value.Preview,
        CharacterCount = value.CharacterCount,
        ThreadId = value.ThreadId,
        TurnId = value.TurnId,
        ItemId = value.ItemId,
        SelectedText = value.SelectedText,
        Comment = value.Comment,
        Side = value.Side,
        StartLine = value.StartLine,
        EndLine = value.EndLine,
        Url = value.Url,
        Title = value.Title,
        SelectionKind = value.SelectionKind,
        Text = value.Text,
        Image = value.Image is null ? null : new() { TempPath = value.Image.TempPath, FileName = value.Image.FileName, MimeType = value.Image.MimeType }
    };

    private static Contract.InputContext? ToContext(SessionInputContext? value) => value is null ? null : new()
    {
        Id = value.Id,
        Kind = value.Kind,
        Path = value.Path,
        FileName = value.FileName,
        Preview = value.Preview,
        CharacterCount = value.CharacterCount,
        ThreadId = value.ThreadId,
        TurnId = value.TurnId,
        ItemId = value.ItemId,
        SelectedText = value.SelectedText,
        Comment = value.Comment,
        Side = value.Side,
        StartLine = value.StartLine,
        EndLine = value.EndLine,
        Url = value.Url,
        Title = value.Title,
        SelectionKind = value.SelectionKind,
        Text = value.Text,
        Image = value.Image is null ? null : new() { TempPath = value.Image.TempPath, FileName = value.Image.FileName, MimeType = value.Image.MimeType }
    };
}
