using DotCraft.Auth.OpenAI;
using DotCraft.Configuration;

namespace DotCraft.Agents;

public sealed partial class OpenAIClientProvider
{
    Task<ProviderAuthenticationStatus> IProviderAuthenticator.GetStatusAsync(CancellationToken cancellationToken) =>
        Task.FromResult(_openAIAuthService?.GetStatus() is { } status
            ? OpenAIProviderProjection.ToProviderStatus(status)
            : new ProviderAuthenticationStatus(false));

    async Task<ProviderAuthenticationStatus> IProviderAuthenticator.LoginAsync(
        string method,
        IProviderLoginInteraction interaction,
        CancellationToken cancellationToken)
    {
        if (method != ModelProviderAuthMethods.ChatGptOAuth)
            throw new InvalidOperationException($"Authentication method '{method}' does not sign in.");
        var auth = _openAIAuthService
            ?? throw new InvalidOperationException("ChatGPT authentication is not available.");
        var status = await auth.LoginAsync(
            openBrowser: false,
            url => interaction.NotifyAsync(new ProviderLoginEvent("auth_url", Url: url), cancellationToken)
                .AsTask().GetAwaiter().GetResult(),
            cancellationToken).ConfigureAwait(false);
        return OpenAIProviderProjection.ToProviderStatus(status);
    }

    Task IProviderAuthenticator.LogoutAsync(CancellationToken cancellationToken) =>
        _openAIAuthService?.LogoutAsync(cancellationToken) ?? Task.CompletedTask;

    async Task<string?> IProviderAccessTokenReader.TryGetAccessTokenAsync(
        bool forceRefresh,
        CancellationToken cancellationToken)
    {
        if (_openAIAuthService == null)
            return null;
        try
        {
            return await _openAIAuthService.GetAccessTokenAsync(forceRefresh, cancellationToken).ConfigureAwait(false);
        }
        catch (OpenAIAuthException)
        {
            return null;
        }
    }
}
