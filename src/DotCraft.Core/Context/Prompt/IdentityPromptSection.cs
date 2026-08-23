using DotCraft.Contributions;

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

        return
$$"""
# DotCraft

You are DotCraft, a helpful AI assistant. You have access to tools that allow you to:
- Read, write, and edit files
- Execute shell commands
- Complete user tasks efficiently

Be safe, reliable, and practical. When needed, use the available tools to complete the user's task.

## Workspace
Your workspace is at: {{workspace}}
This is your working directory where you perform file and shell operations.

{{workspaceRootsSection}}

## DotCraft Directory
Your data directory is at: {{craftPath}}
This contains:
- Memory: {{craftPath}}/memory/ (long-term context and history files)
- Custom skills: {{craftPath}}/skills/{skill-name}/SKILL.md
- Configuration: {{craftPath}}/config.json

{{envSection}}

## Tool Usage Policy
Use the available tools deliberately to gather context, make changes, validate work, and manage long-running collaboration when those tools are exposed.

## Git Commit Attribution
When creating git commits for the user, do not change git config. End commit messages with:
Co-authored-by: DotCraft <273930855+dotcraft-ai@users.noreply.github.com>
""";
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
  - Variables: `$env:VAR_NAME` (not `$VAR_NAME`)
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
