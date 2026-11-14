using System.Security.Authentication;
using DotCraft.Auth.OpenAI;
using DotCraft.CodexGateway;

namespace DotCraft.Hub;

internal sealed class CodexGatewayAuthentication : IDisposable
{
    private readonly OpenAIAuthManager _manager;
    private readonly FileSystemWatcher _watcher;

    public CodexGatewayAuthentication(string craftHome, Action changed)
    {
        Directory.CreateDirectory(craftHome);
        _manager = new OpenAIAuthManager(new OpenAITokenStore(craftHome));
        _watcher = new FileSystemWatcher(craftHome, "auth.json")
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size
        };
        _watcher.Created += (_, _) => changed();
        _watcher.Changed += (_, _) => changed();
        _watcher.Deleted += (_, _) => changed();
        _watcher.Renamed += (_, _) => changed();
        _watcher.Error += (_, _) => changed();
        _watcher.EnableRaisingEvents = true;
    }

    public string? AccountId
    {
        get
        {
            var status = _manager.GetStatus();
            return status.LoggedIn ? status.AccountId : null;
        }
    }

    public async Task<RemoteControlCredentials> CredentialsAsync(bool forceRefresh, CancellationToken ct)
    {
        var account = AccountId ?? throw new AuthenticationException("ChatGPT sign-in is required.");
        try
        {
            var token = await _manager.GetAccessTokenAsync(forceRefresh, ct);
            if (AccountId != account)
                throw new AuthenticationException("ChatGPT account changed during credential acquisition.");
            return new(token, account);
        }
        catch (OpenAIAuthException error) when (error.Reason == OpenAIAuthFailureReason.Network)
        {
            throw new HttpRequestException("codex_remote_control.credential_transport_failed: Unable to refresh ChatGPT credentials.");
        }
        catch (OpenAIAuthException error) when (error.Reason is OpenAIAuthFailureReason.NotSignedIn
            or OpenAIAuthFailureReason.RefreshTokenExpired or OpenAIAuthFailureReason.RefreshTokenReused
            or OpenAIAuthFailureReason.RefreshTokenRevoked || AccountId is null)
        {
            throw new AuthenticationException("codex_remote_control.authentication_failed: ChatGPT sign-in is required.");
        }
    }

    public void Dispose() => _watcher.Dispose();
}
