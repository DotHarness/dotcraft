using System.Text.Json;
using DotCraft.Hooks;
using DotCraft.Protocol.AppServer;

namespace DotCraft.Tests.Sessions.Protocol.AppServer;

public sealed class AppServerHooksTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), $"hooks_appserver_{Guid.NewGuid():N}");
    private readonly string _workspaceCraftPath;

    public AppServerHooksTests()
    {
        _workspaceCraftPath = Path.Combine(_tempRoot, ".craft");
        Directory.CreateDirectory(_workspaceCraftPath);
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
    public async Task Initialize_ReportsHooksManagementCapability()
    {
        using var harness = new AppServerTestHarness(workspaceCraftPath: _workspaceCraftPath);
        using var init = await harness.InitializeAsync();

        Assert.True(init.RootElement
            .GetProperty("result")
            .GetProperty("capabilities")
            .GetProperty("hooksManagement")
            .GetBoolean());
    }

    [Fact]
    public async Task HooksSetState_WritesGlobalStateAndRefreshesRunner()
    {
        WriteWorkspaceHooks("echo workspace hook");
        var runner = new HookRunner(new HookDiscoveryResult(), _tempRoot);
        using var harness = new AppServerTestHarness(
            workspaceCraftPath: _workspaceCraftPath,
            hookRunner: runner);
        await harness.InitializeAsync();

        await harness.ExecuteRequestAsync(harness.BuildRequest(AppServerMethods.HooksList, new { }));
        using var listResponse = await harness.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(listResponse);
        var hook = Assert.Single(listResponse.RootElement.GetProperty("result").GetProperty("hooks").EnumerateArray());
        var key = hook.GetProperty("key").GetString()!;
        var hash = hook.GetProperty("currentHash").GetString()!;
        Assert.Equal("workspace", hook.GetProperty("source").GetString());
        Assert.Equal("Bash(git commit:*)", hook.GetProperty("condition").GetString());
        Assert.Equal("async", hook.GetProperty("executionMode").GetString());
        Assert.True(hook.GetProperty("asyncRewake").GetBoolean());
        Assert.Equal("untrusted", hook.GetProperty("trustStatus").GetString());
        Assert.True(hook.GetProperty("enabled").GetBoolean());
        Assert.False(runner.HasToolHooks);

        await harness.ExecuteRequestAsync(harness.BuildRequest(
            AppServerMethods.HooksSetState,
            new { key, trustedHash = hash }));
        using var setStateResponse = await harness.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(setStateResponse);
        var trustedHook = Assert.Single(setStateResponse.RootElement.GetProperty("result").GetProperty("hooks").EnumerateArray());
        Assert.Equal("trusted", trustedHook.GetProperty("trustStatus").GetString());
        Assert.True(runner.HasToolHooks);

        using var config = JsonDocument.Parse(File.ReadAllText(harness.Monitor.Current.GlobalConfigPath!));
        var state = config.RootElement.GetProperty("Hooks").GetProperty("State").GetProperty(key);
        Assert.Equal(hash, state.GetProperty("TrustedHash").GetString());
    }

    [Fact]
    public async Task HooksTrustPlugin_WritesTrustedHashesForAllCurrentPluginHooks()
    {
        WriteHookPlugin(
            Path.Combine(_workspaceCraftPath, "plugins", "demo"),
            "demo-plugin",
            "echo plugin hook one",
            "echo plugin hook two");
        var runner = new HookRunner(new HookDiscoveryResult(), _tempRoot);
        using var harness = new AppServerTestHarness(
            workspaceCraftPath: _workspaceCraftPath,
            hookRunner: runner);
        await harness.InitializeAsync();

        await harness.ExecuteRequestAsync(harness.BuildRequest(AppServerMethods.HooksList, new { }));
        using var listResponse = await harness.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(listResponse);
        var pluginHooks = listResponse.RootElement
            .GetProperty("result")
            .GetProperty("hooks")
            .EnumerateArray()
            .Where(hook => hook.GetProperty("pluginId").GetString() == "demo-plugin")
            .ToList();
        Assert.Equal(2, pluginHooks.Count);
        Assert.All(pluginHooks, hook => Assert.Equal("untrusted", hook.GetProperty("trustStatus").GetString()));
        Assert.False(runner.HasToolHooks);

        await harness.ExecuteRequestAsync(harness.BuildRequest(
            AppServerMethods.HooksTrustPlugin,
            new { pluginId = "demo-plugin" }));
        using var trustResponse = await harness.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(trustResponse);
        var trustedHooks = trustResponse.RootElement
            .GetProperty("result")
            .GetProperty("hooks")
            .EnumerateArray()
            .Where(hook => hook.GetProperty("pluginId").GetString() == "demo-plugin")
            .ToList();
        Assert.Equal(2, trustedHooks.Count);
        Assert.All(trustedHooks, hook => Assert.Equal("trusted", hook.GetProperty("trustStatus").GetString()));
        Assert.True(runner.HasToolHooks);

        using var config = JsonDocument.Parse(File.ReadAllText(harness.Monitor.Current.GlobalConfigPath!));
        var state = config.RootElement.GetProperty("Hooks").GetProperty("State");
        foreach (var hook in trustedHooks)
        {
            var key = hook.GetProperty("key").GetString()!;
            var hash = hook.GetProperty("currentHash").GetString()!;
            Assert.Equal(hash, state.GetProperty(key).GetProperty("TrustedHash").GetString());
        }
    }

    [Fact]
    public async Task HooksTrustPlugin_TrustsModifiedPluginHooksAgain()
    {
        var pluginRoot = Path.Combine(_workspaceCraftPath, "plugins", "demo");
        WriteHookPlugin(pluginRoot, "demo-plugin", "echo plugin hook");
        using var harness = new AppServerTestHarness(workspaceCraftPath: _workspaceCraftPath);
        await harness.InitializeAsync();

        await harness.ExecuteRequestAsync(harness.BuildRequest(
            AppServerMethods.HooksTrustPlugin,
            new { pluginId = "demo-plugin" }));
        using var firstTrustResponse = await harness.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(firstTrustResponse);
        var firstHook = Assert.Single(
            firstTrustResponse.RootElement
                .GetProperty("result")
                .GetProperty("hooks")
                .EnumerateArray(),
            hook => hook.GetProperty("pluginId").GetString() == "demo-plugin");
        var key = firstHook.GetProperty("key").GetString()!;
        var firstHash = firstHook.GetProperty("currentHash").GetString()!;

        WriteHookPlugin(pluginRoot, "demo-plugin", "echo changed plugin hook");
        await harness.ExecuteRequestAsync(harness.BuildRequest(AppServerMethods.HooksList, new { }));
        using var modifiedListResponse = await harness.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(modifiedListResponse);
        var modifiedHook = Assert.Single(
            modifiedListResponse.RootElement
                .GetProperty("result")
                .GetProperty("hooks")
                .EnumerateArray(),
            hook => hook.GetProperty("pluginId").GetString() == "demo-plugin");
        var modifiedHash = modifiedHook.GetProperty("currentHash").GetString()!;
        Assert.NotEqual(firstHash, modifiedHash);
        Assert.Equal("modified", modifiedHook.GetProperty("trustStatus").GetString());

        await harness.ExecuteRequestAsync(harness.BuildRequest(
            AppServerMethods.HooksTrustPlugin,
            new { pluginId = "demo-plugin" }));
        using var secondTrustResponse = await harness.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(secondTrustResponse);
        var trustedHook = Assert.Single(
            secondTrustResponse.RootElement
                .GetProperty("result")
                .GetProperty("hooks")
                .EnumerateArray(),
            hook => hook.GetProperty("pluginId").GetString() == "demo-plugin");
        Assert.Equal("trusted", trustedHook.GetProperty("trustStatus").GetString());

        using var config = JsonDocument.Parse(File.ReadAllText(harness.Monitor.Current.GlobalConfigPath!));
        var state = config.RootElement.GetProperty("Hooks").GetProperty("State").GetProperty(key);
        Assert.Equal(modifiedHash, state.GetProperty("TrustedHash").GetString());
    }

    [Fact]
    public async Task HooksTrustPlugin_RejectsUnknownPlugin()
    {
        using var harness = new AppServerTestHarness(workspaceCraftPath: _workspaceCraftPath);
        await harness.InitializeAsync();

        await harness.ExecuteRequestAsync(harness.BuildRequest(
            AppServerMethods.HooksTrustPlugin,
            new { pluginId = "missing-plugin" }));
        using var response = await harness.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsErrorResponse(response, -32602);
    }

    [Fact]
    public async Task PluginList_IncludesHookSummaries()
    {
        WriteHookPlugin(Path.Combine(_workspaceCraftPath, "plugins", "demo"), "demo-plugin");
        using var harness = new AppServerTestHarness(workspaceCraftPath: _workspaceCraftPath);
        await harness.InitializeAsync();

        await harness.ExecuteRequestAsync(harness.BuildRequest(AppServerMethods.PluginList, new { includeDisabled = true }));
        using var response = await harness.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(response);
        var plugin = Assert.Single(
            response.RootElement.GetProperty("result").GetProperty("plugins").EnumerateArray(),
            item => item.GetProperty("id").GetString() == "demo-plugin");
        var hook = Assert.Single(plugin.GetProperty("hooks").EnumerateArray());
        Assert.Equal("demo-plugin:hooks/hooks.json:pre_tool_use:0:0", hook.GetProperty("key").GetString());
        Assert.Equal(nameof(HookEvent.PreToolUse), hook.GetProperty("eventName").GetString());
    }

    [Fact]
    public async Task PluginView_IncludesHookSummaries()
    {
        WriteHookPlugin(Path.Combine(_workspaceCraftPath, "plugins", "demo"), "demo-plugin");
        using var harness = new AppServerTestHarness(workspaceCraftPath: _workspaceCraftPath);
        await harness.InitializeAsync();

        await harness.ExecuteRequestAsync(harness.BuildRequest(AppServerMethods.PluginView, new { id = "demo-plugin" }));
        using var response = await harness.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(response);
        var plugin = response.RootElement.GetProperty("result").GetProperty("plugin");
        var hook = Assert.Single(plugin.GetProperty("hooks").EnumerateArray());
        Assert.Equal("demo-plugin:hooks/hooks.json:pre_tool_use:0:0", hook.GetProperty("key").GetString());
        Assert.Equal(nameof(HookEvent.PreToolUse), hook.GetProperty("eventName").GetString());
    }

    private void WriteWorkspaceHooks(string command)
    {
        File.WriteAllText(Path.Combine(_workspaceCraftPath, "hooks.json"), HookJson(command));
    }

    private static void WriteHookPlugin(string pluginRoot, string id, params string[] commands)
    {
        if (commands.Length == 0)
            commands = ["echo plugin hook"];

        Directory.CreateDirectory(Path.Combine(pluginRoot, ".craft-plugin"));
        Directory.CreateDirectory(Path.Combine(pluginRoot, "hooks"));
        File.WriteAllText(
            Path.Combine(pluginRoot, ".craft-plugin", "plugin.json"),
            $$"""
{
  "schemaVersion": 1,
  "id": "{{id}}",
  "version": "1.0.0",
  "displayName": "Demo Hook Plugin",
  "description": "Demo hook plugin.",
  "capabilities": ["hooks"]
}
""");
        File.WriteAllText(Path.Combine(pluginRoot, "hooks", "hooks.json"), PluginHookJson(commands));
    }

    private static string HookJson(string command) =>
        $$"""
{
  "hooks": {
    "PreToolUse": [
      {
        "matcher": "Shell",
        "hooks": [
          {
            "type": "command",
            "command": "{{command}}",
            "timeout": 7,
            "if": "Bash(git commit:*)",
            "asyncRewake": true,
            "rewakeMessage": "Review feedback",
            "rewakeSummary": "Review found issues"
          }
        ]
      }
    ]
  }
}
""";

    private static string PluginHookJson(IReadOnlyList<string> commands)
    {
        var handlers = string.Join(
            "," + Environment.NewLine,
            commands.Select(command =>
                $$"""
          {
            "type": "command",
            "command": "{{command}}",
            "timeout": 7,
            "if": "Bash(git commit:*)",
            "asyncRewake": true,
            "rewakeMessage": "Review feedback",
            "rewakeSummary": "Review found issues"
          }
"""));
        return $$"""
{
  "hooks": {
    "PreToolUse": [
      {
        "matcher": "Shell",
        "hooks": [
{{handlers}}
        ]
      }
    ]
  }
}
""";
    }
}
