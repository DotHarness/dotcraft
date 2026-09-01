using System.Text;
using System.Text.Json;
using DotCraft.CLI;
using DotCraft.AppServer;
using DotCraft.Diagnostics;
using DotCraft.Configuration;
using DotCraft.Hub;
using DotCraft.Hosting;
using DotCraft.Harness;
using DotCraft.Runtime;
using DotCraft.Text;
using DotCraft.Modules;
using DotCraft.Logging;
using DotCraft.DynamicWorkflows;
using DotCraft.OpenSandbox;
using DotCraft.RemoteTools;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Spectre.Console;
using DotCraft.Sessions.Wire;

Console.OutputEncoding = Encoding.UTF8;

if (args.Length > 0 && args[0].Equals("tool-host", StringComparison.OrdinalIgnoreCase))
{
    using var toolHostCts = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; toolHostCts.Cancel(); };
    Environment.Exit(await RemoteToolHostCliRunner.RunAsync(
        args.Skip(1).ToArray(),
        Console.Out,
        Console.Error,
        toolHostCts.Token));
    return;
}

if (args.Length > 0 && args[0].Equals("stack", StringComparison.OrdinalIgnoreCase))
{
    using var stackCts = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; stackCts.Cancel(); };
    Environment.Exit(await StackCliRunner.RunAsync(args.Skip(1).ToArray(), Console.Out, Console.Error, stackCts.Token));
    return;
}

// -------------------------------------------------------------------------
// 1. Parse command-line arguments
// -------------------------------------------------------------------------
var cliArgs = CommandLineArgs.Parse(args);
var isHeadless = CliStartup.IsHeadlessMode(cliArgs.Mode);

// -------------------------------------------------------------------------
// 2. Prepare subprocess environment (stdout → stderr, ignore Ctrl+C)
//    Only needed when the process reserves stdout for a wire protocol.
// -------------------------------------------------------------------------
if (cliArgs.ReservesStdout)
{
    SubprocessEnvironment.Prepare();
}

if (cliArgs.Mode == CommandLineArgs.RunMode.WorkflowWorker)
{
    Environment.Exit(await WorkflowWorkerRunner.RunAsync(
        Console.OpenStandardInput(),
        Console.OpenStandardOutput(),
        Console.OpenStandardError(),
        CancellationToken.None));
    return;
}

// -------------------------------------------------------------------------
// 3. Hub mode: global, workspace-independent local coordinator shell.
// -------------------------------------------------------------------------
if (cliArgs.Mode == CommandLineArgs.RunMode.Hub)
{
    var hubPaths = HubPaths.ForCurrentUser();
    if (args.Length >= 2 && args[1].Equals("set-runtime", StringComparison.OrdinalIgnoreCase))
    {
        var runtimeTools = ParseHubRuntimeTools(args.Skip(2).ToArray());
        var store = new HubRuntimeToolsStore(hubPaths.RuntimeToolsPath);
        var merged = store.MergeAndSave(runtimeTools);
        await Console.Out.WriteLineAsync($"Saved Hub runtime tools to {hubPaths.RuntimeToolsPath}");
        if (!string.IsNullOrWhiteSpace(merged.NodeBin))
            await Console.Out.WriteLineAsync($"Node: {merged.NodeBin}");
        if (!string.IsNullOrWhiteSpace(merged.ModulesDir))
            await Console.Out.WriteLineAsync($"Modules: {merged.ModulesDir}");
        return;
    }

    AppConfig globalConfig;
    try
    {
        globalConfig = AppConfig.Load(hubPaths.GlobalConfigPath);
    }
    catch (Exception ex)
    {
        using var failureLoggerFactory = DotCraftLoggingFactory.CreateHub(
            new AppConfig.LoggingConfig(),
            hubPaths.CraftHomePath);
        failureLoggerFactory.CreateLogger("DotCraft.Hub.Startup")
            .LogCritical(ex, "Failed to load Hub configuration from {ConfigPath}", hubPaths.GlobalConfigPath);
        throw;
    }
    cliArgs.ApplyTo(globalConfig);

    using var hubLoggerFactory = DotCraftLoggingFactory.CreateHub(globalConfig.Logging, hubPaths.CraftHomePath);
    var hubLogger = hubLoggerFactory.CreateLogger("DotCraft.Hub");
    try
    {
        var hubConfig = globalConfig.GetSection<HubConfig>("Hub");
        await using var hubHost = new HubHost(hubConfig, hubPaths, loggerFactory: hubLoggerFactory);
        await hubHost.RunAsync();
    }
    catch (Exception ex)
    {
        hubLogger.LogCritical(ex, "DotCraft Hub terminated unexpectedly");
        throw;
    }
    return;
}

