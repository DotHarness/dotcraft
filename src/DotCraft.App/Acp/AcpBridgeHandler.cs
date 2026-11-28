using System.Collections.Concurrent;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.AppServer;
using DotCraft.CLI;
using DotCraft.Commands.Core;
using DotCraft.Common;
using DotCraft.Hooks;
using DotCraft.Memory;
using DotCraft.Sessions;
using DotCraft.Sessions.Wire;
using DotHarness.Acp.Rpc;
using DotHarness.Acp.Transport;
using DotHarness.Acp.V1;
using Spectre.Console;
using Contract = DotCraft.Protocol.AppServer;

namespace DotCraft.Acp;

public sealed partial class AcpBridgeHandler(
    AppServerWireClient wire,
    string workspacePath,
    HookRunner? hookRunner = null,
    PlanStore? planStore = null,
    AcpLogger? logger = null,
    AppServerProcess? appServerProcess = null,
    int extForwardTimeoutSeconds = 120,
    int permissionRequestTimeoutSeconds = 120,
    CommandRegistry? commandRegistry = null) : IAcpAgentHandler
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private static readonly JsonSerializerOptions WireReadOptions = SessionWireJsonOptions.Default;

    private AcpClientCaller? _client;
    private AcpConnection? _connection;
    private CancellationToken _bridgeLifetime;

    private ClientCapabilities? _clientCapabilities;
    private readonly ConcurrentDictionary<string, string?> _activeTurnIds = new();
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _activePrompts = new();
    private readonly ConcurrentDictionary<string, string> _activeToolNames = new();
    private readonly ConcurrentDictionary<string, AcpRuntimeToolDescriptor> _runtimeToolDescriptors = new();
    private IReadOnlyList<RuntimeDynamicToolDeclarationSpec> _runtimeDynamicTools = [];

    private readonly AppServerProcess? _appServerProcess = appServerProcess;
    private readonly TimeSpan _extForwardTimeout = TimeSpan.FromSeconds(extForwardTimeoutSeconds);
    private readonly TimeSpan _permissionRequestTimeout = TimeSpan.FromSeconds(permissionRequestTimeoutSeconds);

    public const int ProtocolVersion = 1;

    public async Task RunAsync(IAcpTransport transport, CancellationToken ct = default)
    {
        _bridgeLifetime = ct;
        await using var agent = new AcpV1Agent(transport, (client, connection) =>
        {
            _client = client;
            _connection = connection;
            return this;
        });
        try
        {
            await agent.Connection.Completion.WaitAsync(ct).ConfigureAwait(false);
        }
        catch (TimeoutException ex)
        {
            logger?.LogError("ACP connection timed out", ex);
            throw;
        }
    }

    private AcpClientCaller Client => _client ?? throw new InvalidOperationException("ACP bridge is not running.");

    private AcpConnection Connection => _connection ?? throw new InvalidOperationException("ACP bridge is not running.");

    public async ValueTask<InitializeResponse> InitializeAsync(InitializeRequest parameters, CancellationToken ct)
    {
        _clientCapabilities = parameters.ClientCapabilities.GetValueOrDefault();

        if (!AcpRuntimeTools.TryBuildRuntimeTools(_clientCapabilities, out var dynamicTools, out var runtimeDescriptors, out var dynamicToolError))
            throw new AcpRpcException(-32602, dynamicToolError);

        _runtimeDynamicTools = dynamicTools;
        _runtimeToolDescriptors.Clear();
        foreach (var descriptor in runtimeDescriptors)
            _runtimeToolDescriptors[AcpRuntimeTools.RuntimeToolKey(descriptor.Namespace, descriptor.Name)] = descriptor;

        var acpExt = AcpRuntimeTools.BuildAcpClientCapability(_clientCapabilities, runtimeDescriptors);

        try
        {
            await wire.InitializeAsync(
                clientName: "dotcraft-acp",
                clientVersion: AppVersion.Informational,
                approvalSupport: true,
                streamingSupport: true,
                optOutMethods: null,
                acpExtensions: acpExt).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            var detail = ex.Message;
            if (_appServerProcess is { IsRunning: false })
            {
                var stderr = _appServerProcess.RecentStderr.Trim();
                detail = $"AppServer subprocess exited (code {_appServerProcess.ExitCode}). " +
                         (string.IsNullOrEmpty(stderr) ? "No stderr captured." : stderr);
            }
            else if (ex is TaskCanceledException)
            {
                detail = "Wire request was canceled (AppServer connection closed or timed out).";
            }

            logger?.LogError("Wire initialize failed", ex);
            logger?.LogError(detail);
            throw new AcpRpcException(-32603, detail);
        }

        wire.ServerRequestHandler = OnWireServerRequestAsync;

        var result = new InitializeResponse
        {
            ProtocolVersion = new(ProtocolVersion),
            AgentCapabilities = new AgentCapabilities
            {
                LoadSession = true,
                SessionCapabilities = new SessionCapabilities
                {
                    List = new SessionListCapabilities(),
                    Delete = new SessionDeleteCapabilities()
                },
                PromptCapabilities = new PromptCapabilities
                {
                    Image = true,
                    EmbeddedContext = true
                },
                McpCapabilities = new McpCapabilities { Http = true }
            },
            AgentInfo = new Implementation
            {
                Name = "DotCraft",
                Title = "DotCraft AI Agent",
                Version = AppVersion.Short
            }
        };

        var caps = new List<string>();
        var fs = _clientCapabilities?.Fs.GetValueOrDefault();
        if (fs?.ReadTextFile.GetValueOrDefault() == true) caps.Add("fs.read");
        if (fs?.WriteTextFile.GetValueOrDefault() == true) caps.Add("fs.write");
        if (_clientCapabilities?.Terminal.GetValueOrDefault() == true) caps.Add("terminal");
        var extensionFamilies = AcpRuntimeTools.GetCustomExtensionFamilies(runtimeDescriptors);
        caps.AddRange(extensionFamilies.Select(e => $"ext:{e}"));
        if (_runtimeDynamicTools.Count > 0)
            caps.Add($"runtimeTools:{_runtimeDynamicTools.Count}");
        var capsStr = caps.Count > 0 ? string.Join(", ", caps) : "none";
        logger?.LogEvent($"Initialized (client capabilities: {capsStr})");
        AnsiConsole.MarkupLine($"[green][[ACP]][/] Initialized via AppServer (client capabilities: {Markup.Escape(capsStr)})");
        return result;
    }

    private IReadOnlyList<RuntimeDynamicToolDeclarationSpec>? RuntimeDynamicToolsOrNull() =>
        _runtimeDynamicTools.Count == 0 ? null : _runtimeDynamicTools;

    private static void ThrowIfWireError(JsonDocument doc, string context)
    {
        if (!doc.RootElement.TryGetProperty("error", out var error))
            return;
        var message = error.TryGetProperty("message", out var msg)
            ? msg.GetString() ?? "Unknown error"
            : "Unknown error";
        throw new AcpRpcException(-32603, string.IsNullOrEmpty(context) ? message : $"{context}: {message}");
    }

    private static string ToolCallKey(string sessionId, string callId) =>
        $"{sessionId}\u001F{callId}";

    private static string? TryGetStringProperty(JsonElement obj, string name)
    {
        if (obj.ValueKind != JsonValueKind.Object)
            return null;

        foreach (var property in obj.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)
                && property.Value.ValueKind == JsonValueKind.String)
            {
                return AcpRuntimeTools.NormalizeOptionalString(property.Value.GetString());
            }
        }

        return null;
    }

    private static string? TryGetCaseInsensitiveString(JsonElement obj, string name)
    {
        foreach (var property in obj.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)
                && property.Value.ValueKind == JsonValueKind.String)
            {
                return property.Value.GetString();
            }
        }

        return null;
    }

    private static void SetCaseInsensitiveStringProperty(JsonObject obj, string key, string? value)
    {
        var existingKey = obj
            .Select(kv => kv.Key)
            .FirstOrDefault(k => string.Equals(k, key, StringComparison.OrdinalIgnoreCase));

        if (string.IsNullOrWhiteSpace(value))
        {
            if (existingKey != null)
                obj.Remove(existingKey);
            return;
        }

        obj[existingKey ?? key] = value;
    }

    private sealed record ThreadConfigurationState(string Mode, string? Model, JsonObject? Configuration);
}
