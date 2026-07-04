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

    private static void WriteHookPlugin(string pluginRoot, string id)
    {
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
        File.WriteAllText(Path.Combine(pluginRoot, "hooks", "hooks.json"), HookJson("echo plugin hook"));
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
}
