using DotCraft.Contributions;
using DotCraft.Plugins;
using DotCraft.Security.ShellCommands;

namespace DotCraft.Context;

/// <summary>Produces the <c>identity</c> section: who the agent is, where workspace and data directory live, the target shell, and the commit attribution rule.</summary>
internal static class IdentityPromptSection
{
    /// <summary>Builds the section content.</summary>
    internal static string Build(SystemPromptSectionContext context)
    {
        var sources = context.RequireSources();
        var workspace = sources.WorkspacePath;
        var craftPath = sources.CraftPath;
        var envSection = GetHostEnvironmentSection();
        var workspaceRootsSection = GetWorkspaceRootsSection(sources);
        var identity = GetIdentityLine();

        return
$$"""
# DotCraft

{{identity}} Use the available tools to gather context, complete the user's task, and validate your work.

## Workspace
Your working directory for file and shell operations is: {{workspace}}

{{workspaceRootsSection}}

## DotCraft Directory
Your data directory is at: {{craftPath}}
This contains:
- Custom skills: {{craftPath}}/skills/{skill-name}/SKILL.md
- Configuration: {{craftPath}}/config.json

{{envSection}}

## Git Commit Attribution
When creating git commits for the user, do not change git config. End commit messages with:
Co-authored-by: DotCraft <273930855+dotcraft-ai@users.noreply.github.com>
""";
    }

    private static string GetIdentityLine()
    {
        // The entry assembly carries the product version; test hosts and embedding apps report 0.0.0 or their own.
        var product = PluginHostVersion.Current.ProductText;
        return product == "0.0.0"
            ? "You are DotCraft, a helpful AI assistant."
            : $"You are DotCraft, a helpful AI assistant running DotCraft {product}.";
    }

    private static string GetWorkspaceRootsSection(PromptSectionSources sources)
    {
        var roots = sources.WorkspaceRoots;
        if (roots.Count == 0
            || (roots.Count == 1
                && string.Equals(roots[0], sources.WorkspacePath, StringComparison.OrdinalIgnoreCase)))
        {
            return string.Empty;
        }

        var rendered = string.Join(Environment.NewLine, roots.Select(root => $"- {root}"));
        return
$"""
## Workspace Roots
{rendered}
""";
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
