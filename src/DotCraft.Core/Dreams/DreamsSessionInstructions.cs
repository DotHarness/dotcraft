using System.Globalization;
using System.Text;
using DotCraft.Context;
using DotCraft.Memory;

namespace DotCraft.Dreams;

internal static class DreamsSessionInstructions
{
    public static string SystemPrompt { get; } = EmbeddedPrompt.Read("dreams-system-prompt");

    public static string BuildTurnPrompt(DreamsRunInput input)
    {
        var sb = new StringBuilder();
        sb.Append(string.Create(CultureInfo.InvariantCulture, $$"""
# Dream: memory consolidation

Consolidate the candidate store at `{{input.CandidateDirectory}}`. It starts as a copy of the store, and it replaces the store when the run is applied. A read-only copy of the store as it was is at `{{input.BaselineDirectory}}`.

## Phase 1 — Orient

The run manifest at the end lists the store's memory files and its index. Skim the memory files that matter, so you improve them rather than add duplicates.

## Phase 2 — Gather recent signal

1. Memories that drifted: facts that contradict what the workspace shows now.
2. The manifest's recent threads: search their transcripts with SearchTranscripts for narrow terms you already suspect matter, then use ReadTranscript for the excerpts you need. Never read transcripts end to end.

## Phase 3 — Consolidate

Write or update memory files at the top level of the candidate store, following the memory format below.

- Merge near-duplicates into one file.
- Convert relative dates to absolute dates.
- Delete facts that current evidence contradicts. DeleteMemoryFile removes a whole memory.

## Phase 4 — Prune and index

Keep MEMORY.md under {{MemoryIndex.MaxLines}} lines and {{MemoryIndex.MaxBytes:N0}} bytes. It is an index with one line per memory file, `- [Title](name.md) — one-line hook`, under about 150 characters. Never write memory content into it.

- Remove pointers to stale, wrong, or superseded memories.
- Shorten lines over about 200 characters and move the detail into the memory file.
- Add pointers for new memories.
- Resolve memories that contradict each other by fixing the wrong one.

## Reconcile with AGENTS.md

The AGENTS.md instructions in your context are the maintained source. For each feedback or project memory that contradicts AGENTS.md on the same topic:

- When the two describe different procedures for the same task, delete the memory, or rewrite it to agree when its rationale is still useful.
- When the memory is clearly newer and explicitly corrects AGENTS.md, keep it, add "contradicts AGENTS.md — verify which is current", and list it in your summary. Never edit AGENTS.md.
- A memory that adds detail AGENTS.md does not cover, or narrows one of its rules with a stated reason, is not a conflict. Leave it.

A memory saying the user corrected you does not by itself show it is newer than AGENTS.md.

## Memory format

{{MemoryInstructions.FileFormat}}

## Tools

You can read the workspace, the baseline, and AGENTS.md. WriteFile, EditFile, and DeleteMemoryFile change only top-level `<name>.md` files in the candidate store; every other write is refused.

## Run manifest

{{input.Manifest.Trim()}}

"""));
        if (!string.IsNullOrWhiteSpace(input.Instructions))
            sb.Append("\n## Additional instructions\n\n").Append(input.Instructions.Trim()).Append('\n');
        sb.Append("\nFinish with a brief summary of what you consolidated, updated, or pruned. If nothing needed to change, say so.");
        return sb.ToString();
    }
}
