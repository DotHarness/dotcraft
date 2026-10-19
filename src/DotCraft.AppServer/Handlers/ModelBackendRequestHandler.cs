using DotCraft.Agents;
using DotCraft.Configuration;
using DotCraft.Protocol;
using Contract = DotCraft.Protocol.AppServer;

namespace DotCraft.AppServer;

internal sealed class ModelBackendRequestHandler(
    IAppServerTransport transport,
    AppServerConnection connection,
    AppServerConnectionServices services,
    WorkspaceConfigEditor workspaceConfig) : IAppServerDomainHandler
{
    private readonly string _owner = Guid.NewGuid().ToString("N");
    private ModelBackendResolver Resolver => services.ChatClientRegistry?.RuntimeResolver as ModelBackendResolver
        ?? throw AppServerErrors.InvalidRequest("Model backend management is not available.");
    private ProviderAuthenticationCoordinator Authentication => services.ProviderAuthentication
        ?? throw AppServerErrors.InvalidRequest("Provider authentication is not available.");

    public void RegisterMethods(AppServerMethodTable table)
    {
        Map(table, Contract.AppServerRpc.ProviderBackendList, (_, _) => Task.FromResult<object?>(new Contract.ProviderBackendListResult
        {
            Backends = Resolver.GetBackends().Select(descriptor => new Contract.ProviderBackendInfo
            {
                Id = descriptor.Id, DisplayName = descriptor.DisplayName, DefaultProtocol = descriptor.DefaultProtocol,
                Protocols = descriptor.Protocols,
                AuthenticationMethods = descriptor.AuthenticationMethods.Select(method => new Contract.ProviderAuthenticationMethodInfo
                { Id = method.Id, DisplayName = method.DisplayName, Kind = method.Kind }).ToArray()
            }).ToArray()
        }));
        Map(table, Contract.AppServerRpc.ProviderAuthLogin, (p, _) =>
            Task.FromResult<object?>(MapState(Authentication.Login(Config(), Required(p.ProviderId), p.Method, _owner))));
        Map(table, Contract.AppServerRpc.ProviderAuthState, (p, _) =>
            Task.FromResult<object?>(MapState(Authentication.Get(Required(p.LoginId), _owner))));
        Map(table, Contract.AppServerRpc.ProviderAuthInput, (p, _) =>
        {
            Authentication.Respond(Required(p.LoginId), Required(p.PromptId), p.Value, _owner);
            return Task.FromResult<object?>(new RpcEmpty());
        });
        Map(table, Contract.AppServerRpc.ProviderAuthCancel, (p, _) =>
        {
            Authentication.Cancel(Required(p.LoginId), _owner);
            return Task.FromResult<object?>(new RpcEmpty());
        });
        Map(table, Contract.AppServerRpc.ProviderAuthStatus, async (p, ct) =>
        {
            var config = Config();
            var providerId = Required(p.ProviderId);
            var token = p.IncludeToken || p.RefreshToken
                ? await Authentication.GetAccessTokenAsync(config, providerId, p.RefreshToken, ct).ConfigureAwait(false)
                : null;
            var status = await Authentication.GetStatusAsync(config, providerId, ct).ConfigureAwait(false);
            return MapStatus(status, p.IncludeToken ? token : null);
        });
        Map(table, Contract.AppServerRpc.ProviderAuthLogout, async (p, ct) =>
        {
            await Authentication.LogoutAsync(Config(), Required(p.ProviderId), ct).ConfigureAwait(false);
            return new RpcEmpty();
        });
        Map(table, Contract.AppServerRpc.ProviderAuthUsage, async (p, ct) =>
            ProviderUsageMapping.ToResult(
                await Authentication.ReadUsageAsync(Config(), Required(p.ProviderId), ct).ConfigureAwait(false)));
        if (services.ProviderAuthentication is { } authentication)
        {
            authentication.Changed += OnChanged;
            _ = ReleaseOnCloseAsync(authentication);
        }
    }

    private AppConfig Config() => services.AppConfigMonitor?.Current ?? workspaceConfig.LoadCurrentMergedConfig();

    private void OnChanged(string owner, ProviderLoginState state)
    {
        if (owner == _owner && !connection.IsClosed)
            _ = NotifyAsync(state);
    }

    private async Task NotifyAsync(ProviderLoginState state)
    {
        try { await transport.NotifyContractAsync(Contract.AppServerRpc.ProviderAuthUpdated, MapState(state), CancellationToken.None).ConfigureAwait(false); }
        catch (Exception) { }
    }

    private async Task ReleaseOnCloseAsync(ProviderAuthenticationCoordinator authentication)
    {
        await connection.Closed.ConfigureAwait(false);
        authentication.Changed -= OnChanged;
        authentication.CancelOwner(_owner);
    }

    private static string Required(string value) => !string.IsNullOrWhiteSpace(value) ? value.Trim()
        : throw AppServerErrors.InvalidParams("A provider, login or prompt identifier is required.");

    private static void Map<TParams, TResult>(AppServerMethodTable table, RpcRequest<TParams, TResult> method,
        Func<TParams, CancellationToken, Task<object?>> handler) where TParams : class where TResult : class =>
        table.Map(method, async (request, ct) =>
        {
            try { return await handler(request.Params, ct).ConfigureAwait(false); }
            catch (ProviderAuthenticationException exception)
            {
                throw new AppServerException(-32600, exception.Message, new AppServerErrorData
                { Code = exception.Code, MessageKey = exception.Code, FallbackText = exception.Message });
            }
            catch (ModelProviderConfigurationException exception)
            {
                throw AppServerErrors.InvalidRequest(exception.Message);
            }
            catch (ModelBackendUnavailableException exception)
            {
                throw new AppServerException(-32600, exception.Message, new AppServerErrorData
                { Code = ModelBackendUnavailableException.ErrorCode, MessageKey = "ModelBackendUnavailable", FallbackText = exception.Message });
            }
        });

    private static Contract.ProviderAuthStatusResult MapStatus(ProviderAuthenticationStatus status, string? authToken = null) => new()
    {
        IsAuthenticated = status.IsAuthenticated, AccountId = status.AccountId, DisplayName = status.DisplayName,
        PlanType = status.PlanType, Email = status.Email, FailureCode = status.FailureCode, FailureMessage = status.FailureMessage,
        AuthToken = authToken
    };

    private static Contract.ProviderAuthLoginState MapState(ProviderLoginState state) => new()
    {
        LoginId = state.Id, ProviderId = state.ProviderId, Revision = state.Revision, Status = state.Status,
        PromptId = state.PromptId, ErrorCode = state.ErrorCode, ErrorMessage = state.ErrorMessage,
        Authentication = state.Authentication is null ? null : MapStatus(state.Authentication),
        Event = state.Event is null ? null : new Contract.ProviderAuthEvent
        { Kind = state.Event.Kind, Message = state.Event.Message, Url = state.Event.Url, UserCode = state.Event.UserCode },
        Prompt = state.Prompt is null ? null : new Contract.ProviderAuthPrompt
        {
            Kind = state.Prompt.Kind, Message = state.Prompt.Message,
            Choices = state.Prompt.Choices?.Select(choice => new Contract.ProviderAuthChoice { Id = choice.Id, Label = choice.Label }).ToArray()
        }
    };
}
