using DotCraft.CLI;

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
        Assert.Equal("English", args.SetupLanguage);
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
            "--language", "English",
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
}
