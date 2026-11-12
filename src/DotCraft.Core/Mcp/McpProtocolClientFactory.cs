using System.Text.Json;
using ModelContextProtocol.Authentication;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace DotCraft.Mcp;

internal static class McpProtocolClientFactory
{
    internal static async Task<McpClient> CreateAsync(
        McpServerConfig server,
        IMcpElicitationHandler? elicitationHandler,
        ClientOAuthOptions? oauthOptions,
        string? userDataPath,
        CancellationToken cancellationToken)
    {
        // Connection readers must not inherit an invocation that happened to create this client.
        using var connectionScope = new McpInvocationScope(null, Task.CompletedTask);
        IClientTransport transport;

        if (server.NormalizedTransport == "streamableHttp")
        {
            if (string.IsNullOrWhiteSpace(server.Url))
                throw new InvalidOperationException($"MCP server '{server.Name}' has transport 'streamableHttp' but no Url configured.");

            var options = new HttpClientTransportOptions
            {
                Endpoint = new Uri(server.Url),
                Name = server.Name,
                TransportMode = HttpTransportMode.AutoDetect,
            };

            if (oauthOptions == null)
            {
                var tokenStore = McpOAuthTokenStore.Create(server, userDataPath);
                if (IsOAuthCandidate(server))
                {
                    var hadTokens = await tokenStore.HasTokensAsync(cancellationToken);
                    oauthOptions = McpOAuthOptionsFactory.Create(server, userDataPath);
                    oauthOptions.AuthorizationCallbackHandler = (_, _) =>
                        throw new McpAuthenticationRequiredException(hadTokens);
                }
            }

            options.OAuth = oauthOptions;

            var headers = new Dictionary<string, string>(server.Headers, StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrWhiteSpace(server.BearerTokenEnvVar))
            {
                var token = Environment.GetEnvironmentVariable(server.BearerTokenEnvVar);
                if (string.IsNullOrWhiteSpace(token))
                    throw new InvalidOperationException($"MCP server '{server.Name}' requires env var '{server.BearerTokenEnvVar}'.");
                headers["Authorization"] = $"Bearer {token}";
            }

            foreach (var (headerName, envVarName) in server.EnvHttpHeaders)
            {
                var value = Environment.GetEnvironmentVariable(envVarName);
                if (string.IsNullOrWhiteSpace(value))
                    throw new InvalidOperationException($"MCP server '{server.Name}' requires env var '{envVarName}' for header '{headerName}'.");
                headers[headerName] = value;
            }

            if (headers.Count > 0)
                options.AdditionalHeaders = headers;

            if (server.Origin.IsBinding)
            {
                // A binding endpoint is an authority boundary. Following a redirect could move the
                // one-time bearer to a different origin without a fresh activation decision.
                var handler = new HttpClientHandler { AllowAutoRedirect = false };
                transport = new HttpClientTransport(options, new HttpClient(handler), loggerFactory: null, ownsHttpClient: true);
            }
            else
            {
                transport = new HttpClientTransport(options);
            }
        }
        else
        {
            if (string.IsNullOrWhiteSpace(server.Command))
                throw new InvalidOperationException($"MCP server '{server.Name}' has transport 'stdio' but no Command configured.");

            var environmentVariables = McpStdioEnvironment.CreateOverlay(server);

            var options = new StdioClientTransportOptions
            {
                Command = server.Command,
                Name = server.Name,
                Arguments = server.Arguments is { Count: > 0 } ? server.Arguments : null,
                EnvironmentVariables = environmentVariables.Count > 0 ? environmentVariables : null,
                InheritEnvironmentVariables = true,
                WorkingDirectory = string.IsNullOrWhiteSpace(server.Cwd) ? null : server.Cwd
            };

            transport = new StdioClientTransport(options);
        }

        var clientOptions = CreateClientOptions((request, ct) =>
            McpInvocationScope.HandleAsync(elicitationHandler, server.Name, request, ct));
        return await McpClient.CreateAsync(transport, clientOptions, cancellationToken: cancellationToken);
    }

    internal static bool HasConfiguredBearerToken(McpServerConfig server) =>
        !string.IsNullOrWhiteSpace(server.BearerTokenEnvVar)
        || server.Headers.Keys.Any(static name =>
            string.Equals(name, "Authorization", StringComparison.OrdinalIgnoreCase))
        || server.EnvHttpHeaders.Keys.Any(static name =>
            string.Equals(name, "Authorization", StringComparison.OrdinalIgnoreCase));

    internal static bool IsOAuthCandidate(McpServerConfig server) =>
        string.Equals(server.NormalizedTransport, "streamableHttp", StringComparison.Ordinal)
        && !HasConfiguredBearerToken(server);

    private static McpClientOptions CreateClientOptions(
        Func<ElicitRequestParams?, CancellationToken, ValueTask<ElicitResult>> elicitationHandler) =>
        new()
        {
            Capabilities = new ClientCapabilities
            {
                Elicitation = new ElicitationCapability { Form = new(), Url = new() },
                Extensions = new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    [McpAppMetadataParser.ExtensionIdentifier] = JsonSerializer.SerializeToElement(new
                    {
                        mimeTypes = new[] { McpAppMetadataParser.HtmlMimeType }
                    })
                }
            },
            Handlers = new McpClientHandlers { ElicitationHandler = elicitationHandler }
        };
}
