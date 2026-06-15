using DotCraft.Mcp;

namespace DotCraft.Protocol.AppServer;

internal static class McpWireMapper
{
    public static McpServerConfigWire ToWire(McpServerConfig config) => new()
    {
        Name = config.Name,
        Enabled = config.Enabled,
        Transport = config.NormalizedTransport,
        Command = string.IsNullOrWhiteSpace(config.Command) ? null : config.Command,
        Args = config.Arguments.Count > 0 ? [.. config.Arguments] : null,
        Env = config.EnvironmentVariables.Count > 0 ? new Dictionary<string, string>(config.EnvironmentVariables) : null,
        EnvVars = config.EnvVars.Count > 0 ? [.. config.EnvVars] : null,
        Cwd = config.Cwd,
        Url = string.IsNullOrWhiteSpace(config.Url) ? null : config.Url,
        BearerTokenEnvVar = config.BearerTokenEnvVar,
        HttpHeaders = config.Headers.Count > 0 ? new Dictionary<string, string>(config.Headers) : null,
        EnvHttpHeaders = config.EnvHttpHeaders.Count > 0 ? new Dictionary<string, string>(config.EnvHttpHeaders) : null,
        StartupTimeoutSec = config.StartupTimeoutSec,
        ToolTimeoutSec = config.ToolTimeoutSec,
        Origin = ToWire(config.Origin),
        ReadOnly = config.ReadOnly
    };

    public static McpStatusInfoWire ToWire(McpServerStatusSnapshot status) => new()
    {
        Name = status.Name,
        Enabled = status.Enabled,
        StartupState = status.StartupState,
        ToolCount = status.ToolCount,
        ResourceCount = status.ResourceCount,
        ResourceTemplateCount = status.ResourceTemplateCount,
        LastError = status.LastError,
        Transport = status.Transport,
        Origin = ToWire(status.Origin),
        ReadOnly = status.ReadOnly
    };

    public static McpServerConfig FromWire(McpServerConfigWire wire) => new()
    {
        Name = wire.Name.Trim(),
        Enabled = wire.Enabled,
        Transport = NormalizeTransport(wire.Transport),
        Command = wire.Command?.Trim() ?? string.Empty,
        Arguments = wire.Args ?? [],
        EnvironmentVariables = wire.Env ?? new Dictionary<string, string>(),
        EnvVars = wire.EnvVars ?? [],
        Cwd = string.IsNullOrWhiteSpace(wire.Cwd) ? null : wire.Cwd.Trim(),
        Url = wire.Url?.Trim() ?? string.Empty,
        BearerTokenEnvVar = string.IsNullOrWhiteSpace(wire.BearerTokenEnvVar) ? null : wire.BearerTokenEnvVar.Trim(),
        Headers = wire.HttpHeaders ?? new Dictionary<string, string>(),
        EnvHttpHeaders = wire.EnvHttpHeaders ?? new Dictionary<string, string>(),
        StartupTimeoutSec = wire.StartupTimeoutSec,
        ToolTimeoutSec = wire.ToolTimeoutSec,
        Origin = McpServerOrigin.Workspace()
    };

    public static void ValidateConfig(McpServerConfigWire server)
    {
        if (string.IsNullOrWhiteSpace(server.Name))
            throw AppServerErrors.McpServerValidationFailed("'server.name' is required.");

        var transport = NormalizeTransport(server.Transport);

        if (transport == "stdio")
        {
            if (string.IsNullOrWhiteSpace(server.Command))
                throw AppServerErrors.McpServerValidationFailed("'server.command' is required for stdio transport.");
            if (!string.IsNullOrWhiteSpace(server.Url))
                throw AppServerErrors.McpServerValidationFailed("'server.url' is not supported for stdio transport.");
            if (!string.IsNullOrWhiteSpace(server.BearerTokenEnvVar))
                throw AppServerErrors.McpServerValidationFailed("'server.bearerTokenEnvVar' is not supported for stdio transport.");
            if (server.HttpHeaders is { Count: > 0 } || server.EnvHttpHeaders is { Count: > 0 })
                throw AppServerErrors.McpServerValidationFailed("HTTP headers are not supported for stdio transport.");
        }
        else
        {
            if (string.IsNullOrWhiteSpace(server.Url))
                throw AppServerErrors.McpServerValidationFailed("'server.url' is required for streamableHttp transport.");
            if (!Uri.TryCreate(server.Url, UriKind.Absolute, out _))
                throw AppServerErrors.McpServerValidationFailed("'server.url' must be an absolute URL.");
            if (!string.IsNullOrWhiteSpace(server.Command) ||
                server.Args is { Count: > 0 } ||
                server.Env is { Count: > 0 } ||
                server.EnvVars is { Count: > 0 } ||
                !string.IsNullOrWhiteSpace(server.Cwd))
            {
                throw AppServerErrors.McpServerValidationFailed("stdio-only fields are not supported for streamableHttp transport.");
            }
        }
    }

    private static McpServerOriginWire ToWire(McpServerOrigin origin) =>
        new()
        {
            Kind = origin.IsPlugin ? "plugin" : "workspace",
            PluginId = origin.PluginId,
            PluginDisplayName = origin.PluginDisplayName,
            DeclaredName = origin.DeclaredName
        };

    private static string NormalizeTransport(string? transport) =>
        transport?.Equals("streamableHttp", StringComparison.OrdinalIgnoreCase) == true
            || transport?.Equals("streamable-http", StringComparison.OrdinalIgnoreCase) == true
            || transport?.Equals("http", StringComparison.OrdinalIgnoreCase) == true
                ? "streamableHttp"
                : "stdio";
}
