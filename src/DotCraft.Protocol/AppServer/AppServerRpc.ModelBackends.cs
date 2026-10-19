namespace DotCraft.Protocol.AppServer;

public static partial class AppServerRpc
{
    private const string BackendSpec = "specs/protocols/provider-auth.md";
    public static readonly RpcRequest<RpcEmpty, ProviderBackendListResult> ProviderBackendList =
        new("provider/backend/list", RpcDirection.ClientToServer, "1", BackendSpec, scope: "connection", capability: "providerManagement", errors: CommonErrors);
    public static readonly RpcRequest<ProviderAuthLoginParams, ProviderAuthLoginState> ProviderAuthLogin =
        new("provider/auth/login", RpcDirection.ClientToServer, "1", BackendSpec, scope: "connection", capability: "providerManagement", errors: CommonErrors);
    public static readonly RpcRequest<ProviderAuthOperationParams, ProviderAuthLoginState> ProviderAuthState =
        new("provider/auth/state", RpcDirection.ClientToServer, "1", BackendSpec, scope: "connection", capability: "providerManagement", errors: CommonErrors);
    public static readonly RpcRequest<ProviderAuthInputParams, RpcEmpty> ProviderAuthInput =
        new("provider/auth/input", RpcDirection.ClientToServer, "1", BackendSpec, scope: "connection", capability: "providerManagement", errors: CommonErrors);
    public static readonly RpcRequest<ProviderAuthOperationParams, RpcEmpty> ProviderAuthCancel =
        new("provider/auth/cancel", RpcDirection.ClientToServer, "1", BackendSpec, scope: "connection", capability: "providerManagement", errors: CommonErrors);
    public static readonly RpcRequest<ProviderAuthStatusParams, ProviderAuthStatusResult> ProviderAuthStatus =
        new("provider/auth/status", RpcDirection.ClientToServer, "1", BackendSpec, scope: "connection", capability: "providerManagement", errors: CommonErrors);
    public static readonly RpcRequest<ProviderAuthInstanceParams, RpcEmpty> ProviderAuthLogout =
        new("provider/auth/logout", RpcDirection.ClientToServer, "1", BackendSpec, scope: "connection", capability: "providerManagement", errors: CommonErrors);
    public static readonly RpcRequest<ProviderAuthInstanceParams, ProviderAuthUsageResult> ProviderAuthUsage =
        new("provider/auth/usage", RpcDirection.ClientToServer, "1", BackendSpec, scope: "connection", capability: "providerManagement", errors: CommonErrors);
    public static readonly RpcNotification<ProviderAuthLoginState> ProviderAuthUpdated =
        new("provider/auth/updated", RpcDirection.ServerToClient, "1", BackendSpec, scope: "connection");
    public static readonly RpcNotification<ProviderAuthUsageUpdatedNotification> ProviderAuthUsageUpdated =
        new("provider/auth/usage/updated", RpcDirection.ServerToClient, "1", BackendSpec, scope: "connection", notificationOptOut: true);
}
