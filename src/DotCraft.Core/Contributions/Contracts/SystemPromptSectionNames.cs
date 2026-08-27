namespace DotCraft.Contributions;

/// <summary>The stable Tier-B target names of the built-in system prompt sections, each registered as its <see cref="ContributionOptions.TargetName"/>.</summary>
public static class SystemPromptSectionNames
{
    /// <summary>Product identity, workspace paths, environment, and tool usage policy.</summary>
    public const string Identity = "identity";

    /// <summary>The available SubAgent profiles, present only when <c>SpawnAgent</c> is exposed.</summary>
    public const string SubAgentProfiles = "subagent-profiles";

    /// <summary>SubAgent spawning and coordination guidance.</summary>
    public const string SubAgentLifecycle = "subagent-lifecycle";

    /// <summary>AGENTS.md precedence, directory scope, and nested discovery guidance.</summary>
    public const string ProjectInstructions = "project-instructions";

    /// <summary>How the agent narrates its work.</summary>
    public const string WorkingStyle = "working-style";

    /// <summary>How the agent shapes its replies.</summary>
    public const string ResponseStyle = "response-style";

    /// <summary>File editing tool preferences.</summary>
    public const string EditingWorkflow = "editing-workflow";

    /// <summary>The markdown link format for file references.</summary>
    public const string FileReferences = "file-references";

    /// <summary>Plan and Agent mode rules and task-state tooling.</summary>
    public const string ModeProtocol = "mode-protocol";

    /// <summary>When and how to ask the user a structured question.</summary>
    public const string RequestUserInput = "request-user-input";

    /// <summary>The workspace bootstrap markdown files loaded from the DotCraft directory.</summary>
    public const string BootstrapFiles = "bootstrap-files";

    /// <summary>Long-term memory and Dream memory.</summary>
    public const string Memory = "memory";

    /// <summary>Skill self-learning guidance.</summary>
    public const string SelfLearning = "self-learning";

    /// <summary>Fully inlined content of the always-loaded skills.</summary>
    public const string ActiveSkills = "active-skills";

    /// <summary>The progressive-loading summary of the remaining skills.</summary>
    public const string SkillsSummary = "skills-summary";

    /// <summary>The workspace custom command summary.</summary>
    public const string CustomCommands = "custom-commands";

    /// <summary>The sections contributed by <see cref="Context.IChatContextProvider"/> contributions.</summary>
    public const string ChatContext = "chat-context";

    /// <summary>The base-instruction sections contributed by <see cref="Context.IThreadSystemPromptContextProvider"/> contributions.</summary>
    public const string ThreadContext = "thread-context";

    /// <summary>Discovery guidance for deferred MCP tools.</summary>
    public const string DeferredTools = "deferred-tools";

    /// <summary>The channel or automation role instructions.</summary>
    public const string RoleInstructions = "role-instructions";
}
