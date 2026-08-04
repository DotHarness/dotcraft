using DotCraft.CLI;
using Xunit;

namespace DotCraft.Tests.CLI;

public sealed class CommandLineArgsTests
{
    [Theory]
    [InlineData("", CommandLineArgs.RunMode.None)]
    [InlineData("gateway", CommandLineArgs.RunMode.Gateway)]
    [InlineData("hub", CommandLineArgs.RunMode.Hub)]
    public void Parse_ModeOnlySubcommands_UseExpectedMode(string command, CommandLineArgs.RunMode expectedMode)
    {
        string[] argv = string.IsNullOrEmpty(command) ? [] : [command];
        var args = CommandLineArgs.Parse(argv);

        Assert.Equal(expectedMode, args.Mode);
        Assert.False(args.ReservesStdout);
    }

    [Fact]
    public void Parse_DashboardSubcommand_ParsesWorkspaceAndEndpointFlags()
    {
        var args = CommandLineArgs.Parse([
            "dashboard",
            "--workspace", @"E:\workspaces\demo",
            "--host", "127.0.0.1",
            "--port", "8081"
        ]);

        Assert.Equal(CommandLineArgs.RunMode.Dashboard, args.Mode);
        Assert.Equal(@"E:\workspaces\demo", args.DashboardWorkspacePath);
        Assert.Equal("127.0.0.1", args.DashboardHost);
        Assert.Equal(8081, args.DashboardPort);
        Assert.False(args.ReservesStdout);
    }

    [Fact]
    public void Parse_ExecSubcommand_ParsesPrompt()
    {
        var args = CommandLineArgs.Parse(["exec", "summarize", "this"]);

        Assert.Equal(CommandLineArgs.RunMode.Exec, args.Mode);
        Assert.Equal("summarize this", args.ExecPrompt);
        Assert.False(args.ExecReadStdin);
        Assert.True(args.ReservesStdout);
    }

    [Fact]
    public void Parse_ExecSubcommand_WithStdinSentinel_ReadsStdin()
    {
        var args = CommandLineArgs.Parse(["exec", "-"]);

        Assert.Equal(CommandLineArgs.RunMode.Exec, args.Mode);
        Assert.Null(args.ExecPrompt);
        Assert.True(args.ExecReadStdin);
        Assert.True(args.ReservesStdout);
    }

    [Fact]
    public void Parse_ExecSubcommand_WithRemoteAndToken_ParsesConnectionFlags()
    {
        var args = CommandLineArgs.Parse([
            "exec",
            "--remote",
            "ws://127.0.0.1:9100/ws",
            "--token",
            "secret",
            "hello"
        ]);

        Assert.Equal(CommandLineArgs.RunMode.Exec, args.Mode);
        Assert.Equal("ws://127.0.0.1:9100/ws", args.RemoteUrl);
        Assert.Equal("secret", args.Token);
        Assert.Equal("hello", args.ExecPrompt);
        Assert.True(args.ReservesStdout);
    }

    [Fact]
    public void Parse_AppServerSubcommand_UsesAppServerMode()
    {
        var args = CommandLineArgs.Parse(["app-server", "--listen", "ws://127.0.0.1:9100"]);

        Assert.Equal(CommandLineArgs.RunMode.AppServer, args.Mode);
        Assert.Equal("ws://127.0.0.1:9100", args.ListenUrl);
        Assert.False(args.ReservesStdout);
    }

    [Fact]
    public void Parse_SetupSubcommand_ParsesSetupFlags()
    {
        var args = CommandLineArgs.Parse([
            "setup",
            "--language", "English",
            "--model", "gpt-4o-mini",
            "--endpoint", "https://api.openai.com/v1",
            "--api-key", "sk-test",
            "--profile", "developer",
            "--save-user-config",
            "--prefer-existing-user-config"
        ]);

        Assert.Equal(CommandLineArgs.RunMode.Setup, args.Mode);
        Assert.Equal("gpt-4o-mini", args.SetupModel);
        Assert.Equal("https://api.openai.com/v1", args.SetupEndPoint);
        Assert.Equal("sk-test", args.SetupApiKey);
        Assert.Equal("developer", args.SetupProfile);
        Assert.True(args.SaveUserConfig);
        Assert.True(args.PreferExistingUserConfig);
        Assert.False(args.ReservesStdout);
    }

