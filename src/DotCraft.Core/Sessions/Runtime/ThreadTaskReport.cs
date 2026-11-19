namespace DotCraft.Sessions;

public sealed record ThreadTaskReport(
    string NotificationId,
    string ReportedThreadId,
    string Title,
    string Status,
    string Summary,
    string? Project,
    string? Result);
