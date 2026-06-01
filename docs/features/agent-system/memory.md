# Memory & Dreams

DotCraft does not pretend to have memory by stuffing more tokens into a context window. Stable information from successful conversations is written into two plain Markdown files inside the workspace, so the agent can read it across sessions and entry points — and so you can read, edit, and delete it any time.

## Three Layers of Memory

| Layer | Storage | Written by | Purpose |
|---|---|---|---|
| **Session history** | `.craft/threads/` | Engine, automatically | Full Thread / Turn / Item timeline of each session |
| **Long-term memory & history** | `.craft/memory/MEMORY.md`, `HISTORY.md` | Agent maintains after successful turns | Project context, user preferences, recent decisions, recurring issues |
| **Dreams** | `.craft/dreams/` | Background consolidation loop | Low-authority passive context (focus topics, open questions, low-signal noise to avoid) |

Session history is the audit baseline. Long-term memory is the agent-maintained, high-authority notebook. Dreams is the low-authority background scratchpad. The three roles do not overlap.

## MEMORY.md / HISTORY.md — Agent Maintains, You Review

When long-term consolidation is enabled, every few successful conversation rounds DotCraft asks the agent to patch stable information back into `.craft/memory/MEMORY.md` and append the run to `HISTORY.md`. The exact schedule and consolidation model are configurable in the [Configuration Reference](../../developing/configuration#workspace-memory-and-skills).

Writes are **patch-style**: the agent does not blindly overwrite the entire `MEMORY.md`. It performs precise inserts or edits relative to your or its own existing notes. So manual edits to these files are safe — the agent reads your edited version next time.

> [!TIP]
> Desktop's **Settings → Personalization → Reset memory** clears `MEMORY.md`, `HISTORY.md`, `.craft/dreams/`, and derived caches in one click. It does not delete sessions, config, skills, or automation tasks.

## Dreams — Background Memory Consolidation

Dreams is DotCraft's background thinking loop. Whenever AppServer is running (even when you are not actively chatting), it periodically scans recent workspace activity and generates a **low-authority** passive memory store — think of it as a draft notebook that only enters future sessions after you review it.

![Dreams review flow](https://github.com/DotHarness/resources/raw/master/dotcraft/whats-new/dreams.gif)

![DotCraft memory lifecycle topology](/memory-lifecycle-topology.svg)

| State | Meaning |
|---|---|
| **pending** | Auto-generated, not yet in context, awaiting review |
| **active** | Approved by user, joins future sessions as low-authority context |
| **discarded / archived** | Historical, no longer in context |

Desktop's **Settings → Personalization → Dreams** offers:

- **Run now** — Trigger a Dream consolidation immediately
- **Auto-update Dreams** — Off: new dreams stay pending. On: future successful runs auto-apply as active
- **Manage Dreams** — List of dreams, each opens Dashboard for diff / trace / apply / discard / cancel / archive

Dreams does not replace `MEMORY.md`; it complements it. Dreams is the right place for "recent focus" and "low-signal noise to avoid" — items too noisy to commit as authoritative memory but too important to lose with the session.

## How DotCraft Memory Compares

| Approach | Black box | Human-readable | Human-editable | Trigger |
|---|---|---|---|---|
| Model context window | Yes | No | No | Every request |
| Vector / RAG | Partly | Usually no | Usually no | At retrieval |
| **DotCraft MEMORY.md** | No | Yes (Markdown) | Yes | Every N rounds |
| **DotCraft Dreams** | No | Yes (Markdown) | Yes | Background loop |

Putting everything in workspace-readable Markdown is DotCraft's core trust choice: **you cannot trust a memory you cannot read**.

## When to Use Which

| Scenario | Recommendation |
|---|---|
| One-shot reminder "use Python 3.11 this session" | Don't memorize; keep it in the conversation |
| "This project uses pnpm, never npm" | `MEMORY.md` (let the agent patch it) |
| Periodic "what have we been working on" self-check | Enable Dreams |
| Wipe everything and start over | Desktop "Reset memory" |
| Share project conventions with the team | Commit `MEMORY.md` to the repository |

## Privacy & Local-First

`.craft/memory/` and `.craft/dreams/` live entirely inside the workspace directory. DotCraft does not upload them to any external service unless you place the workspace under a synced folder. For stronger isolation:

- Add `.craft/memory/` and `.craft/dreams/` to `.gitignore` to keep them on-device only.
- Use [security configuration](../self-hosted/security) to forbid the agent from writing outside the workspace.

## Related

- [Project Workspace](../project-first)
- [Skills & Self-Learning](./skills)
- [Observability](../self-hosted/observability)
- [Configuration Reference](../../developing/configuration) for `Memory.*` and `Compaction.*` fields
