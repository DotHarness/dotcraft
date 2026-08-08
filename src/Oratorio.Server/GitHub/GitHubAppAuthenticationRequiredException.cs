namespace Oratorio.Server.GitHub;

public sealed class GitHubAppAuthenticationRequiredException(string? message = null) : InvalidOperationException(message ?? DefaultMessage)
{
    public const string Code = "githubAppAuthRequired";
    public string ErrorCode => Code;

    private const string DefaultMessage = "GitHub App ID and private key are required for GitHub API requests.";
}
