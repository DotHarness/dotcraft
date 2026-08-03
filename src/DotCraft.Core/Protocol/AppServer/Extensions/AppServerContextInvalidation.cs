using DotCraft.Context;

namespace DotCraft.AppServer;

/// <summary>
/// Shared context-page invalidation used by the dispatcher and extracted domain handlers so the
/// same wildcard keys are marked dirty regardless of which handler triggers the change (e.g. both
/// skills/* and plugin/* invalidate the skills page; both dreams/apply and memory/reset invalidate
/// the long-term memory page).
/// </summary>
internal static class AppServerContextInvalidation
{
    public static void MarkSkills(IContextPageManager? contextPageManager) =>
        contextPageManager?.MarkDirty(ContextPageKeys.SkillsWildcard());

    public static void MarkMemory(IContextPageManager? contextPageManager) =>
        contextPageManager?.MarkDirty(ContextPageKeys.MemoryLongTerm("*"));
}
