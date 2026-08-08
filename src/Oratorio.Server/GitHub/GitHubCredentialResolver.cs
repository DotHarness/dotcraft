using Oratorio.Server.Services;

namespace Oratorio.Server.GitHub;

public sealed record GitHubCredentialStatus(
    bool HasAppAuthentication,
    bool CanWrite,
    bool HasWebhookSecret);

public interface IGitHubCredentialResolver
{
    GitHubCredentialStatus Resolve(GitHubOptions options);
    string? ResolveValue(string? value);
    string? ResolveSecret(string? value);
}

public sealed class GitHubCredentialResolver(IConfigurationSecretProtector secretProtector) : IGitHubCredentialResolver
{
    public GitHubCredentialStatus Resolve(GitHubOptions options)
    {
        var hasAppAuthentication =
            !string.IsNullOrWhiteSpace(ResolveValue(options.AppId)) &&
            (!string.IsNullOrWhiteSpace(ResolveSecret(options.PrivateKey)) ||
             !string.IsNullOrWhiteSpace(ResolveSecret(options.PrivateKeyPath)));
        return new GitHubCredentialStatus(
            hasAppAuthentication,
            options.WritesEnabled && hasAppAuthentication,
            !string.IsNullOrWhiteSpace(ResolveSecret(options.WebhookSecret)));
    }

    public string? ResolveValue(string? value) =>
        ExpandEnvironment(value);

    public string? ResolveSecret(string? value) =>
        ExpandEnvironment(secretProtector.Unprotect(value));

    private static string? ExpandEnvironment(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (value.StartsWith('$') && value.Length > 1)
        {
            return Environment.GetEnvironmentVariable(value[1..]);
        }

        return Environment.ExpandEnvironmentVariables(value);
    }
}
