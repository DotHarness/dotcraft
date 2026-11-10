namespace DotCraft.AppServer;

public sealed record AppServerPendingClientRequest(int Id, Task<AppServerIncomingMessage> Response);
