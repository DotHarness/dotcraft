using DotCraft.Agents;
using DotCraft.Commands.Custom;
using DotCraft.Configuration;
using DotCraft.Contributions;
using DotCraft.Tracing;
using DotCraft.Memory;
using DotCraft.Dreams;
using DotCraft.Skills;
using Microsoft.Extensions.Logging;

namespace DotCraft.Context;

/// <summary>
/// Enhanced context provider combining memory, skills, and system prompt.
/// </summary>
public sealed class MemoryContextProvider(
    MemoryStore memoryStore,
    SkillsLoader skillsLoader,
    string dotCraftPath,
    string workspacePath,
    Func<IReadOnlyList<string>>? toolNamesProvider = null,
    CustomCommandLoader? customCommandLoader = null,
    bool sandboxEnabled = false,
    IReadOnlyList<string>? deferredMcpServerNames = null,
    string? subAgentProfilesSection = null,
    bool skillVariantModeEnabled = false,
    SkillVariantTarget? skillVariantTarget = null,
    string? roleInstructions = null,
    string? developerInstructions = null,
    IContextPageManager? contextPageManager = null,
    DreamStore? dreamStore = null,
    SubAgentWaitAgentTimeoutOptions? subAgentWaitAgentTimeoutOptions = null,
    string? threadId = null,
    string? originChannel = null,
    IReadOnlyList<string>? workspaceRoots = null,
    ILoggerFactory? loggerFactory = null,
    IContributionView? contributions = null) : AIContextProvider
{
    private readonly PromptBuilder _promptBuilder = new(
        memoryStore,
        skillsLoader,
        dotCraftPath,
        workspacePath,
        customCommandLoader,
        sandboxEnabled,
        deferredMcpServerNames,
        subAgentProfilesSection,
        toolNamesProvider,
        skillVariantModeEnabled,
        skillVariantTarget,
        roleInstructions,
        developerInstructions,
        contextPageManager,
        dreamStore,
        subAgentWaitAgentTimeoutOptions,
        originChannel,
        workspaceRoots,
        loggerFactory?.CreateLogger<PromptBuilder>(),
        contributions);

    public ValueTask<string> ProvideInstructionsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // An unbound build falls back to the active session so the prompt still resolves its thread's context pages.
        var promptThreadId = threadId
            ?? TracingChatClient.CurrentSessionKey
            ?? TracingChatClient.GetActiveSessionKey();
        return ValueTask.FromResult(_promptBuilder.BuildSystemPrompt(promptThreadId));
    }

    protected override async ValueTask<AIContext> ProvideAIContextAsync(
        InvokingContext context,
        CancellationToken cancellationToken = default) =>
        new()
        {
            Instructions = await ProvideInstructionsAsync(cancellationToken).ConfigureAwait(false)
        };
}
