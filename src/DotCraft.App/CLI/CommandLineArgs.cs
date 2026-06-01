using DotCraft.Acp;
using DotCraft.AppServer;
using DotCraft.Configuration;

namespace DotCraft.CLI;

/// <summary>
/// Lightweight, zero-dependency command-line argument parser for DotCraft.
///
/// <para>Supported usage forms:</para>
/// <list type="bullet">
/// <item><c>dotcraft exec "prompt"</c> — one-shot command-line agent run</item>
/// <item><c>dotcraft exec -</c> — one-shot run with the prompt read from stdin</item>
/// <item><c>dotcraft exec --remote ws://host:port/ws [--token T] "prompt"</c> — one-shot run connected to a remote AppServer</item>
/// <item><c>dotcraft app-server</c> — AppServer in stdio mode (backward-compatible)</item>
/// <item><c>dotcraft app-server --listen ws://host:port</c> — AppServer in pure WebSocket mode</item>
/// <item><c>dotcraft app-server --listen ws+stdio://host:port</c> — AppServer in stdio + WebSocket mode</item>
/// <item><c>dotcraft dashboard --workspace &lt;path&gt;</c> — standalone read-only Dashboard viewer</item>
/// <item><c>dotcraft hub</c> — workspace-independent local Hub process</item>
/// <item><c>dotcraft -acp</c> / <c>dotcraft acp</c> — ACP bridge (stdio to IDE; AppServer subprocess or <c>--remote</c>)</item>
/// </list>
///
/// <para>
/// The <c>--listen</c> URL scheme determines the AppServer transport:
/// <c>stdio://</c> (default), <c>ws://</c> (pure WebSocket), or <c>ws+stdio://</c> (both).
/// Host and port are embedded in the URL, avoiding separate flags.
/// </para>
/// </summary>
public sealed record CommandLineArgs
{
    /// <summary>
    /// The top-level execution mode determined from the command-line arguments.
    /// </summary>
    public enum RunMode
    {
        /// <summary>No executable mode was supplied.</summary>
        None,

        /// <summary>One-shot command-line agent run.</summary>
        Exec,

        /// <summary>AppServer subprocess mode (wire protocol server).</summary>
        AppServer,

        /// <summary>Gateway host for concurrent channels and automations.</summary>
        Gateway,

        /// <summary>ACP (Agent Communication Protocol) mode for IDE integration.</summary>
        Acp,

        /// <summary>One-shot non-interactive workspace setup.</summary>
        Setup,

        /// <summary>Workspace-independent local Hub process.</summary>
        Hub,

        /// <summary>Non-interactive skill verification and installation commands.</summary>
        Skill,

        /// <summary>Standalone read-only Dashboard viewer for an existing workspace.</summary>
        Dashboard,

        /// <summary>Interactive authentication commands (e.g. Sign in with ChatGPT).</summary>
        Auth,

        /// <summary>Read-only context export and search commands.</summary>
        Context
    }

    /// <summary>Top-level execution mode.</summary>
    public RunMode Mode { get; init; }

    /// <summary>
    /// <c>--listen</c> URL for <see cref="RunMode.AppServer"/> mode.
    /// <para>Examples: <c>stdio://</c>, <c>ws://127.0.0.1:9100</c>, <c>ws+stdio://127.0.0.1:9100</c>.</para>
    /// Null when not in AppServer mode or not specified (defaults to <c>stdio://</c>).
    /// </summary>
    public string? ListenUrl { get; init; }

    /// <summary>
    /// <c>--remote</c> URL for <see cref="RunMode.Exec"/> mode.
    /// When set, the CLI connects to an already-running AppServer via WebSocket
    /// instead of spawning a subprocess.
    /// <para>Example: <c>ws://127.0.0.1:9100/ws</c></para>
    /// </summary>
    public string? RemoteUrl { get; init; }

    /// <summary>
    /// <c>--token</c> for WebSocket authentication (both server-side and client-side).
    /// </summary>
    public string? Token { get; init; }

