using DotCraft.Context;
using DotCraft.Context.Compaction;
using DotCraft.Context.WorldState;
using DotCraft.Contributions;
using DotCraft.Mcp;
using DotCraft.Sessions.Wire;
using DotCraft.Tools;
using Microsoft.Extensions.AI;

namespace DotCraft.Sessions;

internal sealed record ContextUsageComposition(
    int InstructionsTokens,
    IReadOnlyList<PromptSectionTokens>? Sections,
    string? CraftPath,
    int AgentInstructionsTokens,
    int MemoryIndexTokens,
    IReadOnlyList<AITool> Tools,
    EffectiveToolSnapshot? ToolSnapshot)
{
    public static ContextUsageComposition Capture(
        PromptRequestSnapshot request,
        IReadOnlyList<PromptSectionTokens>? sections,
        string? craftPath,
        EffectiveToolSnapshot? toolSnapshot) =>
        new(
            request.BaseInstructionsTokenEstimate ?? MessageTokenEstimator.RoughTokenCount(request.BaseInstructions),
            sections,
            craftPath,
            request.Messages.Where(AgentInstructionsHistory.IsInstructions).Sum(MessageTokenEstimator.EstimateMessage),
            request.Messages.Where(IsMemoryIndex).Sum(MessageTokenEstimator.EstimateMessage),
            request.Tools,
            toolSnapshot);

    public ContextUsageBreakdown ToBreakdown(
        ContextUsageSnapshot usage,
        IReadOnlyList<string> agentInstructionFiles,
        IReadOnlyList<string> memoryIndexFiles)
    {
        var skills = SectionTokens(SystemPromptSectionNames.ActiveSkills, SystemPromptSectionNames.SkillsSummary);
        var subagents = SectionTokens(SystemPromptSectionNames.SubAgentProfiles);
        var bootstrap = SectionTokens(SystemPromptSectionNames.BootstrapFiles);
        var systemPrompt = Math.Max(0, InstructionsTokens - skills - subagents - bootstrap);
        IReadOnlyList<string> bootstrapFiles = bootstrap > 0 && CraftPath is { } craftPath
            ? [.. WorkspaceContextPromptSections.BootstrapFiles.Select(name => Path.Combine(craftPath, name))]
            : [];

        var loaded = new ToolTally();
        var visibleNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var tool in Tools)
        {
            visibleNames.Add(tool.Name);
            loaded.Add(
                ResolveMcpServer(ToolSnapshot, tool.Name),
                PromptRequestSnapshot.EstimateToolTokens(tool.Name, tool.Description, (tool as AIFunctionDeclaration)?.JsonSchema));
        }

        var deferred = new ToolTally();
        if (ToolSnapshot is { } toolSnapshot
            && toolSnapshot.Registrations.Values.Any(EffectiveToolSnapshot.IsDeferredToolSearch))
        {
            foreach (var definition in toolSnapshot.DeferredDefinitions.Values.SelectMany(static group => group))
            {
                if (!toolSnapshot.ProviderFlatNames.TryGetValue(definition.Name, out var providerName)
                    || visibleNames.Contains(providerName))
                {
                    continue;
                }

                deferred.Add(
                    ResolveMcpServer(toolSnapshot, providerName),
                    PromptRequestSnapshot.EstimateToolTokens(providerName, definition.Description, definition.InputSchema));
            }
        }

        var categories = new List<ContextUsageCategory>();
        Add(categories, ContextUsageCategoryIds.SystemPrompt, systemPrompt);
        Add(
            categories,
            ContextUsageCategoryIds.MemoryFiles,
            bootstrap + AgentInstructionsTokens + MemoryIndexTokens,
            items: FileItems([
                .. AgentInstructionsTokens > 0 ? agentInstructionFiles : [],
                .. bootstrapFiles,
                .. MemoryIndexTokens > 0 ? memoryIndexFiles : []
            ]));
        Add(categories, ContextUsageCategoryIds.Skills, skills);
        Add(categories, ContextUsageCategoryIds.Subagents, subagents);
        Add(categories, ContextUsageCategoryIds.BuiltInTools, loaded.BuiltIn);
        Add(categories, ContextUsageCategoryIds.McpTools, loaded.McpTotal, items: loaded.McpItems());
        var fixedTokens = categories.Sum(static category => category.Tokens);
        if (fixedTokens > usage.Tokens)
            FitInto(categories, fixedTokens, Math.Max(0, usage.Tokens));
        else
            Add(categories, ContextUsageCategoryIds.Messages, usage.Tokens - fixedTokens);
        Add(categories, ContextUsageCategoryIds.BuiltInTools, deferred.BuiltIn, onDemand: true);
        Add(categories, ContextUsageCategoryIds.McpTools, deferred.McpTotal, onDemand: true, items: deferred.McpItems());

        return new ContextUsageBreakdown { ContextUsage = usage, Categories = categories };
    }

    private static bool IsMemoryIndex(ChatMessage message) =>
        WorldStateComposer.SectionIdFromItemKind(ThreadContextItems.GetKind(message)) == MemoryIndexSection.SectionId;

    private static IReadOnlyList<ContextUsageItem>? FileItems(IEnumerable<string> paths)
    {
        var items = new List<ContextUsageItem>();
        foreach (var path in paths)
        {
            try
            {
                if (File.Exists(path) && MessageTokenEstimator.RoughTokenCount(File.ReadAllText(path)) is > 0 and var tokens)
                    items.Add(new ContextUsageItem(path, tokens));
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        return items.Count > 0 ? items : null;
    }

    private static void FitInto(List<ContextUsageCategory> categories, long measured, long total)
    {
        long Scale(long tokens) => tokens * total / measured;

        for (var i = 0; i < categories.Count; i++)
        {
            var category = categories[i];
            categories[i] = category with
            {
                Tokens = Scale(category.Tokens),
                Items = category.Items?.Select(item => item with { Tokens = Scale(item.Tokens) }).ToArray()
            };
        }

        var remainder = total - categories.Sum(static category => category.Tokens);
        var largest = categories.IndexOf(categories.MaxBy(static category => category.Tokens)!);
        categories[largest] = categories[largest] with { Tokens = categories[largest].Tokens + remainder };
        categories.RemoveAll(static category => category.Tokens <= 0);
    }

    private int SectionTokens(params string[] names) =>
        Sections?.Where(section => names.Contains(section.Name, StringComparer.Ordinal)).Sum(static section => section.Tokens) ?? 0;

    private static void Add(
        List<ContextUsageCategory> categories,
        string id,
        long tokens,
        bool onDemand = false,
        IReadOnlyList<ContextUsageItem>? items = null)
    {
        if (tokens > 0)
            categories.Add(new ContextUsageCategory { Id = id, Tokens = tokens, OnDemand = onDemand, Items = items });
    }

    private static string? ResolveMcpServer(EffectiveToolSnapshot? toolSnapshot, string providerName) =>
        toolSnapshot is not null
        && toolSnapshot.TryResolveProviderFlatName(providerName, out var toolName)
        && toolSnapshot.Registrations.TryGetValue(toolName, out var registration)
        && registration.Definition.Id.Kind == ToolSourceKind.Mcp
            ? McpToolRouting.ServerName(registration)
            : null;

    private sealed class ToolTally
    {
        private readonly Dictionary<string, long> _mcpServers = new(StringComparer.Ordinal);

        public long BuiltIn { get; private set; }

        public long McpTotal => _mcpServers.Values.Sum();

        public void Add(string? mcpServer, int tokens)
        {
            if (mcpServer is null)
                BuiltIn += tokens;
            else
                _mcpServers[mcpServer] = _mcpServers.GetValueOrDefault(mcpServer) + tokens;
        }

        public IReadOnlyList<ContextUsageItem> McpItems() =>
            _mcpServers
                .OrderByDescending(static pair => pair.Value)
                .ThenBy(static pair => pair.Key, StringComparer.Ordinal)
                .Select(static pair => new ContextUsageItem(pair.Key, pair.Value))
                .ToArray();
    }
}
