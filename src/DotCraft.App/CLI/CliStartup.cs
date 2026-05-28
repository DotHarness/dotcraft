namespace DotCraft.CLI;

internal enum WorkspaceStartupDecision
{
    Continue,
    InitializeInteractively,
    ShowUsage,
    MissingWorkspace
}

internal static class CliStartup
{
    public static bool IsHeadlessMode(CommandLineArgs.RunMode mode) =>
        mode is CommandLineArgs.RunMode.Acp
            or CommandLineArgs.RunMode.AppServer
            or CommandLineArgs.RunMode.Gateway
            or CommandLineArgs.RunMode.Hub
            or CommandLineArgs.RunMode.Skill
            or CommandLineArgs.RunMode.Dashboard
            or CommandLineArgs.RunMode.Exec;

    public static WorkspaceStartupDecision DecideWorkspaceStartup(
        CommandLineArgs.RunMode mode,
        bool workspaceExists)
    {
        if (mode == CommandLineArgs.RunMode.None)
            return workspaceExists
                ? WorkspaceStartupDecision.ShowUsage
                : WorkspaceStartupDecision.InitializeInteractively;

        if (!workspaceExists && IsHeadlessMode(mode))
            return WorkspaceStartupDecision.MissingWorkspace;

        return WorkspaceStartupDecision.Continue;
    }

    public static async Task WriteUsageAsync(TextWriter writer)
    {
        await writer.WriteLineAsync("Usage: dotcraft exec <prompt>").ConfigureAwait(false);
        await writer.WriteLineAsync("       dotcraft exec -").ConfigureAwait(false);
        await writer.WriteLineAsync("       dotcraft dashboard [--workspace <dir>] [--host <host>] [--port <port>]").ConfigureAwait(false);
        await writer.WriteLineAsync("       dotcraft app-server | gateway | hub | acp | setup | skill").ConfigureAwait(false);
        await writer.WriteLineAsync("       dotcraft auth openai <login|logout|status> [--provider-id <id>] [--no-browser]").ConfigureAwait(false);
    }
}