    /// <summary>
    /// Prompt text for <see cref="RunMode.Exec"/>. Null when stdin should be used or no prompt was supplied.
    /// </summary>
    public string? ExecPrompt { get; init; }

    /// <summary>
    /// Whether <see cref="RunMode.Exec"/> should read its prompt from stdin.
    /// </summary>
    public bool ExecReadStdin { get; init; }

    public string? SetupModel { get; init; }

    public string? SetupEndPoint { get; init; }

    public string? SetupApiKey { get; init; }

    public string? SetupProfile { get; init; }

    public string? SetupProviderMode { get; init; }

    public string? SetupProviderId { get; init; }

    public string? SetupProviderDisplayName { get; init; }

    public string? SetupProviderProtocol { get; init; }

    public string? SetupProviderTimeoutSeconds { get; init; }

    /// <summary>Authentication mode for the bootstrapped provider ("apiKey" or "chatgptOAuth").</summary>
    public string? SetupAuthMethod { get; init; }

    public bool SaveUserConfig { get; init; }

    public bool PreferExistingUserConfig { get; init; }

    public bool SetupSetUserDefault { get; init; }

    public bool SetupSkipProvider { get; init; }

    public string? SkillCommand { get; init; }

    public string? SkillCandidatePath { get; init; }

    public string? SkillName { get; init; }

    public string? SkillSource { get; init; }

    public bool SkillOverwrite { get; init; }

    public bool SkillJson { get; init; }

    public string? DashboardWorkspacePath { get; init; }

    public string? DashboardHost { get; init; }

    public int? DashboardPort { get; init; }

    /// <summary>Auth subject (currently only "openai").</summary>
    public string? AuthProvider { get; init; }

    /// <summary>Auth action: "login", "logout", "status".</summary>
    public string? AuthAction { get; init; }

    /// <summary>Optional provider id to bind/unbind to the ChatGPT account; defaults to "openai".</summary>
    public string? AuthProviderId { get; init; }

    /// <summary>When true, suppress browser launch and print the authorization URL only (for headless or CI).</summary>
    public bool AuthNoBrowser { get; init; }

    /// <summary>When true, skip the usage / rate-limit lookup in <c>auth openai status</c> (CI-friendly).</summary>
    public bool AuthNoUsage { get; init; }

    /// <summary>Context subcommand: "export" or "search".</summary>
    public string? ContextCommand { get; init; }

    /// <summary>Thread id supplied to <c>dotcraft context export</c>.</summary>
    public string? ContextThreadId { get; init; }

    /// <summary>Free-text query supplied to <c>dotcraft context search</c>.</summary>
    public string? ContextQuery { get; init; }

    /// <summary>Workspace path or direct <c>.craft</c> path supplied to context commands.</summary>
    public string? ContextWorkspacePath { get; init; }

    /// <summary>Optional output file for <c>dotcraft context export</c>.</summary>
    public string? ContextOutputPath { get; init; }

    /// <summary>Context export profile: "handoff" or "transcript".</summary>
    public string? ContextProfile { get; init; }

    /// <summary>Context export tool result mode: "none", "summary", or "full".</summary>
    public string? ContextToolResults { get; init; }

    /// <summary>Context export memory history mode: "none", "tail", or "full".</summary>
    public string? ContextHistory { get; init; }

    /// <summary>Maximum number of context search hits.</summary>
    public int? ContextLimit { get; init; }

    /// <summary>Context search status filter: "active", "archived", or "all".</summary>
    public string? ContextStatus { get; init; }

    /// <summary>When true, context search emits JSON.</summary>
    public bool ContextJson { get; init; }

    /// <summary>
    /// Whether this execution mode reserves stdout for a wire protocol (stdio-based JSON-RPC).
    /// When <c>true</c>, all console diagnostics must be redirected to stderr.
    /// </summary>
    public bool ReservesStdout { get; init; }

    // -------------------------------------------------------------------------
    // Parsing
    // -------------------------------------------------------------------------