static HubRuntimeToolsRequest ParseHubRuntimeTools(string[] args)
{
    var result = new HubRuntimeToolsRequest();
    for (var i = 0; i < args.Length; i++)
    {
        var arg = args[i];
        if (arg.Equals("--node-bin", StringComparison.OrdinalIgnoreCase))
        {
            result.NodeBin = ConsumeHubRuntimeValue(args, ref i, "--node-bin");
            continue;
        }

        if (arg.Equals("--modules-dir", StringComparison.OrdinalIgnoreCase))
        {
            result.ModulesDir = ConsumeHubRuntimeValue(args, ref i, "--modules-dir");
            continue;
        }

        if (arg.Equals("--ripgrep-path", StringComparison.OrdinalIgnoreCase))
        {
            result.RipgrepPath = ConsumeHubRuntimeValue(args, ref i, "--ripgrep-path");
            continue;
        }

        if (arg.Equals("--default-plugin-registry-url", StringComparison.OrdinalIgnoreCase))
        {
            result.DefaultPluginRegistryUrl = ConsumeHubRuntimeValue(args, ref i, "--default-plugin-registry-url");
            continue;
        }

        if (arg.Equals("--electron-run-as-node", StringComparison.OrdinalIgnoreCase)
            || arg.Equals("--node-run-as-node", StringComparison.OrdinalIgnoreCase))
        {
            result.NodeRunAsNode = true;
            continue;
        }

        throw new ArgumentException($"Unknown hub set-runtime option: {arg}");
    }

    return result;
}

static string ConsumeHubRuntimeValue(string[] args, ref int index, string option)
{
    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
        throw new ArgumentException($"Missing value for {option}.");
    index++;
    return args[index];
}

if (cliArgs.Mode == CommandLineArgs.RunMode.Dashboard)
{
    var result = await DashboardCliHost.RunAsync(cliArgs);
    Environment.Exit(result);
    return;
}

if (cliArgs.Mode == CommandLineArgs.RunMode.Auth)
{
    // Authentication commands (e.g. Sign in with ChatGPT) operate on the global ~/.craft directory
    // and do not require a workspace. Run before workspace discovery.
    using var ctsAuth = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; ctsAuth.Cancel(); };
    var authResult = await AuthCliRunner.RunAsync(cliArgs, ctsAuth.Token);
    Environment.Exit(authResult);
    return;
}

if (cliArgs.Mode == CommandLineArgs.RunMode.ModelCatalog)
{
    using var ctsCatalog = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; ctsCatalog.Cancel(); };
    var catalogResult = await ModelCatalogCliRunner.RunAsync(cliArgs, ctsCatalog.Token);
    Environment.Exit(catalogResult);
    return;
}

// -------------------------------------------------------------------------
// 4. Workspace discovery & initialization
// -------------------------------------------------------------------------
var workspacePath = Directory.GetCurrentDirectory();
var botPath = Path.GetFullPath(".craft");
var workspaceJustInitialized = false;

