using DotCraft.Auth.OpenAI;

namespace DotCraft.Agents;

public sealed partial class OpenAIClientProvider
{
    ProviderAuthenticationStatus IProviderAuthentication.GetStatus() =>
        ToAuthenticationStatus(_openAIAuthService?.GetStatus());

    async Task<ProviderAuthenticationStatus> IProviderAuthentication.LoginAsync(
        ProviderLoginRequest request,
        CancellationToken cancellationToken)
    {
        var auth = _openAIAuthService
            ?? throw new InvalidOperationException("ChatGPT authentication is not available.");
        var status = await auth.LoginAsync(
            request.OpenBrowser,
            request.AuthorizationRequestAvailable == null
                ? null
                : url => request.AuthorizationRequestAvailable(
                        CreateAuthorizationRequest(url))
                    .AsTask()
                    .GetAwaiter()
                    .GetResult(),
            cancellationToken).ConfigureAwait(false);
        return ToAuthenticationStatus(status);
    }

    Task IProviderAuthentication.LogoutAsync(CancellationToken cancellationToken) =>
        _openAIAuthService?.LogoutAsync(cancellationToken)
        ?? Task.CompletedTask;

    private static ProviderAuthorizationRequest CreateAuthorizationRequest(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var authorizationUrl))
            throw new InvalidOperationException("OpenAI returned an invalid authorization URL.");

        var query = System.Web.HttpUtility.ParseQueryString(authorizationUrl.Query);
        if (!Uri.TryCreate(query["redirect_uri"], UriKind.Absolute, out var redirectUri)
            || !redirectUri.IsLoopback)
        {
            throw new InvalidOperationException(
                "OpenAI authorization URL does not contain a valid loopback redirect URI.");
        }

        return new ProviderAuthorizationRequest(authorizationUrl, redirectUri.Port);
    }

    private static ProviderAuthenticationStatus ToAuthenticationStatus(OpenAIAuthStatus? status) =>
        status == null
            ? new ProviderAuthenticationStatus(false)
            : new ProviderAuthenticationStatus(
                status.LoggedIn,
                status.AccountId,
                status.Email,
                PlanType: status.PlanType,
                Email: status.Email,
                LastRefresh: status.LastRefresh,
                AccessTokenExpiresAt: status.AccessTokenExpiresAt);
}
