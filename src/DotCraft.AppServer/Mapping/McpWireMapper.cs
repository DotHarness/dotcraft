using Contract = DotCraft.Protocol.AppServer;
using McpServerConfig = DotCraft.Mcp.McpServerConfig;
using McpServerOrigin = DotCraft.Mcp.McpServerOrigin;
using McpToolApprovalMode = DotCraft.Mcp.McpToolApprovalMode;
using McpToolConfig = DotCraft.Mcp.McpToolConfig;

namespace DotCraft.AppServer;

internal static class McpContractMapper
{
    public static Contract.McpServerConfig ToContract(McpServerConfig config) => new()
    {
        Name = config.Name,
        Enabled = config.Enabled,
        Transport = config.NormalizedTransport,
        Command = string.IsNullOrWhiteSpace(config.Command) ? null : config.Command,
        Args = new Protocol.Optional<IReadOnlyList<string>?>(
            config.Arguments.Count > 0 ? config.Arguments.ToArray() : null),
        Env = config.EnvironmentVariables.Count > 0 ? new Dictionary<string, string>(config.EnvironmentVariables) : null,
        EnvVars = new Protocol.Optional<IReadOnlyList<string>?>(
            config.EnvVars.Count > 0 ? config.EnvVars.ToArray() : null),
        Cwd = config.Cwd,
        Url = string.IsNullOrWhiteSpace(config.Url) ? null : config.Url,
        BearerTokenEnvVar = config.BearerTokenEnvVar,
        OAuth = McpOAuthContractMapper.ToContract(config.OAuth),
        HttpHeaders = config.Headers.Count > 0 ? new Dictionary<string, string>(config.Headers) : null,
        EnvHttpHeaders = config.EnvHttpHeaders.Count > 0 ? new Dictionary<string, string>(config.EnvHttpHeaders) : null,
        StartupTimeoutSec = config.StartupTimeoutSec,
        ToolTimeoutSec = config.ToolTimeoutSec,
        ToolApprovalMode = ToContract(config.ToolApprovalMode),
        Tools = config.Tools is null
            ? default
            : new Protocol.Optional<IReadOnlyDictionary<string, Contract.McpToolConfig>?>(config.Tools.ToDictionary(
                static pair => pair.Key,
                static pair => new Contract.McpToolConfig
                {
                    ApprovalMode = pair.Value.ApprovalMode is { } mode ? ToContract(mode) : default
                },
                StringComparer.Ordinal)),
        EnabledTools = ToolNames(config.EnabledTools),
        DisabledTools = ToolNames(config.DisabledTools),
        Origin = ToContract(config.Origin),
        ReadOnly = config.ReadOnly
    };

    public static McpServerConfig FromContract(Contract.McpServerConfig wire, McpServerConfig? existing = null)
    {
        var kept = existing?.Clone();
        return new()
        {
            Name = ValueOrDefault(wire.Name)?.Trim() ?? string.Empty,
            Enabled = ValueOrDefault(wire.Enabled),
            Transport = NormalizeTransport(ValueOrDefault(wire.Transport)),
            Command = ValueOrDefault(wire.Command)?.Trim() ?? string.Empty,
            Arguments = ValueOrDefault(wire.Args)?.ToList() ?? [],
            EnvironmentVariables = ValueOrDefault(wire.Env)?.ToDictionary() ?? new Dictionary<string, string>(),
            EnvVars = ValueOrDefault(wire.EnvVars)?.ToList() ?? [],
            Cwd = string.IsNullOrWhiteSpace(ValueOrDefault(wire.Cwd)) ? null : ValueOrDefault(wire.Cwd)!.Trim(),
            Url = ValueOrDefault(wire.Url)?.Trim() ?? string.Empty,
            BearerTokenEnvVar = string.IsNullOrWhiteSpace(ValueOrDefault(wire.BearerTokenEnvVar)) ? null : ValueOrDefault(wire.BearerTokenEnvVar)!.Trim(),
            Headers = ValueOrDefault(wire.HttpHeaders)?.ToDictionary() ?? new Dictionary<string, string>(),
            EnvHttpHeaders = ValueOrDefault(wire.EnvHttpHeaders)?.ToDictionary() ?? new Dictionary<string, string>(),
            StartupTimeoutSec = ValueOrDefault(wire.StartupTimeoutSec),
            ToolTimeoutSec = ValueOrDefault(wire.ToolTimeoutSec),
            ToolApprovalMode = wire.ToolApprovalMode.IsSet
                ? ParseToolApprovalMode(wire.ToolApprovalMode.Value, "server.toolApprovalMode")
                : kept?.ToolApprovalMode ?? McpToolApprovalMode.Auto,
            Tools = wire.Tools.IsSet ? ParseTools(wire.Tools.Value) : kept?.Tools,
            EnabledTools = wire.EnabledTools.IsSet ? wire.EnabledTools.Value?.ToList() : kept?.EnabledTools,
            DisabledTools = wire.DisabledTools.IsSet ? wire.DisabledTools.Value?.ToList() : kept?.DisabledTools,
            OAuth = NormalizeTransport(ValueOrDefault(wire.Transport)) == "stdio" ? null : McpOAuthContractMapper.FromContract(wire.OAuth, kept?.OAuth),
            Origin = McpServerOrigin.Workspace()
        };
    }

