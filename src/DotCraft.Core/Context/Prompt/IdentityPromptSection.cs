using DotCraft.Contributions;
using DotCraft.Plugins;
using DotCraft.Security.ShellCommands;

namespace DotCraft.Context;

/// <summary>Produces the <c>identity</c> section: who the agent is, where the data directory lives, the target shell, and the commit attribution rule.</summary>
internal static class IdentityPromptSection
{
    /// <summary>Builds the section content.</summary>
    internal static string Build(SystemPromptSectionContext context)
    {
        var sources = context.RequireSources();
        var craftPath = sources.CraftPath;
        var envSection = GetHostEnvironmentSection();
        var identity = GetIdentityLine();
        var attributionSection = GetCommitAttributionSection(sources.CommitAttribution);

        return
$$"""
# DotCraft

{{identity}} Use the available tools to gather context, complete the user's task, and validate your work.

## DotCraft Directory
Your data directory is at: {{craftPath}}
This contains:
- Custom skills: {{craftPath}}/skills/{skill-name}/SKILL.md
- Configuration: {{craftPath}}/config.json

{{envSection}}
{{attributionSection}}
""";
    }

    private static string GetCommitAttributionSection(string? attribution)
    {
        if (string.IsNullOrWhiteSpace(attribution))
            return string.Empty;

        return
$"""

## Git Commit Attribution
When creating git commits for the user, do not change git config. End commit messages with:
{attribution.Trim()}
""";
    }

    private static string GetIdentityLine()
    {
        // An unversioned build of DotCraft.Core reports 0.0.0 and names no version.
        var product = PluginHostVersion.Current.ProductText;
        return product == "0.0.0"
            ? "You are DotCraft, a helpful AI assistant."
            : $"You are DotCraft, a helpful AI assistant running DotCraft {product}.";
    }

    private static string GetHostEnvironmentSection()
    {
        string osName;
        string shell;
        string? shellPath = null;

        if (OperatingSystem.IsWindows())
        {
            var version = Environment.OSVersion.Version;
            osName = $"Windows {version.Major}.{version.Minor} (Build {version.Build})";
            ShellIdentityResolver.Host.TryResolve(null, out var identity, out _);
            shellPath = identity?.ExecutablePath;
            shell = identity?.Kind switch
            {
                ShellKind.Pwsh => "PowerShell (pwsh)",
                ShellKind.PowerShell => "Windows PowerShell",
                ShellKind.Cmd => "Command Prompt (cmd)",
                _ => "Unavailable"
            };
        }
        else
        {
            osName = OperatingSystem.IsMacOS() ? "macOS" : "Linux";
            shell = "Bash";
            shellPath = "/bin/bash";
        }

        return
$$"""
## Environment
- OS: {{osName}}
- Default shell: {{shell}}{{(shellPath is null ? string.Empty : $" ({shellPath})")}}
""";
    }

}
