namespace DotCraft.Tests.Sessions.Protocol;

internal static class TestHookCommands
{
    public static string Shell => OperatingSystem.IsWindows() ? "cmd" : "sh";

    public static string Echo(string output) =>
        OperatingSystem.IsWindows() ? $"echo {output}" : $"printf '%s\\n' '{output}'";

    public static string EchoAndExit(string output, int exitCode) =>
        OperatingSystem.IsWindows()
            ? $"echo {output}& exit /b {exitCode}"
            : $"printf '%s\\n' '{output}'; exit {exitCode}";

    public static string StderrAndExit(string output, int exitCode) =>
        OperatingSystem.IsWindows()
            ? $"echo {output} 1>&2& exit /b {exitCode}"
            : $"printf '%s\\n' '{output}' >&2; exit {exitCode}";

    public static string AppendLine(string relativePath, string line) =>
        OperatingSystem.IsWindows()
            ? $"echo {line}>>{relativePath}"
            : $"printf '%s\\n' '{line}' >> '{relativePath}'";

    public static string PrintFile(string relativePath) =>
        OperatingSystem.IsWindows() ? $"type {relativePath}" : $"cat '{relativePath}'";
}
