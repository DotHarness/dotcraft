using DotCraft.Protocol;
using Contract = DotCraft.Protocol.AppServer;
using Domain = DotCraft.Mcp;

namespace DotCraft.AppServer;

internal static class McpOAuthContractMapper
{
    /// <summary>The read projection of a saved literal secret; writing it back keeps the saved value.</summary>
    internal const string MaskedSecret = "********";

    public static Optional<Contract.McpOAuthConfig?> ToContract(Domain.McpOAuthConfig? config) => config is null
        ? default
        : new Contract.McpOAuthConfig
        {
            ClientId = config.ClientId,
            ClientSecret = string.IsNullOrEmpty(config.ClientSecret) ? null : MaskedSecret,
            HasClientSecret = !string.IsNullOrEmpty(config.ClientSecret),
            ClientSecretEnvVar = config.ClientSecretEnvVar,
            CallbackUrl = config.CallbackUrl,
            CallbackPort = config.CallbackPort
        };

    public static Domain.McpOAuthConfig? FromContract(Optional<Contract.McpOAuthConfig?> wire, Domain.McpOAuthConfig? existing = null)
    {
        if (!wire.IsSet) return existing?.Clone();
        if (wire.Value is not { } value) return null;
        var secret = value.ClientSecret.IsSet && value.ClientSecret.Value != MaskedSecret
            ? value.ClientSecret.Value
            : existing?.ClientSecret;
        return new Domain.McpOAuthConfig
        {
            ClientId = Value(value.ClientId)?.Trim(),
            ClientSecret = string.IsNullOrWhiteSpace(secret) ? null : secret,
            ClientSecretEnvVar = Value(value.ClientSecretEnvVar)?.Trim(),
            CallbackUrl = Value(value.CallbackUrl)?.Trim(),
            CallbackPort = Value(value.CallbackPort)
        };
    }

    private static T? Value<T>(Optional<T> value) => value.IsSet ? value.Value : default;
}