    [Fact]
    public void Parse_SetupSubcommand_ParsesProviderFlags()
    {
        var args = CommandLineArgs.Parse([
            "setup",
            "--profile", "developer",
            "--provider-mode", "create",
            "--provider-id", "anthropic",
            "--provider-display-name", "Anthropic",
            "--provider-protocol", "anthropic",
            "--endpoint", "https://api.anthropic.com",
            "--api-key", "sk-ant",
            "--provider-timeout-seconds", "120",
            "--model", "claude-sonnet-4-5",
            "--set-user-default"
        ]);

        Assert.Equal(CommandLineArgs.RunMode.Setup, args.Mode);
        Assert.Equal("create", args.SetupProviderMode);
        Assert.Equal("anthropic", args.SetupProviderId);
        Assert.Equal("Anthropic", args.SetupProviderDisplayName);
        Assert.Equal("anthropic", args.SetupProviderProtocol);
        Assert.Equal("120", args.SetupProviderTimeoutSeconds);
        Assert.True(args.SetupSetUserDefault);
        Assert.Equal("https://api.anthropic.com", args.SetupEndPoint);
        Assert.Equal("sk-ant", args.SetupApiKey);
        Assert.Equal("claude-sonnet-4-5", args.SetupModel);
    }

    [Fact]
    public void Parse_ModelCatalogSubcommand_ParsesProviderAndStdinFlags()
    {
        var providerArgs = CommandLineArgs.Parse(["model-catalog", "--provider-id", "anthropic"]);
        var stdinArgs = CommandLineArgs.Parse(["model-catalog", "--stdin"]);

        Assert.Equal(CommandLineArgs.RunMode.ModelCatalog, providerArgs.Mode);
        Assert.Equal("anthropic", providerArgs.SetupProviderId);
        Assert.False(providerArgs.ModelCatalogReadStdin);
        Assert.True(providerArgs.ReservesStdout);
        Assert.Equal(CommandLineArgs.RunMode.ModelCatalog, stdinArgs.Mode);
        Assert.True(stdinArgs.ModelCatalogReadStdin);
    }

    [Fact]
    public void Parse_AuthLoginSubcommand_ParsesNoBindFlag()
    {
        var args = CommandLineArgs.Parse(["auth", "openai", "login", "--provider-id", "setup", "--no-bind"]);

        Assert.Equal(CommandLineArgs.RunMode.Auth, args.Mode);
        Assert.Equal("setup", args.SetupProviderId);
        Assert.True(args.AuthNoBind);
    }

    [Fact]
    public void Parse_SkillSubcommand_ParsesSkillFlags()
    {
        var args = CommandLineArgs.Parse([
            "skill",
            "install",
            "--candidate", "tmp/demo",
            "--name", "demo-skill",
            "--source", "local",
            "--overwrite",
            "--json"
        ]);

        Assert.Equal(CommandLineArgs.RunMode.Skill, args.Mode);
        Assert.Equal("install", args.SkillCommand);
        Assert.Equal("tmp/demo", args.SkillCandidatePath);
        Assert.Equal("demo-skill", args.SkillName);
        Assert.Equal("local", args.SkillSource);
        Assert.True(args.SkillOverwrite);
        Assert.True(args.SkillJson);
        Assert.False(args.ReservesStdout);
    }

    [Fact]
    public void Parse_ContextExportSubcommand_ParsesExportFlags()
    {
        var args = CommandLineArgs.Parse([
            "context",
            "export",
            "--thread", "thread_20260601_ab12cd",
            "--workspace", @"E:\workspaces\demo",
            "--output", "handoff.md",
            "--profile", "transcript",
            "--tool-results", "none",
            "--history", "full"
        ]);

        Assert.Equal(CommandLineArgs.RunMode.Context, args.Mode);
        Assert.Equal("export", args.ContextCommand);
        Assert.Equal("thread_20260601_ab12cd", args.ContextThreadId);
        Assert.Equal(@"E:\workspaces\demo", args.ContextWorkspacePath);
        Assert.Equal("handoff.md", args.ContextOutputPath);
        Assert.Equal("transcript", args.ContextProfile);
        Assert.Equal("none", args.ContextToolResults);
        Assert.Equal("full", args.ContextHistory);
        Assert.Null(args.DashboardWorkspacePath);
        Assert.False(args.ReservesStdout);
    }

    [Fact]
    public void Parse_ContextSearchSubcommand_ParsesSearchFlags()
    {
        var args = CommandLineArgs.Parse([
            "context",
            "search",
            "--query=context explosion",
            "--workspace=.",
            "--limit=3",
            "--status=archived",
            "--json"
        ]);

        Assert.Equal(CommandLineArgs.RunMode.Context, args.Mode);
        Assert.Equal("search", args.ContextCommand);
        Assert.Equal("context explosion", args.ContextQuery);
        Assert.Equal(".", args.ContextWorkspacePath);
        Assert.Equal(3, args.ContextLimit);
        Assert.Equal("archived", args.ContextStatus);
        Assert.True(args.ContextJson);
        Assert.False(args.SkillJson);
        Assert.False(args.ReservesStdout);
    }
}