    /// <summary>
    /// Parse raw command-line arguments into a <see cref="CommandLineArgs"/> instance.
    /// </summary>
    public static CommandLineArgs Parse(string[] args)
    {
        RunMode mode = RunMode.None;
        string? listenUrl = null;
        string? remoteUrl = null;
        string? token = null;
        string? execPrompt = null;
        var execReadStdin = false;
        var execPromptParts = new List<string>();
        string? setupModel = null;
        string? setupEndPoint = null;
        string? setupApiKey = null;
        string? setupProfile = null;
        string? setupProviderMode = null;
        string? setupProviderId = null;
        string? setupProviderDisplayName = null;
        string? setupProviderProtocol = null;
        string? setupProviderTimeoutSeconds = null;
        string? setupAuthMethod = null;
        var saveUserConfig = false;
        var preferExistingUserConfig = false;
        var setupSetUserDefault = false;
        var setupSkipProvider = false;
        string? skillCommand = null;
        string? skillCandidatePath = null;
        string? skillName = null;
        string? skillSource = null;
        var skillOverwrite = false;
        var skillJson = false;
        string? dashboardWorkspacePath = null;
        string? dashboardHost = null;
        int? dashboardPort = null;
        string? authProvider = null;
        string? authAction = null;
        string? authProviderId = null;
        var authNoBrowser = false;
        var authNoUsage = false;
        string? contextCommand = null;
        string? contextThreadId = null;
        string? contextQuery = null;
        string? contextWorkspacePath = null;
        string? contextOutputPath = null;
        string? contextProfile = null;
        string? contextToolResults = null;
        string? contextHistory = null;
        int? contextLimit = null;
        string? contextStatus = null;
        var contextJson = false;

        for (int i = 0; i < args.Length; i++)
        {
            var arg = args[i];

            if (arg.Equals("exec", StringComparison.OrdinalIgnoreCase))
            {
                mode = RunMode.Exec;
                continue;
            }

            // Sub-command: app-server
            if (arg.Equals("app-server", StringComparison.OrdinalIgnoreCase))
            {
                mode = RunMode.AppServer;
                continue;
            }

            if (arg.Equals("gateway", StringComparison.OrdinalIgnoreCase))
            {
                mode = RunMode.Gateway;
                continue;
            }

            if (arg.Equals("hub", StringComparison.OrdinalIgnoreCase))
            {
                mode = RunMode.Hub;
                continue;
            }

            if (arg.Equals("dashboard", StringComparison.OrdinalIgnoreCase))
            {
                mode = RunMode.Dashboard;
                continue;
            }

            if (arg.Equals("skill", StringComparison.OrdinalIgnoreCase))
            {
                mode = RunMode.Skill;
                if (i + 1 < args.Length && !args[i + 1].StartsWith("-", StringComparison.Ordinal))
                    skillCommand = args[++i];
                continue;
            }

            if (arg.Equals("setup", StringComparison.OrdinalIgnoreCase))
            {
                mode = RunMode.Setup;
                continue;
            }

            if (arg.Equals("auth", StringComparison.OrdinalIgnoreCase))
            {
                mode = RunMode.Auth;
                if (i + 1 < args.Length && !args[i + 1].StartsWith("-", StringComparison.Ordinal))
                    authProvider = args[++i];
                if (i + 1 < args.Length && !args[i + 1].StartsWith("-", StringComparison.Ordinal))
                    authAction = args[++i];
                continue;
            }

            if (arg.Equals("context", StringComparison.OrdinalIgnoreCase))
            {
                mode = RunMode.Context;
                if (i + 1 < args.Length && !args[i + 1].StartsWith("-", StringComparison.Ordinal))
                    contextCommand = args[++i];
                continue;
            }

            if (arg.Equals("--no-browser", StringComparison.OrdinalIgnoreCase))
            {
                authNoBrowser = true;
                continue;
            }

            if (arg.Equals("--no-usage", StringComparison.OrdinalIgnoreCase))
            {
                authNoUsage = true;
                continue;
            }

            // Sub-command: acp / -acp
            if (arg.Equals("acp", StringComparison.OrdinalIgnoreCase) ||
                arg.Equals("-acp", StringComparison.OrdinalIgnoreCase))
            {
                mode = RunMode.Acp;
                continue;
            }

            // --listen <URL>  (app-server transport)
            if (arg.Equals("--listen", StringComparison.OrdinalIgnoreCase))
            {
                listenUrl = ConsumeNext(args, ref i, "--listen");
                continue;
            }

            // --remote <URL>  (CLI connects to remote AppServer)
            if (arg.Equals("--remote", StringComparison.OrdinalIgnoreCase))
            {
                remoteUrl = ConsumeNext(args, ref i, "--remote");
                continue;
            }

            // --token <VALUE>
            if (arg.Equals("--token", StringComparison.OrdinalIgnoreCase))
            {
                token = ConsumeNext(args, ref i, "--token");
                continue;
            }

            if (arg.Equals("--model", StringComparison.OrdinalIgnoreCase))
            {
                setupModel = ConsumeNext(args, ref i, "--model");
                continue;
            }

            if (arg.Equals("--language", StringComparison.OrdinalIgnoreCase))
            {
                _ = ConsumeNext(args, ref i, "--language");
                continue;
            }

            if (arg.Equals("--endpoint", StringComparison.OrdinalIgnoreCase))
            {
                setupEndPoint = ConsumeNext(args, ref i, "--endpoint");
                continue;
            }

            if (arg.Equals("--api-key", StringComparison.OrdinalIgnoreCase))
            {
                setupApiKey = ConsumeNext(args, ref i, "--api-key");
                continue;
            }

            if (arg.Equals("--profile", StringComparison.OrdinalIgnoreCase))
            {
                if (mode == RunMode.Context)
                    contextProfile = ConsumeNext(args, ref i, "--profile");
                else
                    setupProfile = ConsumeNext(args, ref i, "--profile");
                continue;
            }

            if (arg.Equals("--provider-mode", StringComparison.OrdinalIgnoreCase))
            {
                setupProviderMode = ConsumeNext(args, ref i, "--provider-mode");
                continue;
            }

            if (arg.Equals("--provider-id", StringComparison.OrdinalIgnoreCase))
            {
                setupProviderId = ConsumeNext(args, ref i, "--provider-id");
                continue;
            }

            if (arg.Equals("--provider-display-name", StringComparison.OrdinalIgnoreCase))
            {
                setupProviderDisplayName = ConsumeNext(args, ref i, "--provider-display-name");
                continue;
            }

            if (arg.Equals("--provider-protocol", StringComparison.OrdinalIgnoreCase))
            {
                setupProviderProtocol = ConsumeNext(args, ref i, "--provider-protocol");
                continue;
            }

            if (arg.Equals("--provider-timeout-seconds", StringComparison.OrdinalIgnoreCase))
            {
                setupProviderTimeoutSeconds = ConsumeNext(args, ref i, "--provider-timeout-seconds");
                continue;
            }

            if (arg.Equals("--auth-method", StringComparison.OrdinalIgnoreCase))
            {
                setupAuthMethod = ConsumeNext(args, ref i, "--auth-method");
                continue;
            }

            if (arg.Equals("--save-user-config", StringComparison.OrdinalIgnoreCase))
            {
                saveUserConfig = true;
                continue;
            }

            if (arg.Equals("--prefer-existing-user-config", StringComparison.OrdinalIgnoreCase))
            {
                preferExistingUserConfig = true;
                continue;
            }

            if (arg.Equals("--set-user-default", StringComparison.OrdinalIgnoreCase))
            {
                setupSetUserDefault = true;
                continue;
            }

            if (arg.Equals("--skip-provider", StringComparison.OrdinalIgnoreCase))
            {
                setupSkipProvider = true;
                continue;
            }

            if (arg.Equals("--candidate", StringComparison.OrdinalIgnoreCase))
            {
                skillCandidatePath = ConsumeNext(args, ref i, "--candidate");
                continue;
            }

            if (arg.Equals("--name", StringComparison.OrdinalIgnoreCase))
            {
                skillName = ConsumeNext(args, ref i, "--name");
                continue;
            }

            if (arg.Equals("--source", StringComparison.OrdinalIgnoreCase))
            {
                skillSource = ConsumeNext(args, ref i, "--source");
                continue;
            }

            if (arg.Equals("--overwrite", StringComparison.OrdinalIgnoreCase))
            {
                skillOverwrite = true;
                continue;
            }

            if (arg.Equals("--json", StringComparison.OrdinalIgnoreCase))
            {
                if (mode == RunMode.Context)
                    contextJson = true;
                else
                    skillJson = true;
                continue;
            }

            if (arg.Equals("--workspace", StringComparison.OrdinalIgnoreCase))
            {
                if (mode == RunMode.Context)
                    contextWorkspacePath = ConsumeNext(args, ref i, "--workspace");
                else
                    dashboardWorkspacePath = ConsumeNext(args, ref i, "--workspace");
                continue;
            }

            if (arg.Equals("--thread", StringComparison.OrdinalIgnoreCase))
            {
                contextThreadId = ConsumeNext(args, ref i, "--thread");
                continue;
            }

            if (arg.Equals("--query", StringComparison.OrdinalIgnoreCase))
            {
                contextQuery = ConsumeNext(args, ref i, "--query");
                continue;
            }

            if (arg.Equals("--output", StringComparison.OrdinalIgnoreCase))
            {
                contextOutputPath = ConsumeNext(args, ref i, "--output");
                continue;
            }

            if (arg.Equals("--tool-results", StringComparison.OrdinalIgnoreCase))
            {
                contextToolResults = ConsumeNext(args, ref i, "--tool-results");
                continue;
            }

            if (arg.Equals("--history", StringComparison.OrdinalIgnoreCase))
            {
                contextHistory = ConsumeNext(args, ref i, "--history");
                continue;
            }

            if (arg.Equals("--limit", StringComparison.OrdinalIgnoreCase))
            {
                contextLimit = ParsePositiveInt(ConsumeNext(args, ref i, "--limit"), "--limit");
                continue;
            }

            if (arg.Equals("--status", StringComparison.OrdinalIgnoreCase))
            {
                contextStatus = ConsumeNext(args, ref i, "--status");
                continue;
            }

            if (arg.Equals("--host", StringComparison.OrdinalIgnoreCase))
            {
                dashboardHost = ConsumeNext(args, ref i, "--host");
                continue;
            }

            if (arg.Equals("--port", StringComparison.OrdinalIgnoreCase))
            {
                dashboardPort = ParsePort(ConsumeNext(args, ref i, "--port"), "--port");
                continue;
            }

            // Support --listen=<url> / --remote=<url> / --token=<value> forms
            if (TryParseKeyValue(arg, "--listen", out var listenValue))
            {
                listenUrl = listenValue;
                continue;
            }

            if (TryParseKeyValue(arg, "--remote", out var remoteValue))
            {
                remoteUrl = remoteValue;
                continue;
            }

            if (TryParseKeyValue(arg, "--token", out var tokenValue))
            {
                token = tokenValue;
                continue;
            }

            if (TryParseKeyValue(arg, "--model", out var modelValue))
            {
                setupModel = modelValue;
                continue;
            }

            if (TryParseKeyValue(arg, "--language", out _))
            {
                continue;
            }

            if (TryParseKeyValue(arg, "--endpoint", out var endpointValue))
            {
                setupEndPoint = endpointValue;
                continue;
            }

            if (TryParseKeyValue(arg, "--api-key", out var apiKeyValue))
            {
                setupApiKey = apiKeyValue;
                continue;
            }

            if (TryParseKeyValue(arg, "--profile", out var profileValue))
            {
                if (mode == RunMode.Context)
                    contextProfile = profileValue;
                else
                    setupProfile = profileValue;
                continue;
            }

            if (TryParseKeyValue(arg, "--provider-mode", out var providerModeValue))
            {
                setupProviderMode = providerModeValue;
                continue;
            }

            if (TryParseKeyValue(arg, "--provider-id", out var providerIdValue))
            {
                setupProviderId = providerIdValue;
                continue;
            }

            if (TryParseKeyValue(arg, "--provider-display-name", out var providerDisplayNameValue))
            {
                setupProviderDisplayName = providerDisplayNameValue;
                continue;
            }

            if (TryParseKeyValue(arg, "--provider-protocol", out var providerProtocolValue))
            {
                setupProviderProtocol = providerProtocolValue;
                continue;
            }

            if (TryParseKeyValue(arg, "--provider-timeout-seconds", out var providerTimeoutValue))
            {
                setupProviderTimeoutSeconds = providerTimeoutValue;
                continue;
            }

            if (TryParseKeyValue(arg, "--auth-method", out var authMethodValue))
            {
                setupAuthMethod = authMethodValue;
                continue;
            }

            if (TryParseKeyValue(arg, "--candidate", out var candidateValue))
            {
                skillCandidatePath = candidateValue;
                continue;
            }

            if (TryParseKeyValue(arg, "--name", out var nameValue))
            {
                skillName = nameValue;
                continue;
            }

            if (TryParseKeyValue(arg, "--source", out var sourceValue))
            {
                skillSource = sourceValue;
                continue;
            }

            if (TryParseKeyValue(arg, "--workspace", out var workspaceValue))
            {
                if (mode == RunMode.Context)
                    contextWorkspacePath = workspaceValue;
                else
                    dashboardWorkspacePath = workspaceValue;
                continue;
            }

            if (TryParseKeyValue(arg, "--thread", out var threadValue))
            {
                contextThreadId = threadValue;
                continue;
            }

            if (TryParseKeyValue(arg, "--query", out var queryValue))
            {
                contextQuery = queryValue;
                continue;
            }

            if (TryParseKeyValue(arg, "--output", out var outputValue))
            {
                contextOutputPath = outputValue;
                continue;
            }

            if (TryParseKeyValue(arg, "--tool-results", out var toolResultsValue))
            {
                contextToolResults = toolResultsValue;
                continue;
            }

            if (TryParseKeyValue(arg, "--history", out var historyValue))
            {
                contextHistory = historyValue;
                continue;
            }

            if (TryParseKeyValue(arg, "--limit", out var limitValue))
            {
                contextLimit = ParsePositiveInt(limitValue, "--limit");
                continue;
            }

            if (TryParseKeyValue(arg, "--status", out var statusValue))
            {
                contextStatus = statusValue;
                continue;
            }

            if (TryParseKeyValue(arg, "--host", out var hostValue))
            {
                dashboardHost = hostValue;
                continue;
            }

            if (TryParseKeyValue(arg, "--port", out var portValue))
            {
                dashboardPort = ParsePort(portValue, "--port");
                continue;
            }

            if (mode == RunMode.Exec)
            {
                execPromptParts.Add(arg);
            }
            // Unknown arguments outside exec are silently ignored (forward-compatible).
        }

        if (mode == RunMode.Exec)
        {
            execPrompt = string.Join(" ", execPromptParts).Trim();
            if (execPrompt == "-")
            {
                execPrompt = null;
                execReadStdin = true;
            }
        }

        // Determine whether stdout is reserved for a wire protocol.
        // - ACP always uses stdio JSON-RPC.
        // - AppServer uses stdio unless the listen URL is pure WebSocket (ws://).
        var reservesStdout = mode switch
        {
            RunMode.Acp => true,
            RunMode.AppServer => !IsPureWebSocketListen(listenUrl),
            RunMode.Exec => true,
            _ => false
        };

        return new CommandLineArgs
        {
            Mode = mode,
            ListenUrl = listenUrl,
            RemoteUrl = remoteUrl,
            Token = token,
            ExecPrompt = execPrompt,
            ExecReadStdin = execReadStdin,
            SetupModel = setupModel,
            SetupEndPoint = setupEndPoint,
            SetupApiKey = setupApiKey,
            SetupProfile = setupProfile,
            SetupProviderMode = setupProviderMode,
            SetupProviderId = setupProviderId,
            SetupProviderDisplayName = setupProviderDisplayName,
            SetupProviderProtocol = setupProviderProtocol,
            SetupProviderTimeoutSeconds = setupProviderTimeoutSeconds,
            SetupAuthMethod = setupAuthMethod,
            SaveUserConfig = saveUserConfig,
            PreferExistingUserConfig = preferExistingUserConfig,
            SetupSetUserDefault = setupSetUserDefault,
            SetupSkipProvider = setupSkipProvider,
            SkillCommand = skillCommand,
            SkillCandidatePath = skillCandidatePath,
            SkillName = skillName,
            SkillSource = skillSource,
            SkillOverwrite = skillOverwrite,
            SkillJson = skillJson,
            DashboardWorkspacePath = dashboardWorkspacePath,
            DashboardHost = dashboardHost,
            DashboardPort = dashboardPort,
            AuthProvider = authProvider,
            AuthAction = authAction,
            // --provider-id is shared with setup; route it to auth when in Auth mode.
            AuthProviderId = mode == RunMode.Auth ? setupProviderId : authProviderId,
            AuthNoBrowser = authNoBrowser,
            AuthNoUsage = authNoUsage,
            ContextCommand = contextCommand,
            ContextThreadId = contextThreadId,
            ContextQuery = contextQuery,
            ContextWorkspacePath = contextWorkspacePath,
            ContextOutputPath = contextOutputPath,
            ContextProfile = contextProfile,
            ContextToolResults = contextToolResults,
            ContextHistory = contextHistory,
            ContextLimit = contextLimit,
            ContextStatus = contextStatus,
            ContextJson = contextJson,
            ReservesStdout = reservesStdout
        };
    }