if (cliArgs.Mode == CommandLineArgs.RunMode.Context)
{
    using var ctsContext = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; ctsContext.Cancel(); };
    var result = await ContextCliRunner.RunAsync(cliArgs, Console.Out, Console.Error, ctsContext.Token);
    Environment.Exit(result);
    return;
}

if (cliArgs.Mode == CommandLineArgs.RunMode.Skill)
{
    var result = await SkillCliRunner.RunAsync(botPath, cliArgs, Console.Out, Console.Error);
    Environment.Exit(result);
    return;
}

if (cliArgs.Mode == CommandLineArgs.RunMode.Setup)
{
    static WorkspaceSetupProviderMode ResolveSetupProviderMode(CommandLineArgs cliArgs)
    {
        if (cliArgs.SetupSkipProvider)
            return WorkspaceSetupProviderMode.Skip;

        if (!string.IsNullOrWhiteSpace(cliArgs.SetupProviderMode))
        {
            var mode = cliArgs.SetupProviderMode.Trim();
            if (string.Equals(mode, "existing", StringComparison.OrdinalIgnoreCase))
                return WorkspaceSetupProviderMode.Existing;
            if (string.Equals(mode, "create", StringComparison.OrdinalIgnoreCase))
                return WorkspaceSetupProviderMode.Create;
            if (string.Equals(mode, "skip", StringComparison.OrdinalIgnoreCase))
                return WorkspaceSetupProviderMode.Skip;
            throw new ArgumentException("Invalid --provider-mode. Expected existing, create, or skip.");
        }

        if (!string.IsNullOrWhiteSpace(cliArgs.SetupProviderProtocol)
            || !string.IsNullOrWhiteSpace(cliArgs.SetupApiKey)
            || !string.IsNullOrWhiteSpace(cliArgs.SetupEndPoint))
            return WorkspaceSetupProviderMode.Create;
        if (!string.IsNullOrWhiteSpace(cliArgs.SetupProviderId) || cliArgs.SetupSetUserDefault)
            return WorkspaceSetupProviderMode.Existing;

        return WorkspaceSetupProviderMode.Skip;
    }

    static int? ParseProviderTimeout(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        if (int.TryParse(value.Trim(), out var timeout) && timeout > 0)
            return timeout;
        throw new ArgumentException("Invalid --provider-timeout-seconds. Expected a positive integer.");
    }

    try
    {
        if (cliArgs.SaveUserConfig && cliArgs.PreferExistingUserConfig)
            throw new ArgumentException("Cannot combine --save-user-config with --prefer-existing-user-config.");

        var providerMode = ResolveSetupProviderMode(cliArgs);
        WorkspaceSetupRequest request;
        var providerProtocol = string.IsNullOrWhiteSpace(cliArgs.SetupProviderProtocol)
            ? ModelProviderProtocols.OpenAI
            : ModelProviderProtocols.Normalize(cliArgs.SetupProviderProtocol);
        var providerId = cliArgs.SetupProviderId?.Trim() ?? string.Empty;
        if (providerMode == WorkspaceSetupProviderMode.Create && string.IsNullOrWhiteSpace(providerId))
        {
            providerId = string.Equals(providerProtocol, ModelProviderProtocols.Anthropic, StringComparison.OrdinalIgnoreCase)
                ? "anthropic"
                : "openai";
        }

        var preference = string.IsNullOrWhiteSpace(cliArgs.SetupPreferenceJson)
            ? null
            : JsonSerializer.Deserialize<ModelPreference>(
                cliArgs.SetupPreferenceJson,
                SessionWireJsonOptions.Default);
        request = new WorkspaceSetupRequest
        {
            Model = cliArgs.SetupModel?.Trim() ?? string.Empty,
            Preference = preference,
            EndPoint = cliArgs.SetupEndPoint?.Trim() ?? string.Empty,
            ApiKey = cliArgs.SetupApiKey?.Trim() ?? string.Empty,
            ProviderMode = providerMode,
            ProviderId = providerId,
            Provider = providerMode == WorkspaceSetupProviderMode.Create
                ? new WorkspaceSetupProviderDraft
                {
                    Id = providerId,
                    DisplayName = cliArgs.SetupProviderDisplayName?.Trim() ?? string.Empty,
                    Protocol = providerProtocol,
                    ApiKey = cliArgs.SetupApiKey?.Trim() ?? string.Empty,
                    EndPoint = cliArgs.SetupEndPoint?.Trim() ?? string.Empty,
                    NetworkTimeoutSeconds = ParseProviderTimeout(cliArgs.SetupProviderTimeoutSeconds),
                    AuthMethod = string.IsNullOrWhiteSpace(cliArgs.SetupAuthMethod)
                        ? "apiKey"
                        : cliArgs.SetupAuthMethod.Trim()
                }
                : null,
            SetAsUserDefault = cliArgs.SetupSetUserDefault || cliArgs.SaveUserConfig
        };

        var result = InitHelper.RunSetup(botPath, request);
        if (result != 0)
        {
            Environment.Exit(result);
            return;
        }

        Console.WriteLine($"Workspace setup completed: {workspacePath}");
        if (request.ProviderMode == WorkspaceSetupProviderMode.Skip)
        {
            Console.WriteLine("Skipped provider setup.");
        }
        else
        {
            Console.WriteLine(request.SetAsUserDefault
                ? "Saved provider selection to user config."
                : "Saved provider selection to workspace config.");
        }
        return;
    }
    catch (Exception ex)
    {
        await Console.Error.WriteLineAsync(ex.Message);
        Environment.Exit(1);
        return;
    }
}

