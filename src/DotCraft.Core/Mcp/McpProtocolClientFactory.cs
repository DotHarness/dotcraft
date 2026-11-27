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
        using var connectionScope = new McpInvocationScope(null, null);
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

            transport = new HttpClientTransport(options);
        }
        else
        {
            if (string.IsNullOrWhiteSpace(server.Command))
                throw new InvalidOperationException($"MCP server '{server.Name}' has transport 'stdio' but no Command configured.");

            var environmentVariables = McpStdioEnvironment.CreateOverlay(server);
            var isDotNetAssembly = server.Command.EndsWith(".dll", StringComparison.OrdinalIgnoreCase);
            var command = isDotNetAssembly ? ResolveDotNetHost() : server.Command;
            List<string>? arguments = isDotNetAssembly
                ? [server.Command, .. server.Arguments]
                : server.Arguments is { Count: > 0 } ? server.Arguments : null;

            var options = new StdioClientTransportOptions
            {
                Command = command,
                Name = server.Name,
                Arguments = arguments,
                EnvironmentVariables = environmentVariables.Count > 0 ? environmentVariables : null,
                InheritEnvironmentVariables = true,
                WorkingDirectory = string.IsNullOrWhiteSpace(server.Cwd) ? null : server.Cwd
            };

            transport = new StdioClientTransport(options);
        }

        var activeInvocations = new McpInvocationScope.ActiveInvocationTable();
        var clientOptions = CreateClientOptions((request, ct) =>
            McpInvocationScope.HandleAsync(elicitationHandler, server.Name, activeInvocations, request, ct));
        var client = await McpClient.CreateAsync(transport, clientOptions, cancellationToken: cancellationToken);
        McpInvocationScope.AttachClient(client, activeInvocations);
        return client;
    }

    internal static string ResolveDotNetHost(string? processPath = null)
    {
        processPath ??= Environment.ProcessPath;
        return !string.IsNullOrWhiteSpace(processPath)
            && Path.GetFileNameWithoutExtension(processPath).Equals("dotnet", StringComparison.OrdinalIgnoreCase)
            ? processPath
            : "dotnet";
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
