using DotCraft.Abstractions;
using DotCraft.Commands.Custom;
using DotCraft.Configuration;
using DotCraft.Dreams;
using DotCraft.Memory;
using DotCraft.Skills;
using System.Text;

namespace DotCraft.Context;

/// <summary>
/// Builds the complete system prompt from workspace context, memory, and skills.
/// </summary>
public sealed class PromptBuilder(
    MemoryStore memoryStore,
    SkillsLoader skillsLoader,
    string craftPath,
    string workspacePath,
    CustomCommandLoader? customCommandLoader = null,
    bool sandboxEnabled = false,
    IReadOnlyList<string>? deferredMcpServerNames = null,
    string? subAgentProfilesSection = null,
    Func<IReadOnlyList<string>>? toolNamesProvider = null,
    bool skillVariantModeEnabled = false,
    SkillVariantTarget? skillVariantTarget = null,
    string? promptProfile = null,
    string? roleInstructions = null,
    IContextPageManager? contextPageManager = null,
    DreamStore? dreamStore = null,
    SubAgentWaitAgentTimeoutOptions? subAgentWaitAgentTimeoutOptions = null,
    IReadOnlyList<IThreadSystemPromptContextProvider>? threadSystemPromptContextProviders = null)
{
    private readonly string _craftPath = Path.GetFullPath(craftPath);

    private readonly string _workspacePath = Path.GetFullPath(workspacePath);

    /// <summary>
    /// Bootstrap files to load from DotCraft directory.
    /// </summary>
    private static readonly string[] BootstrapFiles =
    [
        "AGENTS.md",
        "SOUL.md",
        "USER.md",
        "TOOLS.md",
        "IDENTITY.md"
    ];

    /// <summary>
    /// Build the complete system prompt with identity, bootstrap files, memory, and skills.
    /// </summary>
    public string BuildSystemPrompt(string? threadId = null)
    {
        var subAgentLight = string.Equals(
            promptProfile,
            SubAgentPromptProfiles.Light,
            StringComparison.OrdinalIgnoreCase);
        var availableToolNames = toolNamesProvider?.Invoke();
        var parts = new List<string>
        {
            // Core identity and built-in operating guidance
            GetIdentity()
        };

        if (!string.IsNullOrWhiteSpace(subAgentProfilesSection))
            parts.Add(subAgentProfilesSection);

        if (!subAgentLight && IsToolAvailable(availableToolNames, "SpawnAgent"))
            parts.Add(GetSubAgentLifecyclePrompt(availableToolNames, subAgentWaitAgentTimeoutOptions));

        parts.Add(GetWorkingStylePrompt());
        parts.Add(GetResponseStylePrompt());
        parts.Add(GetEditingWorkflowPrompt());
        parts.Add(GetFileReferenceFormatPrompt());
        if (!subAgentLight)
        {
            parts.Add(GetModeProtocolPrompt());
            if (IsToolAvailable(availableToolNames, "RequestUserInput"))
                parts.Add(GetRequestUserInputPrompt());
        }

        // Bootstrap files (AGENTS.md, SOUL.md, USER.md, TOOLS.md, IDENTITY.md)
        var bootstrapContent = GetContextPage(
            threadId,
            ContextPageKeys.BootstrapFiles(BuildBootstrapVariant(subAgentLight)),
            () => LoadBootstrapFiles(agentsOnly: subAgentLight));
        if (!string.IsNullOrWhiteSpace(bootstrapContent))
        {
            parts.Add(bootstrapContent);
        }

        // Memory context
        if (!subAgentLight)
        {
            var memory = GetContextPage(
                threadId,
                ContextPageKeys.MemoryLongTerm(BuildMemoryVariant()),
                BuildMemoryContext);
            if (!string.IsNullOrWhiteSpace(memory))
                parts.Add($"# Memory\n\n{memory}");
        }

        // Skills - Progressive loading approach:
        // 1. Always-loaded skills: include full content
        if (!subAgentLight && IsToolAvailable(availableToolNames, "SkillManage"))
            parts.Add(GetSelfLearningPrompt());

        var skillsVariant = BuildSkillsVariant(availableToolNames);
        var alwaysContent = GetContextPage(
            threadId,
            ContextPageKeys.SkillsAlways(skillsVariant),
            () =>
            {
                var alwaysSkills = skillsLoader.GetAlwaysSkills(availableToolNames);
                return alwaysSkills.Count == 0
                    ? string.Empty
                    : skillsLoader.LoadSkillsForContext(
                        alwaysSkills,
                        skillVariantModeEnabled,
                        skillVariantTarget);
            });
        if (!string.IsNullOrWhiteSpace(alwaysContent))
        {
            parts.Add($"# Active Skills\n\n{alwaysContent}");
        }

        // 2. Available skills: show summary (agent uses ReadFile to load full content)
        var skillsSummary = GetContextPage(
            threadId,
            ContextPageKeys.SkillsSummary(skillsVariant),
            () => skillsLoader.BuildSkillsSummary(
                availableToolNames,
                skillVariantModeEnabled,
                skillVariantTarget));
        if (!string.IsNullOrWhiteSpace(skillsSummary))
        {
            var skillLoadInstruction = IsToolAvailable(availableToolNames, "SkillView")
                ? "Before replying, scan the available skills below. If a skill is relevant or even partially relevant to the task, you MUST load it with the SkillView tool and follow its instructions. Use ReadFile only when SkillView is unavailable or when you need to inspect a specific physical supporting file referenced by the loaded skill."
                : "Before replying, scan the available skills below. If a skill is relevant or even partially relevant to the task, you MUST read its SKILL.md file using the ReadFile tool and follow its instructions.";
            parts.Add(
$"""
# Skills (mandatory)

{skillLoadInstruction}

Err on the side of loading skills. Skills encode project workflows, pitfalls, user preferences, and quality standards that may outperform a general-purpose approach.

Only proceed without loading a skill if genuinely none of the listed skills are relevant to the task.

{skillsSummary}
"""
                );
        }

        // Custom commands summary
        if (!subAgentLight && customCommandLoader != null)
        {
            var commandsSummary = GetContextPage(
                threadId,
                ContextPageKeys.CustomCommandsSummary(_craftPath),
                customCommandLoader.BuildCommandsSummary);
            if (!string.IsNullOrWhiteSpace(commandsSummary))
                parts.Add(commandsSummary);
        }

        foreach (var provider in ChatContextRegistry.All)
        {
            var section = provider.GetSystemPromptSection();
            if (!string.IsNullOrWhiteSpace(section))
                parts.Add(section);
        }

        if (!string.IsNullOrWhiteSpace(threadId) && threadSystemPromptContextProviders is { Count: > 0 })
        {
            var promptContext = new ThreadSystemPromptContext(threadId.Trim(), _workspacePath);
            foreach (var provider in threadSystemPromptContextProviders)
            {
                var section = GetContextPage(
                    threadId,
                    provider.ContextPageKey,
                    () => provider.GetSystemPromptSection(promptContext) ?? string.Empty);
                if (!string.IsNullOrWhiteSpace(section))
                    parts.Add(section);
            }
        }

        // Deferred MCP tool discovery guidance (injected when deferred loading is active)
        if (!subAgentLight && deferredMcpServerNames is { Count: > 0 })
            parts.Add(BuildDeferredToolsSection(deferredMcpServerNames));

        if (subAgentLight)
            parts.Add(GetSubAgentLightPrompt(availableToolNames));

        if (!string.IsNullOrWhiteSpace(roleInstructions))
            parts.Add($"## Role Instructions\n\n{roleInstructions.Trim()}");

        return string.Join("\n\n---\n\n", parts);
    }

    private static bool IsToolAvailable(IReadOnlyList<string>? availableToolNames, string toolName) =>
        availableToolNames?.Any(name => string.Equals(name, toolName, StringComparison.OrdinalIgnoreCase)) == true;

    private string GetContextPage(
        string? threadId,
        ContextPageKey key,
        Func<string> loader) =>
        contextPageManager?.GetOrAdd(
            threadId,
            key,
            ContextPageLifecycle.StableUntilCompaction,
            loader).Content
        ?? loader();

    private string BuildBootstrapVariant(bool agentsOnly) =>
        $"{_craftPath}|agentsOnly:{agentsOnly.ToString().ToLowerInvariant()}";

    private string BuildMemoryVariant()
    {
        var sb = new StringBuilder();
        sb.Append("memory:");
        sb.Append(Path.GetFullPath(memoryStore.MemoryDirectoryPath));
        if (dreamStore != null)
        {
            sb.Append("|dreams:");
            sb.Append(Path.GetFullPath(dreamStore.DreamsDirectoryPath));
        }

        return sb.ToString();
    }

    private string BuildMemoryContext()
    {
        var parts = new List<string>();
        var longTerm = memoryStore.GetMemoryContext();
        if (!string.IsNullOrWhiteSpace(longTerm))
            parts.Add(longTerm);

        var dreamMemory = BuildDreamMemoryContext();
        if (!string.IsNullOrWhiteSpace(dreamMemory))
            parts.Add(dreamMemory);

        return string.Join("\n\n", parts);
    }

    private string BuildDreamMemoryContext()
    {
        var dream = dreamStore?.ReadDream();
        if (string.IsNullOrWhiteSpace(dream))
            return string.Empty;

        return
$"""
## Dream Memory

The following is inferred background context generated by scheduled Dreams. Use it as helpful workspace context, but do not treat it as explicit user instruction when it conflicts with direct instructions, project files, or MEMORY.md.
Detailed Dream topic files, when listed, live under .craft/dreams/memory/ and should be read on demand only when relevant.

{StripDreamMemoryHeading(dream)}
""";
    }

    private static string StripDreamMemoryHeading(string markdown)
    {
        var trimmed = markdown.Trim();
        if (trimmed.StartsWith("# Dream Memory", StringComparison.OrdinalIgnoreCase))
        {
            var nextLine = trimmed.IndexOf('\n');
            return nextLine < 0 ? string.Empty : trimmed[(nextLine + 1)..].TrimStart();
        }

        return trimmed;
    }

    private string BuildSkillsVariant(IReadOnlyList<string>? availableToolNames)
    {
        var sb = new StringBuilder();
        sb.Append("workspace:");
        sb.Append(_workspacePath);
        sb.Append("|skills:");
        sb.Append(skillsLoader.WorkspaceSkillsPath);
        sb.Append("|variantMode:");
        sb.Append(skillVariantModeEnabled.ToString().ToLowerInvariant());
        sb.Append("|target:");
        AppendSkillVariantTarget(sb, skillVariantTarget);
        sb.Append("|tools:");
        if (availableToolNames is { Count: > 0 })
            sb.Append(string.Join(",", availableToolNames.OrderBy(name => name, StringComparer.OrdinalIgnoreCase)));
        return sb.ToString();
    }

    private static void AppendSkillVariantTarget(StringBuilder sb, SkillVariantTarget? target)
    {
        if (target == null)
        {
            sb.Append("none");
            return;
        }

        sb.Append(target.Harness);
        sb.Append('|');
        sb.Append(target.HarnessVersion);
        sb.Append('|');
        sb.Append(target.Model);
        sb.Append('|');
        sb.Append(target.Os);
        sb.Append('|');
        sb.Append(target.Shell);
        sb.Append('|');
        sb.Append(target.Sandbox);
        sb.Append('|');
        sb.Append(target.ToolProfileHash);
        sb.Append('|');
        sb.Append(target.ApprovalPolicy);
        sb.Append('|');
        sb.Append(target.WorkspaceHash);
    }

    private static string GetSelfLearningPrompt()
    {
        return
"""
## Skill Self-Learning

You can create and maintain workspace skills with `SkillManage`. Skills are procedural memory: reusable, narrow instructions for task types that are likely to recur.

Create or update a skill after a complex task succeeds, especially after about 5+ tool calls, iterative troubleshooting, a tricky error fix, a user-corrected workflow, or an explicit request to remember a procedure. Do not create skills for simple one-off answers.

When you load a skill and find it stale, incomplete, wrong, using incorrect commands, or missing a pitfall discovered during the task, patch it before finishing with `SkillManage(action: "patch")`. Prefer `patch` for small corrections. For major rewrites, load the current skill with `SkillView` first and then use `edit`.

Prefer updating or generalizing an existing skill over creating a new one when the existing skill already covers the task class. Create new skills at the reusable task-class level, not for one exact session.

Newly created or updated skills may not affect the current prompt immediately; they are available after the next turn or session refresh.
""";
    }

    private static string GetSubAgentLifecyclePrompt(
        IReadOnlyList<string>? availableToolNames,
        SubAgentWaitAgentTimeoutOptions? waitAgentTimeoutOptions)
    {
        var timeoutOptions = waitAgentTimeoutOptions ?? SubAgentWaitAgentTimeoutOptions.Defaults;
        var hasSendMessage = IsToolAvailable(availableToolNames, "SendMessage");
        var hasFollowupTask = IsToolAvailable(availableToolNames, "FollowupTask");
        var hasWaitAgent = IsToolAvailable(availableToolNames, "WaitAgent");
        var hasListAgents = IsToolAvailable(availableToolNames, "ListAgents");
        var hasCloseAgent = IsToolAvailable(availableToolNames, "CloseAgent");

        var controls = new List<string>();
        if (hasListAgents)
            controls.Add("Use `ListAgents` to inspect available `agentPath` targets before sending follow-up work.");
        if (hasSendMessage)
            controls.Add("Use `SendMessage` for mailbox-only coordination; it records a message for the target and does not start a turn.");
        if (hasFollowupTask)
            controls.Add("Use `FollowupTask` to start or queue a target agent turn; set `deliveryMode` to `steer` only when a running native target should receive same-turn guidance, otherwise keep the default `queue`. Pending mailbox messages for that target are delivered with the task.");
        if (hasWaitAgent)
            controls.Add($"Use `WaitAgent` only when the parent is blocked on mailbox or SubAgent graph changes; `timeoutMs` is milliseconds, defaults to {timeoutOptions.DefaultTimeoutMs}, and must be between {timeoutOptions.MinTimeoutMs} and {timeoutOptions.MaxTimeoutMs}.");
        if (hasCloseAgent)
            controls.Add("Use `CloseAgent` with an `agentPath` when a child thread is no longer needed; do not keep idle child agents open indefinitely.");

        var controlsText = controls.Count == 0
            ? "- Track spawned agent paths and manage their results explicitly with the tools currently available."
            : "- " + string.Join("\n- ", controls);

        return
$$"""
## SubAgent Lifecycle

Use `SpawnAgent` for concrete sidecar work that can run while the parent keeps the critical path moving.

- Keep immediate blockers local; spawn parallel exploration, verification, or disjoint implementation work.
- Make each child prompt specific and self-contained; use `agentRole: "explorer"` for read-only research and `agentRole: "worker"` for bounded execution.
- Set a lowercase `taskName` using only letters, digits, and underscores; the child is addressed by `agentPath`, while `agentNickname` only controls display naming.
{{controlsText}}
- When a child finishes, review and integrate its result without redoing the same work.
""";
    }

    /// <summary>
    /// Builds the system prompt section that instructs the model how to discover
    /// deferred MCP tools via the <c>SearchTools</c> function.
    /// </summary>
    private static string BuildDeferredToolsSection(IReadOnlyList<string> serverNames)
    {
        var servers = string.Join(", ", serverNames);
        return
$$"""
## Available Tool Sources

You have a core set of tools available directly. Additional tools from external
services (MCP servers) are available on demand.

To use an external tool:
1. Call `SearchTools` with keywords describing what you need
2. The matching tools will become available for use
3. Call the discovered tool directly

Do NOT guess tool names. Always use SearchTools to discover available tools first.
Currently connected external services: {{servers}}
""";
    }

    /// <summary>
    /// Load bootstrap files from DotCraft directory.
    /// Bootstrap files provide additional context and instructions.
    /// </summary>
    /// <returns>Combined content of all bootstrap files, or empty string if none exist.</returns>
    private string LoadBootstrapFiles(bool agentsOnly = false)
    {
        var parts = new List<string>();

        foreach (var filename in BootstrapFiles)
        {
            if (agentsOnly && !string.Equals(filename, "AGENTS.md", StringComparison.OrdinalIgnoreCase))
                continue;

            var filePath = Path.Combine(_craftPath, filename);
            if (File.Exists(filePath))
            {
                try
                {
                    var content = File.ReadAllText(filePath, System.Text.Encoding.UTF8);
                    if (!string.IsNullOrWhiteSpace(content))
                    {
                        parts.Add($"## {filename}\n\n{content}");
                    }
                }
                catch (Exception ex)
                {
                    // Log warning but continue loading other files
                    Console.Error.WriteLine($"[Warning] Failed to load bootstrap file {filename}: {ex.Message}");
                }
            }
        }

        return parts.Count > 0 ? string.Join("\n\n", parts) : string.Empty;
    }

    private static string GetSubAgentLightPrompt(IReadOnlyList<string>? availableToolNames)
    {
        var tools = availableToolNames is { Count: > 0 }
            ? string.Join(", ", availableToolNames.OrderBy(name => name, StringComparer.OrdinalIgnoreCase))
            : "No tools are currently exposed.";

        return
$$"""
## SubAgent Context

You are running as a session-backed SubAgent. The parent agent owns final synthesis; your job is to complete the assigned task and return concise, concrete results.

Available tools for this role: {{tools}}

Rules:
- Stay within the assigned task and role.
- Use only tools that are actually available in this thread.
- Do not assume write, shell, web, or agent-control access unless the tool is listed.
- Final response should summarize findings, actions, changed files if any, and validation performed.
""";
    }

    private string GetIdentity()
    {
        var workspace = sandboxEnabled ? "/workspace" : _workspacePath;
        var craftPath = _craftPath;
        var envSection = sandboxEnabled ? GetSandboxEnvironmentSection() : GetHostEnvironmentSection();

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

## DotCraft Directory
Your data directory is at: {{craftPath}}
This contains:
- Memory: {{craftPath}}/memory/ (see Memory skill for details)
- Custom skills: {{craftPath}}/skills/{skill-name}/SKILL.md
- Configuration: {{craftPath}}/config.json

{{envSection}}

## Tool Usage Policy
Use the available tools deliberately to gather context, make changes, validate work, and manage long-running collaboration when those tools are exposed.

## Git Commit Attribution
When creating git commits for the user, do not change git config. End commit messages with:
Co-authored-by: DotCraft Bot <273930855+DotCraft-Bot@users.noreply.github.com>
""";
    }

    private static string GetWorkingStylePrompt()
    {
        return
"""
## Working Style
- Before the first tool call in a task, briefly explain what you are about to do in 1-2 sentences.
- If several related tool calls are coming next, group them under one short explanation instead of narrating each trivial action.
- Keep these explanations concrete and forward-looking: focus on your current read of the task and the immediate next step.
- During longer exploration, searching, testing, or editing stretches, send brief progress updates when they help the user follow your work.
- Before making file edits, briefly explain what you are going to change and why.
""";
    }

    private static string GetResponseStylePrompt()
    {
        return
"""
## Response Style
- Be concise, direct, and useful. Lead with the answer, outcome, or blocker.
- Do not restate the request, narrate routine actions, or list every tool call or file read.
- Use structure only when it helps; simple answers should be one sentence or one short paragraph.
- During work, update only for meaningful findings, milestones, blockers, or decisions needing input.
- Final responses should cover what changed or was found, relevant files, validation, and any real next step. Expand when the user asks for detail.
""";
    }

    private static string GetEditingWorkflowPrompt()
    {
        return
"""
## File Editing Workflow
- Prefer `EditFile` when changing an existing file.
- Use `WriteFile` for new files or intentional full rewrites.
- Read the file before editing.
- In `EditFile`, use the smallest unique `oldText` snippet that can identify the target.
- If a large edit can be done as several precise replacements, prefer that over rewriting the whole file.
- If an edit fails, re-read and retry instead of immediately switching to `WriteFile`.
""";
    }

    private static string GetFileReferenceFormatPrompt()
    {
        return
"""
## File References
When referencing a file in your final response, wrap it as a markdown link `[label](target)` so the user can open it on click.
- `target` may be workspace-relative, absolute, or a `file://` URL; append `:line[:col]` for a line hint.
- Each reference must be a standalone link; do not wrap `target` in backticks.
- Inline code (`` ` ``) stays reserved for code identifiers, commands, and non-clickable text.
- Examples: [app.ts](src/app.ts), [app.ts:42](src/app.ts:42), [main.rs:12:5](C:/repo/project/main.rs:12:5).
""";
    }

    private static string GetModeProtocolPrompt()
    {
        return
"""
## Mode Protocol

The current operational mode is provided in the latest system reminder runtime context. Treat that runtime context as the source of truth for the current turn.

Runtime context fields:
- CurrentMode is Plan or Agent.
- ModeTransition is None or PlanToAgent.
- AllowedActionProfile describes the action class the execution policy allows for this turn.
- PlanState describes whether a saved plan is available for this thread.

The latest `## Mode Action` block is an instruction, not telemetry. Follow it when deciding whether to explore, create a plan, update task progress, or perform workspace-changing actions.

### Plan Mode

Plan mode is read-only. Use tools for observation, code search, reading files, web research, and planning. Do not intentionally modify files, write stdin, install packages, commit, push, delete, move, or run mutating shell commands. Do not create, read, update, or complete thread goals in Plan mode. When the implementation plan is ready, call CreatePlan.

If you accidentally call a tool that the execution policy rejects, read the denial result and continue with an allowed read-only or planning action.

### Agent Mode

Agent mode may execute approved workspace changes according to the normal approval and sandbox policy. When an active plan exists or ModeTransition is PlanToAgent, follow the plan and keep progress state current for non-trivial work.

### Task State

CreatePlan records an implementation plan. UpdateTodos and TodoWrite are for execution tracking and substantial multi-step work. Do not use task tools for simple informational answers or one obvious change.

TodoWrite is a conditional organizational tool, not a default progress tracker. Use it proactively only when the task genuinely benefits from structured tracking; otherwise just do the work directly.

Use TodoWrite for complex multi-step tasks, non-trivial tasks requiring planning or multiple operations, explicit user-provided task lists, or when brief exploration reveals a larger scope. Do not use it for informational answers, a single obvious change, one command execution, or anything completable in fewer than three non-trivial steps.

For non-trivial work in an unfamiliar area, do 1-2 reads or searches first, then write a concrete task list. Exactly one task is in_progress at a time, and completed tasks should be marked immediately after they are fully done.
""";
    }

    private static string GetRequestUserInputPrompt()
    {
        return
"""
## RequestUserInput

Use `RequestUserInput` only when it is listed in the available tools for this turn.

In Plan mode, after targeted non-mutating exploration, use `RequestUserInput` for user decisions that materially change the plan. Ask only questions that cannot be answered by repo or environment exploration. Do not ask meaningful multiple-choice questions as plain assistant text when this tool is available.

Question shape: prefer 1 question and never exceed 3. Each question has 2-3 meaningful, mutually exclusive options. Put the recommended option first and suffix its label with `(Recommended)`. Do not include an `Other` option; the client adds free-form input automatically.

In Agent mode, prefer reasonable assumptions and execution; ask only when the user requested a choice or guessing is risky.
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
