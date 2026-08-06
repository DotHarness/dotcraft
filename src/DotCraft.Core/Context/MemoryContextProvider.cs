using DotCraft.Agents;
using DotCraft.Commands.Custom;
using DotCraft.Configuration;
using DotCraft.Tracing;
using DotCraft.Memory;
using DotCraft.Dreams;
using DotCraft.Skills;

namespace DotCraft.Context;

/// <summary>
/// Enhanced context provider combining memory, skills, and system prompt.
/// </summary>
public sealed class MemoryContextProvider(
    MemoryStore memoryStore,
    SkillsLoader skillsLoader,
    string dotCraftPath,
    string workspacePath,
    TraceCollector? traceCollector = null,
    Func<IReadOnlyList<string>>? toolNamesProvider = null,
    CustomCommandLoader? customCommandLoader = null,
    bool sandboxEnabled = false,
    IReadOnlyList<string>? deferredMcpServerNames = null,
    string? subAgentProfilesSection = null,
    bool skillVariantModeEnabled = false,
    SkillVariantTarget? skillVariantTarget = null,
    string? roleInstructions = null,
    IContextPageManager? contextPageManager = null,
    DreamStore? dreamStore = null,
    SubAgentWaitAgentTimeoutOptions? subAgentWaitAgentTimeoutOptions = null,
    string? threadId = null,
    IReadOnlyList<IThreadSystemPromptContextProvider>? threadSystemPromptContextProviders = null,
    string? originChannel = null,
    IReadOnlyList<string>? workspaceRoots = null) : AIContextProvider
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
        contextPageManager,
        dreamStore,
        subAgentWaitAgentTimeoutOptions,
        threadSystemPromptContextProviders,
        originChannel,
        workspaceRoots);

    public ValueTask<string> ProvideInstructionsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var sessionKey = TracingChatClient.CurrentSessionKey ?? TracingChatClient.GetActiveSessionKey();
        var systemPrompt = _promptBuilder.BuildSystemPrompt(threadId ?? sessionKey);
        if (!string.IsNullOrWhiteSpace(sessionKey))
            traceCollector?.RecordSessionMetadata(sessionKey, systemPrompt, toolNamesProvider?.Invoke());

        return ValueTask.FromResult(systemPrompt);
    }

    protected override async ValueTask<AIContext> ProvideAIContextAsync(
        InvokingContext context,
        CancellationToken cancellationToken = default) =>
        new()
        {
            Instructions = await ProvideInstructionsAsync(cancellationToken).ConfigureAwait(false)
        };
}
