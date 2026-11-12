using System.Collections;
using System.Security.Cryptography;
using System.Text.Json;

namespace DotCraft.Mcp;

internal readonly record struct McpConnectionIdentity(string Configuration, string? Credentials)
{
    public static async ValueTask<McpConnectionIdentity> CreateAsync(
        McpServerConfig config, string? userDataPath, CancellationToken cancellationToken)
    {
        var configuration = CreateConfiguration(config);
        string? credentials = null;
        if (config.Enabled && userDataPath is not null && McpProtocolClientFactory.IsOAuthCandidate(config))
        {
            try
            {
                credentials = await McpOAuthTokenStore.Create(config, userDataPath)
                    .GetCredentialFingerprintAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Startup reports credential-store failures per server instead of failing the whole catalog.
                credentials = "unavailable";
            }
        }
        return new McpConnectionIdentity(configuration, credentials);
    }

    private static string CreateConfiguration(McpServerConfig config)
    {
        object inputs;
        if (config.NormalizedTransport == "streamableHttp")
        {
            var headers = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in config.Headers)
                headers[pair.Key] = pair.Value;
            if (!string.IsNullOrWhiteSpace(config.BearerTokenEnvVar))
                headers["Authorization"] = $"Bearer {Environment.GetEnvironmentVariable(config.BearerTokenEnvVar)}";
            foreach (var pair in config.EnvHttpHeaders)
                headers[pair.Key] = Environment.GetEnvironmentVariable(pair.Value);
            inputs = new
            {
                config.Url,
                Headers = headers.OrderBy(static pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(static pair => KeyValuePair.Create(pair.Key.ToUpperInvariant(), pair.Value)).ToArray(),
                config.OAuth,
                ClientSecret = string.IsNullOrWhiteSpace(config.OAuth?.ClientSecretEnvVar)
                    ? null : Environment.GetEnvironmentVariable(config.OAuth.ClientSecretEnvVar.Trim())
            };
        }
        else
        {
            var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
            var environment = new Dictionary<string, string?>(comparer);
            foreach (DictionaryEntry pair in Environment.GetEnvironmentVariables())
                environment[(string)pair.Key] = (string?)pair.Value;
            foreach (var pair in McpStdioEnvironment.CreateOverlay(config))
                environment[pair.Key] = pair.Value;
            inputs = new
            {
                config.Command,
                config.Arguments,
                config.Cwd,
                CwdBase = string.IsNullOrWhiteSpace(config.Cwd) || !Path.IsPathFullyQualified(config.Cwd)
                    ? Environment.CurrentDirectory : null,
                Environment = environment.OrderBy(static pair => pair.Key, comparer).Select(static pair =>
                    KeyValuePair.Create(OperatingSystem.IsWindows() ? pair.Key.ToUpperInvariant() : pair.Key, pair.Value)).ToArray()
            };
        }

        var bytes = JsonSerializer.SerializeToUtf8Bytes(new
        {
            config.Name, config.Enabled, Transport = config.NormalizedTransport, Origin = config.Origin.Kind, Inputs = inputs
        });
        return Convert.ToHexStringLower(SHA256.HashData(bytes));
    }
}
