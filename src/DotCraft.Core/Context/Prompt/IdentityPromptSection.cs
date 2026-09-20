using DotCraft.Contributions;
using DotCraft.Plugins;

namespace DotCraft.Context;

/// <summary>Produces the <c>identity</c> section: who the agent is, where workspace and data directory live, the target shell, and the commit attribution rule.</summary>
internal static class IdentityPromptSection
{
    /// <summary>Builds the section content.</summary>
    internal static string Build(SystemPromptSectionContext context)
    {
        var sources = context.RequireSources();
        var workspace = sources.SandboxEnabled ? "/workspace" : sources.WorkspacePath;
        var craftPath = sources.CraftPath;
        var envSection = sources.SandboxEnabled
            ? GetSandboxEnvironmentSection()
            : GetHostEnvironmentSection();
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

        var rendered = string.Join(
            Environment.NewLine,
            roots.Select((root, index) =>
            {
                if (!sources.SandboxEnabled)
                    return $"- {root}";
                var sandboxPath = string.Equals(root, sources.WorkspacePath, StringComparison.OrdinalIgnoreCase)
                    ? "/workspace"
                    : $"/workspace-roots/{index}";
                return $"- {sandboxPath}";
            }));
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
        string shellTips;

        if (OperatingSystem.IsWindows())
        {
            var version = Environment.OSVersion.Version;
            osName = $"Windows {version.Major}.{version.Minor} (Build {version.Build})";
            shell = "PowerShell";
            shellTips =
"""
  - Environment variables: `$env:VAR_NAME`
  - Command existence: `Get-Command <name>` (not `which`)
  - Null discard: `$null` (not `/dev/null`)
  - Path separator: `\` (use quotes for paths with spaces)
  - Chaining: `;` to sequence, `&&` requires PowerShell 7+
""";
        }
        else if (OperatingSystem.IsMacOS())
        {
            osName = "macOS";
            shell = "Bash";
            shellTips =
"""
  - Standard Unix/Bash syntax applies
  - Use `/bin/bash` compatible commands
""";
        }
        else
        {
            osName = "Linux";
            shell = "Bash";
            shellTips =
"""
  - Standard Unix/Bash syntax applies
""";
        }

        return
$$"""
## Environment
- OS: {{osName}}
- Shell: {{shell}}

When using the Exec tool, write commands for {{shell}}. Key syntax notes:
{{shellTips}}
""";
    }

    private static string GetSandboxEnvironmentSection()
    {
        return
"""
## Environment
- OS: Linux (sandbox container)
- Shell: Bash

When using the Exec tool, write standard Bash commands.
""";
    }
}
