# Memory & Dreams

DotCraft can carry useful project context from one conversation to the next. The memory it saves for later is stored as plain Markdown in your workspace, so you can read, edit, or delete it at any time.

![DotCraft memory lifecycle topology](/memory-lifecycle-topology.svg)

## Three ways DotCraft remembers

| Type | What it helps with |
|---|---|
| **Conversation history** | Reopen or continue work from an earlier conversation |
| **Saved memory** | Reuse stable project details, preferences, decisions, and recurring issues |
| **Dreams** | Keep tentative notes about recent focus, open questions, and low-signal context |

Conversation history preserves the work in each conversation. Saved memory keeps information that should remain useful beyond one conversation. Dreams adds tentative background notes without replacing either one.

## MEMORY.md and HISTORY.md

When saved memory is enabled, DotCraft periodically updates `.craft/memory/MEMORY.md` with useful long-term information and adds a short record to `HISTORY.md`. The schedule and model are configurable in the [Configuration Reference](../../developing/configuration#workspace-memory-and-skills).

You can review and edit both files. DotCraft reads the current files before the next memory update, so your changes become part of the memory it works from.

> [!TIP]
> Desktop's **Settings → Personalization → Reset memory** clears `MEMORY.md`, `HISTORY.md`, `.craft/dreams/`, and derived caches in one click. It does not delete sessions, config, skills, or automation tasks.

## Dreams

Dreams reviews recent workspace activity in the background, even when you are not actively chatting. It prepares tentative notes that can help future conversations without treating them as instructions or established facts.

Dreams is off by default. Turn on **Enable Dreams** in Desktop under **Settings → Personalization → Dreams** to opt in. After it is enabled, a successful Dreams run waits for your review before DotCraft uses it. If you turn on **Auto-update Dreams**, future successful runs become available automatically and skip manual review. Existing pending runs are not applied automatically.

![Dreams review flow](https://github.com/DotHarness/resources/raw/master/dotcraft/whats-new/dreams.gif)

| State | Meaning |
|---|---|
| **pending** | Waiting for review and not used in conversations yet |
| **applied** | Available to future conversations, either after review or through auto-update |
| **discarded / archived** | Kept out of future conversations |

Desktop's **Settings → Personalization → Dreams** offers:

- **Enable Dreams** — Opt in to background Dreams for this workspace
- **Run now** — Start a Dreams update immediately
- **Auto-update Dreams** — Let future successful runs skip manual review and become available automatically
- **Manage Dreams** — Review recent runs and apply, discard, cancel, or archive them

Dreams does not replace saved memory. Use `MEMORY.md` for facts and preferences you want DotCraft to rely on. Treat Dreams as supporting notes that may still need correction or removal.

## Related docs

- [Skills & Self-Learning](./skills)
- [Observability](../self-hosted/observability)
- [Configuration Reference](../../developing/configuration) for `Memory.*` and `Compaction.*` fields
- [Session persistence](../../developing/architecture/session-persistence)
