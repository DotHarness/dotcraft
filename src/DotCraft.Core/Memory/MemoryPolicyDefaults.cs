namespace DotCraft.Memory;

public static class MemoryPolicyDefaults
{
    public const int ExtractionInterval = 7;

    public const string SaveGuidance =
"""
Save knowledge that will apply to future conversations in the same reply that responds to the user, before continuing the task. Do not save on your own what the repository or AGENTS.md already records (code structure, past fixes, history) or what only matters to the current task. Before saving, check whether an existing memory covers it; update that file instead of adding a duplicate, and delete memories that turn out to be wrong. If you are unsure that something outlasts the current task, do not save it.

For feedback and project memories, follow the fact with **Why:** and **How to apply:** lines. Link related memories with [[name]].
""";

    public const string ExtractionGuidance =
"""
Save any knowledge that your memory instructions say to save but that was not saved while responding. Update an existing memory rather than adding a duplicate.
""";

    public const string ConsolidationGuidance =
"""
- Merge near-duplicates into one file.
- Convert relative dates to absolute dates.
- Delete facts that current evidence contradicts.

Reconcile with AGENTS.md. The AGENTS.md instructions in your context are the maintained source. For each feedback or project memory that contradicts AGENTS.md on the same topic:

- When the two describe different procedures for the same task, delete the memory, or rewrite it to agree when its rationale is still useful.
- When the memory is clearly newer and explicitly corrects AGENTS.md, keep it, add "contradicts AGENTS.md — verify which is current", and list it in your summary.
- A memory that adds detail AGENTS.md does not cover, or narrows one of its rules with a stated reason, is not a conflict. Leave it.

A memory saying the user corrected you does not by itself show it is newer than AGENTS.md.
""";

    public static List<MemoryTypeDefinition> CreateTypes() =>
    [
        new() { Name = "user", Description = "who the user is (role, expertise, preferences).", Personal = true },
        new() { Name = "feedback", Description = "how the user wants you to work, both corrections and approaches they confirmed; include the why." },
        new() { Name = "project", Description = "ongoing goals, decisions, or constraints the repository does not record; convert relative dates to absolute dates." },
        new() { Name = "reference", Description = "where to find information outside the workspace (URLs, dashboards, trackers)." }
    ];
}
