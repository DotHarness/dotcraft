using System.ComponentModel;
using System.Text.Json;
using DotCraft.Agents;
using DotCraft.Configuration;
using DotCraft.Harness;
using DotCraft.Plugins;
using DotCraft.Sessions;
using DotCraft.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using static SmokeAssertions;

Ensure(args.Length == 1, "Pass the independently built plugin output directory.");
var testRoot = Path.Combine(Path.GetTempPath(), $"dotcraft-harness-consumer-{Guid.NewGuid():N}");
var completed = false;
try
{
    var declaration = DotCraft.GeneratedTools.Harness.Consumer.GeneratedToolDeclarations.IConsumerDeclarations_Echo_Declaration;
    Ensure(declaration.Name == "echo", "The package did not generate the declared tool.");
    Ensure(declaration.InputSchema.GetProperty("properties").GetProperty("value").GetProperty("type").GetString() == "string",
        "The generated tool lost its parameter schema.");

    var workspacePath = Path.Combine(testRoot, "session");
    var binaryRoot = PluginFixtureFiles.InstallBinary(workspacePath, args[0]);
    PluginFixtureFiles.CreateSourceProject(workspacePath);
    var config = CreateConfig(testRoot, binaryRoot);
    var builder = Host.CreateApplicationBuilder();
    builder.Services.AddDotCraftHarness(config, options => options.WorkspacePath = workspacePath);
    builder.Services.RemoveAll<IModelProvider>();
    builder.Services.AddSingleton<IModelProvider, FakeModelProvider>();

    using var host = builder.Build();
    await host.StartAsync();
    try
    {
        var plugins = host.Services.GetRequiredService<IPluginDotnetRuntimeCoordinator>();
        var trust = await plugins.TrustAsync("package.binary");
        Ensure(trust.Outcome != PluginRuntimeMutationOutcome.NotApplied,
            "Could not trust the binary fixture: " + JsonSerializer.Serialize(trust.Diagnostics));
        AssertActive(plugins, "package.binary");

        var sessions = host.Services.GetRequiredService<ISessionService>();
        var first = await CreateThread(sessions, workspacePath, "first");
        await AssertEcho(sessions, first, workspacePath, "PackageBinary", "binary");
        await AssertEnvelope(sessions, first, "PackageBinary", "package.binary");

        var build = await RunToolTurn(sessions, first, "DotNetPlugin", "Build", "package.source");
        Ensure(build.Result.Success, "Authoring tool failed: " + build.Result.Result);
        using (var document = JsonDocument.Parse(build.Result.Result))
        {
            Ensure(document.RootElement.GetProperty("outcome").GetString() == "built",
                "Source-plugin compilation failed: " + build.Result.Result);
        }
        AssertActive(plugins, "package.source");
        var sourceRoot = Path.Combine(workspacePath, ".craft", "plugin-projects", "package.source", "src");
        Ensure(Directory.EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories).Count() == 1,
            "Authoring wrote generated files into persistent plugin source.");

        var second = await CreateThread(sessions, workspacePath, "second");
        await Task.WhenAll(
            AssertEcho(sessions, first, workspacePath, "PackageSource", "first"),
            AssertEcho(sessions, second, workspacePath, "PackageSource", "second"));
        await AssertEnvelope(sessions, first, "PackageSource", "package.source");
        Console.WriteLine("DotCraft.Harness package smoke passed: binary plugin, source authoring, typed tools, live contexts, and full results.");
    }
    finally
    {
        await host.StopAsync();
    }
    completed = true;
}
catch
{
    completed = false;
    throw;
}
finally
{
    if (completed && Directory.Exists(testRoot))
    {
        try
        {
            Directory.Delete(testRoot, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"Consumer smoke cleanup skipped: {exception.Message}");
        }
    }
    if (Directory.Exists(testRoot))
        Console.Error.WriteLine($"Consumer smoke diagnostics retained at: {testRoot}");
}

static AppConfig CreateConfig(string root, string binaryRoot)
{
    var config = new AppConfig
    {
        GlobalConfigPath = Path.Combine(root, "global", "config.json"),
        ProviderId = "package-smoke",
        ProviderPreferences = new Dictionary<string, ModelPreference>(StringComparer.OrdinalIgnoreCase)
        {
            ["package-smoke"] = new ModelPreference { Model = "fake-model" }
        },
        Providers =
        {
            ["package-smoke"] = new AppConfig.ModelProviderConfig
            {
                DisplayName = "Package smoke provider",
                Protocol = ModelProviderProtocols.OpenAIChatCompletions,
                ApiKey = "not-used",
                EndPoint = "https://example.invalid/v1"
            }
        }
    };
    config.Plugins.PluginRoots.Add(binaryRoot);
    config.Plugins.EnabledPlugins.Add("package.binary");
    config.Plugins.DisableDefaultPluginRegistry = true;
    config.Tools.DeferredLoading.Strategy = AppConfig.DeferredLoadingStrategy.Off;
    config.Permissions.DefaultApprovalPolicy = ApprovalPolicy.AutoApprove;
    return config;
}

static async Task<string> CreateThread(ISessionService sessions, string workspace, string user) =>
    (await sessions.CreateThreadAsync(new SessionIdentity
    {
        ChannelName = "package-smoke",
        UserId = user,
        WorkspacePath = workspace
    })).Id;

static void AssertActive(IPluginDotnetRuntimeCoordinator plugins, string pluginId)
{
    var plugin = plugins.Snapshot.Plugins.SingleOrDefault(plugin => plugin.PluginId == pluginId);
    Ensure(plugin?.State == PluginDotnetRuntimeState.Active,
        "Plugin did not activate: " + JsonSerializer.Serialize(plugins.Snapshot));
}

internal interface IConsumerDeclarations
{
    [ToolDeclaration(Name = "echo")]
    [Description("Return the input value.")]
    string Echo([Description("Value to return.")] string value);
}