var startupDecision = CliStartup.DecideWorkspaceStartup(cliArgs.Mode, Directory.Exists(botPath));
if (startupDecision == WorkspaceStartupDecision.ShowUsage)
{
    await CliStartup.WriteUsageAsync(Console.Error);
    Environment.Exit(1);
    return;
}

if (startupDecision == WorkspaceStartupDecision.MissingWorkspace)
{
    await Console.Error.WriteLineAsync($"DotCraft workspace not found: {botPath}");
    Environment.Exit(1);
    return;
}

if (startupDecision == WorkspaceStartupDecision.InitializeInteractively)
{
    // Trust folder confirmation
    Console.WriteLine();
    var trustPanel = new Panel(
        new Markup(
            $"[cyan]{FallbackText.InitTrustFolderWorkspacePath}[/]\n" +
            $"  [white]{Markup.Escape(workspacePath)}[/]\n\n" +
            FallbackText.InitTrustFolderDescription))
    {
        Header = new PanelHeader($"[cyan]🔐 {FallbackText.InitTrustFolderTitle}[/]"),
        Border = BoxBorder.Rounded,
        BorderStyle = new Style(Color.Cyan),
        Padding = new Padding(1, 0, 1, 0)
    };
    AnsiConsole.Write(trustPanel);
    Console.WriteLine();

    if (!InitHelper.AskYesNo(FallbackText.InitTrustFolderQuestion))
    {
        AnsiConsole.MarkupLine($"\n[grey]{FallbackText.InitTrustFolderCancelled}[/]");
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"[grey]{FallbackText.InitPressAnyKey}[/]");
        Console.ReadKey(true);
        Environment.Exit(0);
        return;
    }

    // Initialize workspace
    AnsiConsole.WriteLine();
    var initResult = InitHelper.InitializeWorkspace(botPath);
    if (initResult != 0)
    {
        AnsiConsole.MarkupLine($"\n[red]{FallbackText.InitFailedShort}[/]");
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"[grey]{FallbackText.InitPressAnyKey}[/]");
        Console.ReadKey(true);
        Environment.Exit(1);
        return;
    }
    workspaceJustInitialized = true;
}

