using System.Text.Json;
using DotCraft.Hooks;
using Xunit;

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
        }
    }

    [Fact]
    public async Task HooksSetState_WritesGlobalStateAndRefreshesRunner()
    {
        WriteWorkspaceHooks("echo workspace hook");
        var runner = new HookRunner(new HookDiscoveryResult(), _tempRoot);
        using var harness = new CoreAppServerTestHarness(
            workspaceCraftPath: _workspaceCraftPath,
            hookRunner: runner);
        using (var init = await harness.InitializeAsync())
            Assert.True(init.RootElement.GetProperty("result").GetProperty("capabilities").GetProperty("hooksManagement").GetBoolean());

        await harness.ExecuteRequestAsync(harness.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.HooksList, new { }));
        using var listResponse = await harness.Transport.ReadNextSentAsync();
        CoreAppServerTestHarness.AssertIsSuccessResponse(listResponse);
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
            DotCraft.Protocol.AppServer.AppServerMethodNames.HooksSetState,
            new { key, trustedHash = hash }));
        using var setStateResponse = await harness.Transport.ReadNextSentAsync();
        CoreAppServerTestHarness.AssertIsSuccessResponse(setStateResponse);
        var trustedHook = Assert.Single(setStateResponse.RootElement.GetProperty("result").GetProperty("hooks").EnumerateArray());
        Assert.Equal("trusted", trustedHook.GetProperty("trustStatus").GetString());
        Assert.True(runner.HasToolHooks);

        using var config = JsonDocument.Parse(File.ReadAllText(harness.Monitor.Current.GlobalConfigPath!));
        var state = config.RootElement.GetProperty("Hooks").GetProperty("State").GetProperty(key);
        Assert.Equal(hash, state.GetProperty("TrustedHash").GetString());
    }

    [Fact]
    public async Task HooksTrustPlugin_TrustsEveryCurrentPluginHookAgainAfterModificationAndRejectsUnknownPlugins()
    {
        var pluginRoot = Path.Combine(_workspaceCraftPath, "plugins", "demo");
        WriteHookPlugin(pluginRoot, "demo-plugin", "echo plugin hook one", "echo plugin hook two");
        var runner = new HookRunner(new HookDiscoveryResult(), _tempRoot);
        using var harness = new CoreAppServerTestHarness(
            workspaceCraftPath: _workspaceCraftPath,
            hookRunner: runner);
        await harness.InitializeAsync();

        var untrusted = await PluginHooksAsync(harness, DotCraft.Protocol.AppServer.AppServerMethodNames.HooksList);
        Assert.Equal(2, untrusted.Count);
        Assert.All(untrusted, hook => Assert.Equal("untrusted", hook.GetProperty("trustStatus").GetString()));
        Assert.False(runner.HasToolHooks);

        var trusted = await PluginHooksAsync(harness, DotCraft.Protocol.AppServer.AppServerMethodNames.HooksTrustPlugin, new { pluginId = "demo-plugin" });
        Assert.Equal(2, trusted.Count);
        Assert.All(trusted, hook => Assert.Equal("trusted", hook.GetProperty("trustStatus").GetString()));
        Assert.True(runner.HasToolHooks);
        using (var config = JsonDocument.Parse(File.ReadAllText(harness.Monitor.Current.GlobalConfigPath!)))
        {
            var state = config.RootElement.GetProperty("Hooks").GetProperty("State");
            foreach (var hook in trusted)
                Assert.Equal(hook.GetProperty("currentHash").GetString(), state.GetProperty(hook.GetProperty("key").GetString()!).GetProperty("TrustedHash").GetString());
        }

        WriteHookPlugin(pluginRoot, "demo-plugin", "echo changed plugin hook");
        var modified = Assert.Single(await PluginHooksAsync(harness, DotCraft.Protocol.AppServer.AppServerMethodNames.HooksList));
        Assert.Equal("modified", modified.GetProperty("trustStatus").GetString());
        var modifiedHash = modified.GetProperty("currentHash").GetString()!;
        Assert.DoesNotContain(trusted, hook => hook.GetProperty("currentHash").GetString() == modifiedHash);

        var retrusted = Assert.Single(await PluginHooksAsync(harness, DotCraft.Protocol.AppServer.AppServerMethodNames.HooksTrustPlugin, new { pluginId = "demo-plugin" }));
        Assert.Equal("trusted", retrusted.GetProperty("trustStatus").GetString());
        using (var config = JsonDocument.Parse(File.ReadAllText(harness.Monitor.Current.GlobalConfigPath!)))
        {
            var state = config.RootElement.GetProperty("Hooks").GetProperty("State").GetProperty(retrusted.GetProperty("key").GetString()!);
            Assert.Equal(modifiedHash, state.GetProperty("TrustedHash").GetString());
        }

        await harness.ExecuteRequestAsync(harness.BuildRequest(
            DotCraft.Protocol.AppServer.AppServerMethodNames.HooksTrustPlugin,
            new { pluginId = "missing-plugin" }));
        using var response = await harness.Transport.ReadNextSentAsync();
        CoreAppServerTestHarness.AssertIsErrorResponse(response, -32602);
    }

    [Fact]
    public async Task PluginListAndView_IncludeHookSummaries()
    {
        WriteHookPlugin(Path.Combine(_workspaceCraftPath, "plugins", "demo"), "demo-plugin");
        using var harness = new CoreAppServerTestHarness(workspaceCraftPath: _workspaceCraftPath);
        await harness.InitializeAsync();

        await harness.ExecuteRequestAsync(harness.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.PluginList, new { includeDisabled = true }));
        using (var response = await harness.Transport.ReadNextSentAsync())
        {
            CoreAppServerTestHarness.AssertIsSuccessResponse(response);
            AssertHookSummary(Assert.Single(
                response.RootElement.GetProperty("result").GetProperty("plugins").EnumerateArray(),
                item => item.GetProperty("id").GetString() == "demo-plugin"));
        }

        await harness.ExecuteRequestAsync(harness.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.PluginView, new { id = "demo-plugin" }));
        using var view = await harness.Transport.ReadNextSentAsync();
        CoreAppServerTestHarness.AssertIsSuccessResponse(view);
        AssertHookSummary(view.RootElement.GetProperty("result").GetProperty("plugin"));

        static void AssertHookSummary(JsonElement plugin)
        {
            var hook = Assert.Single(plugin.GetProperty("hooks").EnumerateArray());
            Assert.Equal("demo-plugin:hooks/hooks.json:pre_tool_use:0:0", hook.GetProperty("key").GetString());
            Assert.Equal(nameof(HookEvent.PreToolUse), hook.GetProperty("eventName").GetString());
        }
    }

    private static async Task<List<JsonElement>> PluginHooksAsync(CoreAppServerTestHarness harness, string method, object? parameters = null)
    {
        await harness.ExecuteRequestAsync(harness.BuildRequest(method, parameters ?? new { }));
        using var response = await harness.Transport.ReadNextSentAsync();
        CoreAppServerTestHarness.AssertIsSuccessResponse(response);
        return response.RootElement.GetProperty("result").GetProperty("hooks").EnumerateArray()
            .Where(hook => hook.GetProperty("pluginId").GetString() == "demo-plugin")
            .Select(hook => hook.Clone())
            .ToList();
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