    // -------------------------------------------------------------------------
    // Config application
    // -------------------------------------------------------------------------

    /// <summary>
    /// Apply parsed CLI overrides onto the loaded <see cref="AppConfig"/>.
    /// Command-line arguments take precedence over config.json values.
    /// </summary>
    public void ApplyTo(AppConfig config)
    {
        switch (Mode)
        {
            case RunMode.Acp:
            {
                var acp = new AcpConfig { Enabled = true };
                if (!string.IsNullOrWhiteSpace(RemoteUrl))
                {
                    acp.AppServerUrl = RemoteUrl;
                    acp.AppServerToken = Token;
                }

                config.SetSection("Acp", acp);
                config.DashBoard.Enabled = false;
                break;
            }

            case RunMode.AppServer:
                ApplyAppServerConfig(config);
                break;

            case RunMode.Gateway:
                break;

            case RunMode.Dashboard:
                ApplyDashboardConfig(config);
                break;

            case RunMode.Exec:
                ApplyCliConfig(config);
                break;

            case RunMode.None:
            case RunMode.Setup:
                break;

            case RunMode.Hub:
            case RunMode.Skill:
            case RunMode.Auth:
            case RunMode.Context:
                break;
        }
    }

    private void ApplyDashboardConfig(AppConfig config)
    {
        if (!string.IsNullOrWhiteSpace(DashboardHost))
            config.DashBoard.Host = DashboardHost.Trim();

        if (DashboardPort.HasValue)
            config.DashBoard.Port = DashboardPort.Value;
    }