// -------------------------------------------------------------------------
// 4. Load configuration & apply CLI overrides
// -------------------------------------------------------------------------
var configPath = Path.Combine(botPath, "config.json");
AppConfig config;
try
{
    config = AppConfig.LoadWithGlobalFallback(configPath);
}
catch (Exception ex)
{
    using var failureLoggerFactory = DotCraftLoggingFactory.CreateWorkspace(
        new AppConfig.LoggingConfig(),
        botPath,
        cliArgs.ReservesStdout);
    failureLoggerFactory.CreateLogger("DotCraft.Startup")
        .LogCritical(ex, "Failed to load workspace configuration from {ConfigPath}", configPath);
    throw;
}

// CLI arguments take precedence over config.json values.
cliArgs.ApplyTo(config);
if (cliArgs.Mode == CommandLineArgs.RunMode.AppServer)
{
    ManagedAppServerEnvironment.ApplyTo(config);
}

DebugModeService.Initialize(config.DebugMode);
if (config.DebugMode)
{
    AnsiConsole.MarkupLine("[yellow]Debug mode is enabled - tool arguments and results will be shown in full[/]");
}

if (cliArgs.Mode == CommandLineArgs.RunMode.None)
{
    if (workspaceJustInitialized)
    {
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"[green]✓ {FallbackText.InitWorkspaceInitialized}[/]");
        await Console.Out.WriteLineAsync("Run `dotcraft exec <prompt>` to start a one-shot command-line task.");
    }
    return;
}

using var loggerFactory = DotCraftLoggingFactory.CreateWorkspace(
    config.Logging,
    botPath,
    cliArgs.ReservesStdout);
var applicationLogger = loggerFactory.CreateLogger("DotCraft.Application");
DebugModeService.DiagnosticSink = message =>
    applicationLogger.LogDebug("{DebugDiagnostic}", message);

// -------------------------------------------------------------------------
// 7. Module registry, DI, and host startup
// -------------------------------------------------------------------------
var moduleRegistry = new ModuleRegistry();
var hostFactoryRegistry = new HostFactoryRegistry();
ModuleRegistrations.RegisterAll(moduleRegistry, hostFactoryRegistry);

// Module config validation
var configValidationOk = ServiceRegistration.ValidateConfigurations(config, moduleRegistry, applicationLogger);
if (!configValidationOk && isHeadless)
{
    applicationLogger.LogError("Configuration validation failed for workspace {WorkspacePath}", workspacePath);
    await Console.Error.WriteLineAsync("Configuration validation failed.");
    Environment.ExitCode = 1;
    return;
}

var preferredPrimaryModuleName = cliArgs.Mode switch
{
    CommandLineArgs.RunMode.Exec => "cli",
    CommandLineArgs.RunMode.AppServer => "app-server",
    CommandLineArgs.RunMode.Acp => "acp",
    _ => null
};

var hostBuilder = new HostBuilder(moduleRegistry, hostFactoryRegistry, config, preferredPrimaryModuleName);

try
{
var services = new ServiceCollection()
        .AddSingleton<ILoggerFactory>(loggerFactory)
        .AddSingleton(moduleRegistry)
        .AddSingleton(cliArgs)
        .AddSingleton<IConfigSchemaProvider>(ConfigSchemaRegistrations.CreateSchemaProvider())
        .AddOpenSandboxProvider(config.Tools.Sandbox)
        .AddDotCraftHarness(config, options =>
        {
            options.WorkspacePath = workspacePath;
            options.DataPath = botPath;
            options.UserDataPath = HubPaths.ForCurrentUser().CraftHomePath;
        });

    var (provider, host) = hostBuilder.Build(services);
    await using var providerLifetime = provider;
    await using (host)
    {
        await host.RunAsync();
    }
}
catch (Exception ex)
{
    applicationLogger.LogCritical(
        ex,
        "DotCraft host {HostMode} terminated unexpectedly for workspace {WorkspacePath}",
        preferredPrimaryModuleName,
        workspacePath);
    throw;
}
