using System.Text.Json;
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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Spectre.Console;
using DotCraft.Sessions.Wire;

namespace DotCraft.CLI;

internal static class DotCraftApplication
{
    public static async Task<int> RunAsync(CommandLineArgs cliArgs, CancellationToken cancellationToken)
    {
        var isHeadless = cliArgs.Mode is CommandLineArgs.RunMode.Acp
            or CommandLineArgs.RunMode.AppServer
            or CommandLineArgs.RunMode.Hub
            or CommandLineArgs.RunMode.Dashboard
            or CommandLineArgs.RunMode.Exec;

        if (cliArgs.ReservesStdout)
        {
            SubprocessEnvironment.Prepare();
        }

        if (cliArgs.Mode == CommandLineArgs.RunMode.WorkflowWorker)
        {
            return await WorkflowWorkerRunner.RunAsync(
                Console.OpenStandardInput(),
                Console.OpenStandardOutput(),
                Console.OpenStandardError(),
                cancellationToken);
        }

        if (cliArgs.Mode == CommandLineArgs.RunMode.Hub)
        {
            var hubPaths = HubPaths.ForCurrentUser();
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
            return 0;
        }

        if (cliArgs.Mode == CommandLineArgs.RunMode.Dashboard)
        {
            var result = await DashboardCliHost.RunAsync(cliArgs);
            return result;
        }

        if (cliArgs.Mode == CommandLineArgs.RunMode.ModelCatalog)
        {
            return await ModelCatalogCliRunner.RunAsync(cliArgs, cancellationToken);
        }

        var workspacePath = Directory.GetCurrentDirectory();
        var botPath = Path.GetFullPath(".craft");
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
                    return result;
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
                return 0;
            }
            catch (Exception ex)
            {
                await Console.Error.WriteLineAsync(ex.Message);
                return 1;
            }
        }

        if (!Directory.Exists(botPath))
        {
            await Console.Error.WriteLineAsync($"DotCraft workspace not found: {botPath}");
            return 1;
        }

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

        using var loggerFactory = DotCraftLoggingFactory.CreateWorkspace(
            config.Logging,
            botPath,
            cliArgs.ReservesStdout);
        var applicationLogger = loggerFactory.CreateLogger("DotCraft.Application");
        DebugModeService.DiagnosticSink = message =>
            applicationLogger.LogDebug("{DebugDiagnostic}", message);

        var moduleRegistry = new ModuleRegistry();
        var hostFactoryRegistry = new HostFactoryRegistry();
        ModuleRegistrations.RegisterAll(moduleRegistry, hostFactoryRegistry);

        var configValidationOk = ServiceRegistration.ValidateConfigurations(config, moduleRegistry, applicationLogger);
        if (!configValidationOk && isHeadless)
        {
            applicationLogger.LogError("Configuration validation failed for workspace {WorkspacePath}", workspacePath);
            await Console.Error.WriteLineAsync("Configuration validation failed.");
            return 1;
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
        return 0;
    }
}