    private void ApplyAppServerConfig(AppConfig config)
    {
        var (appServerMode, wsHost, wsPort) = ParseListenUrl(ListenUrl);

        var appServerConfig = new AppServerConfig { Mode = appServerMode };

        // Apply WebSocket settings when the mode includes WebSocket
        if (appServerMode is AppServerMode.WebSocket or AppServerMode.StdioAndWebSocket)
        {
            appServerConfig.WebSocket = new WebSocketServerConfig
            {
                Host = wsHost ?? "127.0.0.1",
                Port = wsPort ?? 9100,
                Token = Token
            };
        }

        config.SetSection("AppServer", appServerConfig);
    }

    private void ApplyCliConfig(AppConfig config)
    {
        if (RemoteUrl is null)
            return;

        // --remote overrides CliConfig.AppServerUrl
        var cliConfig = config.GetSection<CliConfig>("CLI");
        cliConfig.AppServerUrl = RemoteUrl;

        if (Token is not null)
            cliConfig.AppServerToken = Token;

        config.SetSection("CLI", cliConfig);
    }

    // -------------------------------------------------------------------------
    // URL parsing helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Parse a <c>--listen</c> URL into an <see cref="AppServerMode"/> and optional host/port.
    /// </summary>
    internal static (AppServerMode Mode, string? Host, int? Port) ParseListenUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return (AppServerMode.Stdio, null, null);