    public static void ValidateContract(Contract.McpServerConfig server)
    {
        var name = ValueOrDefault(server.Name);
        var command = ValueOrDefault(server.Command);
        var url = ValueOrDefault(server.Url);
        var bearerTokenEnvVar = ValueOrDefault(server.BearerTokenEnvVar);
        var args = ValueOrDefault(server.Args);
        var env = ValueOrDefault(server.Env);
        var envVars = ValueOrDefault(server.EnvVars);
        var cwd = ValueOrDefault(server.Cwd);
        var httpHeaders = ValueOrDefault(server.HttpHeaders);
        var envHttpHeaders = ValueOrDefault(server.EnvHttpHeaders);
        if (string.IsNullOrWhiteSpace(name))
            throw AppServerErrors.McpServerValidationFailed("'server.name' is required.");

        var transport = NormalizeTransport(ValueOrDefault(server.Transport));
        try { FromContract(server).ValidateOAuth(); }
        catch (InvalidOperationException ex) { throw AppServerErrors.McpServerValidationFailed(ex.Message); }

        if (transport == "stdio")
        {
            if (string.IsNullOrWhiteSpace(command))
                throw AppServerErrors.McpServerValidationFailed("'server.command' is required for stdio transport.");
            if (!string.IsNullOrWhiteSpace(url))
                throw AppServerErrors.McpServerValidationFailed("'server.url' is not supported for stdio transport.");
            if (!string.IsNullOrWhiteSpace(bearerTokenEnvVar))
                throw AppServerErrors.McpServerValidationFailed("'server.bearerTokenEnvVar' is not supported for stdio transport.");
            if (httpHeaders is { Count: > 0 } || envHttpHeaders is { Count: > 0 })
                throw AppServerErrors.McpServerValidationFailed("HTTP headers are not supported for stdio transport.");
        }
        else
        {
            if (string.IsNullOrWhiteSpace(url))
                throw AppServerErrors.McpServerValidationFailed("'server.url' is required for streamableHttp transport.");
            if (!Uri.TryCreate(url, UriKind.Absolute, out _))
                throw AppServerErrors.McpServerValidationFailed("'server.url' must be an absolute URL.");
            if (!string.IsNullOrWhiteSpace(command) ||
                args is { Count: > 0 } ||
                env is { Count: > 0 } ||
                envVars is { Count: > 0 } ||
                !string.IsNullOrWhiteSpace(cwd))
            {
                throw AppServerErrors.McpServerValidationFailed("stdio-only fields are not supported for streamableHttp transport.");
            }
        }
    }

    internal static Contract.McpServerOrigin ToContract(McpServerOrigin origin) =>
        new()
        {
            Kind = string.IsNullOrWhiteSpace(origin.Kind) ? "workspace" : origin.Kind,
            PluginId = origin.PluginId,
            PluginDisplayName = origin.PluginDisplayName,
            DeclaredName = origin.DeclaredName,
            ThreadId = origin.ThreadId
        };

    private static string ToContract(McpToolApprovalMode mode) => mode switch
    {
        McpToolApprovalMode.Writes => "writes",
        McpToolApprovalMode.Approve => "approve",
        McpToolApprovalMode.Prompt => "prompt",
        _ => "auto"
    };

    private static McpToolApprovalMode ParseToolApprovalMode(string? value, string field) => value switch
    {
        null or "auto" => McpToolApprovalMode.Auto,
        "writes" => McpToolApprovalMode.Writes,
        "approve" => McpToolApprovalMode.Approve,
        "prompt" => McpToolApprovalMode.Prompt,
        _ => throw AppServerErrors.McpServerValidationFailed($"'{field}' must be 'auto', 'writes', 'approve', or 'prompt'.")
    };

    private static Dictionary<string, McpToolConfig>? ParseTools(IReadOnlyDictionary<string, Contract.McpToolConfig>? tools) =>
        tools?.ToDictionary(
            static pair => pair.Key,
            static pair => new McpToolConfig
            {
                ApprovalMode = pair.Value?.ApprovalMode is { IsSet: true, Value: { } mode }
                    ? ParseToolApprovalMode(mode, $"server.tools.{pair.Key}.approvalMode")
                    : null
            },
            StringComparer.Ordinal);

    private static Protocol.Optional<IReadOnlyList<string>?> ToolNames(List<string>? names) =>
        names is null ? default : new Protocol.Optional<IReadOnlyList<string>?>(names.ToArray());

    private static T? ValueOrDefault<T>(Protocol.Optional<T> value) =>
        value.IsSet ? value.Value : default;

    private static string NormalizeTransport(string? transport) =>
        transport?.Equals("streamableHttp", StringComparison.OrdinalIgnoreCase) == true
            || transport?.Equals("streamable-http", StringComparison.OrdinalIgnoreCase) == true
            || transport?.Equals("http", StringComparison.OrdinalIgnoreCase) == true
                ? "streamableHttp"
                : "stdio";
}
