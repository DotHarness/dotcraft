using DotCraft.Configuration;
using DotCraft.Skills;
using Xunit;

namespace DotCraft.Tests.Sessions.Protocol.AppServer;

public sealed class AppServerMarketplaceTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), $"marketplace_protocol_{Guid.NewGuid():N}");
    private readonly string _workspaceCraftPath;
    private readonly string _userCraftHome;

    public AppServerMarketplaceTests()
    {
        _workspaceCraftPath = Path.Combine(_tempRoot, ".craft");
        _userCraftHome = Path.Combine(_tempRoot, "user-craft");
        Directory.CreateDirectory(_workspaceCraftPath);
        Directory.CreateDirectory(_userCraftHome);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempRoot))
                Directory.Delete(_tempRoot, recursive: true);
        }
        catch
        {
            // Best-effort cleanup.
        }
    }

    [Fact]
    public async Task Initialize_ReportsPluginMarketplacesCapability()
    {
        using var harness = CreateHarness();
        using var init = await harness.InitializeAsync();

        Assert.True(init.RootElement
            .GetProperty("result")
            .GetProperty("capabilities")
            .GetProperty("pluginMarketplaces")
            .GetBoolean());
    }

    [Fact]
    public async Task MarketplaceAdd_RecordsLocalSourceAndExposesItsPluginAsInstallable()
    {
        var sourceRoot = Path.Combine(_tempRoot, "source");
        WriteMarketplace(sourceRoot, "example-marketplace", "Example Plugins", ["example-plugin"]);
        WritePlugin(Path.Combine(sourceRoot, "plugins", "example-plugin"), "example-plugin");
        using var harness = CreateHarness();
        await harness.InitializeAsync();

        await harness.ExecuteRequestAsync(harness.BuildRequest(
            DotCraft.Protocol.AppServer.AppServerMethodNames.MarketplaceAdd,
            new { source = sourceRoot }));

        using var response = await harness.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(response);
        var result = response.RootElement.GetProperty("result");
        Assert.False(result.GetProperty("alreadyAdded").GetBoolean());
        var marketplace = result.GetProperty("marketplace");
        Assert.Equal("example-marketplace", marketplace.GetProperty("name").GetString());
        Assert.Equal("Example Plugins", marketplace.GetProperty("displayName").GetString());
        Assert.Equal("local", marketplace.GetProperty("sourceType").GetString());
        Assert.Equal(
            ["example-plugin"],
            marketplace.GetProperty("pluginIds").EnumerateArray().Select(item => item.GetString()!).ToArray());

        using var list = await ListPluginsAsync(harness);
        var listResult = list.RootElement.GetProperty("result");
        var plugin = Assert.Single(
            listResult.GetProperty("plugins").EnumerateArray(),
            item => item.GetProperty("id").GetString() == "example-plugin");
        Assert.True(plugin.GetProperty("installable").GetBoolean());
        Assert.False(plugin.GetProperty("installed").GetBoolean());
        Assert.Equal("example-marketplace", plugin.GetProperty("marketplaceName").GetString());
        Assert.Single(listResult.GetProperty("marketplaces").EnumerateArray());
    }

    [Fact]
    public async Task MarketplaceAdd_ReportsAlreadyAddedForTheSameSource()
    {
        var sourceRoot = Path.Combine(_tempRoot, "source");
        WriteMarketplace(sourceRoot, "example-marketplace");
        using var harness = CreateHarness();
        await harness.InitializeAsync();

        await harness.ExecuteRequestAsync(harness.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.MarketplaceAdd, new { source = sourceRoot }));
        using (await harness.Transport.ReadNextSentAsync())
        {
        }

        await harness.ExecuteRequestAsync(harness.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.MarketplaceAdd, new { source = sourceRoot }));
        using var response = await harness.Transport.ReadNextSentAsync();

        AppServerTestHarness.AssertIsSuccessResponse(response);
        Assert.True(response.RootElement.GetProperty("result").GetProperty("alreadyAdded").GetBoolean());
    }

    [Fact]
    public async Task MarketplaceAdd_RejectsInvalidSourceWithStableCode()
    {
        using var harness = CreateHarness();
        await harness.InitializeAsync();

        await harness.ExecuteRequestAsync(harness.BuildRequest(
            DotCraft.Protocol.AppServer.AppServerMethodNames.MarketplaceAdd,
            new { source = "https://user:secret@example.com/team/repo.git" }));

        using var response = await harness.Transport.ReadNextSentAsync();
        var error = response.RootElement.GetProperty("error");
        Assert.Equal(-32093, error.GetProperty("code").GetInt32());
        Assert.Equal("MarketplaceSourceInvalid", error.GetProperty("data").GetProperty("code").GetString());
    }

    [Fact]
    public async Task MarketplaceAdd_RejectsSparsePathsOnLocalSource()
    {
        var sourceRoot = Path.Combine(_tempRoot, "source");
        WriteMarketplace(sourceRoot, "example-marketplace");
        using var harness = CreateHarness();
        await harness.InitializeAsync();

        await harness.ExecuteRequestAsync(harness.BuildRequest(
            DotCraft.Protocol.AppServer.AppServerMethodNames.MarketplaceAdd,
            new { source = sourceRoot, sparsePaths = new[] { "plugins/example" } }));

        using var response = await harness.Transport.ReadNextSentAsync();
        Assert.Equal(
            "MarketplaceSourceInvalid",
            response.RootElement.GetProperty("error").GetProperty("data").GetProperty("code").GetString());
    }

    [Fact]
    public async Task MarketplaceRemove_DropsTheSourceFromSubsequentListings()
    {
        var sourceRoot = Path.Combine(_tempRoot, "source");
        WriteMarketplace(sourceRoot, "example-marketplace", plugins: ["example-plugin"]);
        WritePlugin(Path.Combine(sourceRoot, "plugins", "example-plugin"), "example-plugin");
        using var harness = CreateHarness();
        await harness.InitializeAsync();
        await harness.ExecuteRequestAsync(harness.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.MarketplaceAdd, new { source = sourceRoot }));
        using (await harness.Transport.ReadNextSentAsync())
        {
        }

        await harness.ExecuteRequestAsync(harness.BuildRequest(
            DotCraft.Protocol.AppServer.AppServerMethodNames.MarketplaceRemove,
            new { name = "example-marketplace" }));

        using var response = await harness.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(response);
        Assert.Equal("example-marketplace", response.RootElement.GetProperty("result").GetProperty("name").GetString());
        Assert.True(Directory.Exists(sourceRoot));

        using var list = await ListPluginsAsync(harness);
        var listResult = list.RootElement.GetProperty("result");
        Assert.Empty(listResult.GetProperty("marketplaces").EnumerateArray());
        Assert.DoesNotContain(
            listResult.GetProperty("plugins").EnumerateArray(),
            item => item.GetProperty("id").GetString() == "example-plugin");
    }

    [Fact]
    public async Task MarketplaceRemove_RejectsUnknownMarketplace()
    {
        using var harness = CreateHarness();
        await harness.InitializeAsync();

        await harness.ExecuteRequestAsync(harness.BuildRequest(
            DotCraft.Protocol.AppServer.AppServerMethodNames.MarketplaceRemove,
            new { name = "missing" }));

        using var response = await harness.Transport.ReadNextSentAsync();
        Assert.Equal(
            "MarketplaceNotFound",
            response.RootElement.GetProperty("error").GetProperty("data").GetProperty("code").GetString());
    }

    [Fact]
    public async Task MarketplaceRefresh_RevalidatesEveryConfiguredSource()
    {
        var sourceRoot = Path.Combine(_tempRoot, "source");
        WriteMarketplace(sourceRoot, "example-marketplace");
        using var harness = CreateHarness();
        await harness.InitializeAsync();
        await harness.ExecuteRequestAsync(harness.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.MarketplaceAdd, new { source = sourceRoot }));
        using (await harness.Transport.ReadNextSentAsync())
        {
        }

        await harness.ExecuteRequestAsync(harness.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.MarketplaceRefresh, new { }));

        using var response = await harness.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(response);
        var result = response.RootElement.GetProperty("result");
        Assert.Single(result.GetProperty("marketplaces").EnumerateArray());
        Assert.Empty(result.GetProperty("errors").EnumerateArray());
    }

    [Fact]
    public async Task MarketplaceRefresh_ReportsUnreachableSourceWithoutFailingTheRequest()
    {
        var sourceRoot = Path.Combine(_tempRoot, "source");
        WriteMarketplace(sourceRoot, "example-marketplace");
        using var harness = CreateHarness();
        await harness.InitializeAsync();
        await harness.ExecuteRequestAsync(harness.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.MarketplaceAdd, new { source = sourceRoot }));
        using (await harness.Transport.ReadNextSentAsync())
        {
        }

        Directory.Delete(sourceRoot, recursive: true);
        await harness.ExecuteRequestAsync(harness.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.MarketplaceRefresh, new { }));

        using var response = await harness.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(response);
        var result = response.RootElement.GetProperty("result");
        Assert.Empty(result.GetProperty("marketplaces").EnumerateArray());
        var failure = Assert.Single(result.GetProperty("errors").EnumerateArray());
        Assert.Equal("example-marketplace", failure.GetProperty("name").GetString());
        Assert.Equal("MarketplaceDocumentMissing", failure.GetProperty("code").GetString());
    }

    private async Task<System.Text.Json.JsonDocument> ListPluginsAsync(AppServerTestHarness harness)
    {
        await harness.ExecuteRequestAsync(harness.BuildRequest(
            DotCraft.Protocol.AppServer.AppServerMethodNames.PluginList,
            new { includeDisabled = true }));
        return await harness.Transport.ReadNextSentAsync();
    }

    private AppServerTestHarness CreateHarness()
    {
        var config = new AppConfig { GlobalConfigPath = Path.Combine(_userCraftHome, "config.json") };
        var loader = new SkillsLoader(_workspaceCraftPath);
        return new AppServerTestHarness(
            workspaceCraftPath: _workspaceCraftPath,
            skillsLoader: loader,
            appConfigMonitor: new AppConfigMonitor(config),
            builtInPluginSourceRoots: []);
    }

    private static void WriteMarketplace(
        string root,
        string name,
        string? displayName = null,
        IReadOnlyList<string>? plugins = null)
    {
        var documentDirectory = Path.Combine(root, ".craft", "plugins");
        Directory.CreateDirectory(documentDirectory);
        var interfaceBlock = displayName == null
            ? string.Empty
            : $$"""
  "interface": { "displayName": "{{displayName}}" },

""";
        var entries = string.Join(",\n", (plugins ?? []).Select(plugin => $$"""
    {
      "name": "{{plugin}}",
      "source": { "source": "local", "path": "./plugins/{{plugin}}" },
      "policy": { "installation": "AVAILABLE", "authentication": "ON_INSTALL" }
    }
"""));

        File.WriteAllText(
            Path.Combine(documentDirectory, "marketplace.json"),
            $$"""
{
  "name": "{{name}}",
{{interfaceBlock}}  "plugins": [
{{entries}}
  ]
}
""");
    }

    private static void WritePlugin(string pluginRoot, string id)
    {
        Directory.CreateDirectory(Path.Combine(pluginRoot, ".craft-plugin"));
        Directory.CreateDirectory(Path.Combine(pluginRoot, "skills", "demo-skill"));
        File.WriteAllText(
            Path.Combine(pluginRoot, "skills", "demo-skill", "SKILL.md"),
            "---\nname: demo-skill\ndescription: Demo skill\n---\n# Demo");
        File.WriteAllText(
            Path.Combine(pluginRoot, ".craft-plugin", "plugin.json"),
            $$"""
{
  "schemaVersion": 1,
  "id": "{{id}}",
  "version": "1.0.0",
  "displayName": "Example",
  "description": "Example plugin.",
  "capabilities": ["skill"],
  "skills": "./skills/"
}
""");
    }
}