        // stdio:// → pure stdio
        if (url.StartsWith("stdio://", StringComparison.OrdinalIgnoreCase))
            return (AppServerMode.Stdio, null, null);

        // ws+stdio://host:port → stdio + WebSocket
        if (url.StartsWith("ws+stdio://", StringComparison.OrdinalIgnoreCase))
        {
            var (host, port) = ParseHostPort(url["ws+stdio://".Length..]);
            return (AppServerMode.StdioAndWebSocket, host, port);
        }

        // ws://host:port → pure WebSocket
        if (url.StartsWith("ws://", StringComparison.OrdinalIgnoreCase))
        {
            var (host, port) = ParseHostPort(url["ws://".Length..]);
            return (AppServerMode.WebSocket, host, port);
        }

        // wss://host:port is not supported by the embedded listener yet.
        // Reject explicitly instead of silently downgrading to plain ws/http.
        if (url.StartsWith("wss://", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("The wss:// scheme is not currently supported. Use ws:// or terminate TLS in front of AppServer.");

        // Unrecognized scheme — treat as stdio with a warning
        Console.Error.WriteLine($"[CLI] Warning: unrecognized --listen URL scheme '{url}', defaulting to stdio.");
        return (AppServerMode.Stdio, null, null);
    }

    private static (string Host, int? Port) ParseHostPort(string hostPort)
    {
        // Remove trailing path (e.g., /ws)
        var pathIndex = hostPort.IndexOf('/');
        if (pathIndex >= 0)
            hostPort = hostPort[..pathIndex];

        var colonIndex = hostPort.LastIndexOf(':');
        if (colonIndex < 0)
            return (hostPort, null);

        var host = hostPort[..colonIndex];
        if (int.TryParse(hostPort[(colonIndex + 1)..], out var port))
            return (host, port);

        return (hostPort, null);
    }

    /// <summary>
    /// Returns <c>true</c> when the listen URL uses pure WebSocket mode (ws:// or wss://)
    /// and does NOT include stdio.
    /// </summary>
    private static bool IsPureWebSocketListen(string? listenUrl)
    {
        if (string.IsNullOrWhiteSpace(listenUrl))
            return false;

        // ws+stdio:// includes stdio, so it reserves stdout
        if (listenUrl.StartsWith("ws+stdio://", StringComparison.OrdinalIgnoreCase))
            return false;

        return listenUrl.StartsWith("ws://", StringComparison.OrdinalIgnoreCase) ||
               listenUrl.StartsWith("wss://", StringComparison.OrdinalIgnoreCase);
    }

    // -------------------------------------------------------------------------
    // Generic argument parsing helpers
    // -------------------------------------------------------------------------

    private static string ConsumeNext(string[] args, ref int index, string flag)
    {
        if (index + 1 >= args.Length)
            throw new ArgumentException($"Missing value after '{flag}'.");
        return args[++index];
    }

    private static int ParsePort(string value, string flag)
    {
        if (int.TryParse(value, out var port) && port is >= 1 and <= 65535)
            return port;

        throw new ArgumentException($"Invalid value for '{flag}'. Expected a TCP port between 1 and 65535.");
    }

    private static int ParsePositiveInt(string value, string flag)
    {
        if (int.TryParse(value, out var parsed) && parsed > 0)
            return parsed;

        throw new ArgumentException($"Invalid value for '{flag}'. Expected a positive integer.");
    }

    private static bool TryParseKeyValue(string arg, string key, out string value)
    {
        var prefix = key + "=";
        if (arg.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            value = arg[prefix.Length..];
            return true;
        }

        value = default!;
        return false;
    }
}
