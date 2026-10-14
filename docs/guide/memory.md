# Memory & Dreams

DotCraft remembers what later conversations in the same workspace will need: who you are, how you want it to work, project context the repository doesn't record, and where to find outside information. Memory is plain Markdown in your workspace, so you can read and edit it at any time. Turn on Dreams and DotCraft also tidies that memory in the background.

![How DotCraft saves and reuses memory](/memory-lifecycle-topology.svg)

## What DotCraft remembers

Tell DotCraft what to remember, correct, or forget, for example: “Remember that I prefer short answers.” It updates its memory in the same reply.

Without being asked, DotCraft also saves knowledge that later conversations will need. Each memory has one of four types:

| Type | What it holds |
|---|---|
| `user` | Your role, expertise, and preferences |
| `feedback` | How you want DotCraft to work, both corrections and approaches you confirmed |
| `project` | Goals, decisions, and constraints the repository doesn't record, with exact dates |
| `reference` | Where to find information outside the workspace, such as a dashboard or an issue tracker |

DotCraft doesn't save on its own what the repository or `AGENTS.md` already records, such as code structure, past fixes, or history, or anything that only matters to the current task. When it isn't sure something will matter later, it doesn't save it. It updates an existing memory instead of adding a duplicate, and deletes memories that turned out to be wrong.

## Where memory lives

Memory lives in your workspace's `.craft/memory/` folder, one Markdown file per memory. Each file starts with its name, a one-line description, and its type:

```markdown
---
name: prefers-short-answers
description: User wants brief answers without a closing recap
metadata:
  type: feedback
---

Keep answers short and skip the closing summary.

**Why:** The user reads replies between meetings.
**How to apply:** Lead with the answer and offer detail only when asked.
```

`MEMORY.md` in the same folder is the index, with one line per memory, such as `- [Prefers short answers](prefers-short-answers.md) — brief replies, no recap`. Add, change, or delete memory files by hand whenever you like, and keep each file's line in `MEMORY.md` up to date.

## How conversations use memory

Every conversation starts with the `MEMORY.md` index, and DotCraft opens a memory file when it's relevant to the task. A memory reflects the moment it was saved, so your current instructions and what DotCraft sees in the workspace come first. Before acting on a file, function, or setting that a memory names, DotCraft checks that it still exists. When it reads an older memory, it's reminded that the memory may be out of date. Tell it to ignore memory and it won't use, cite, or mention it.

Changes to memory reach conversations that are already open, whether you made them, another conversation saved them, or Dreams tidied them.

In Desktop, memory shows up in the conversation itself. "Recalled N memories" lists the memory files DotCraft read, and "Memory updated" lists the files it saved, with **Manage** to open the memory settings.

## Save missed memories

DotCraft usually saves memory while it replies. To catch what it missed, turn on **Settings → Personalization → Save missed memories**: every few replies, DotCraft checks the conversation in the background for anything worth remembering that was not saved. These background saves don't appear in the conversation. The switch applies to conversations you start afterwards.

## Dreams

![Reviewing a Dreams run](https://github.com/DotHarness/resources/raw/master/dotcraft/docs/dreams.webp)

Over time, memory collects duplicates and facts that no longer hold. Dreams tidies it in the background: it merges duplicates, fixes facts that no longer match the workspace, and keeps `MEMORY.md` short. It also checks memory against `AGENTS.md`, and when the two disagree, `AGENTS.md` wins. If a memory is clearly newer than `AGENTS.md`, Dreams keeps it and flags it in the run's summary for you to check. Dreams never edits `AGENTS.md`.

Turn it on in Desktop under **Settings → Personalization → Dreams**. Dreams then runs on its own while DotCraft is running, at most once a day by default, and only after several conversations have used memory since the last run. Under **Manage**, change the frequency or choose **Run now** to tidy memory right away.

Each run proposes a change to memory, and memory stays as it is until you apply it. In the run list under **Manage**, choose **Review** to open the run in Dashboard, compare each changed file before and after, then apply or discard it. If you change your mind after applying, Dashboard can restore memory to how it was before the run, as long as memory hasn't changed since. If memory changed while a run was working, its result can't be applied; run Dreams again.

To let each run take effect without review, turn on **Auto-update Dreams** under **Manage**. Runs already waiting for review still wait for you.

## Give an agent its own memory

[Agent Profiles](./agent-profiles) share memory with your other conversations in the workspace by default. To let an agent build up memory of its own, set **Memory** to **Agent only** when you edit it (`memory: own` in the profile file). Its memories stay separate from your other conversations, and Dreams tidies them separately.

An agent's own memory follows its name, so renaming the agent starts it with empty memory. Removing or exporting the agent leaves its memory untouched.

## Turn off or delete memory

To stop using memory, turn off **Settings → Personalization → Enable memories** in Desktop. New conversations then neither use nor save memory, and conversations already open keep the setting they started with. Save missed memories, Dreams, and welcome suggestions pause too, and resume with their settings when you turn memory back on.

To forget one thing, ask DotCraft to forget it, or delete its file and its line in `MEMORY.md`. To clear the workspace's memory, open **Settings → Personalization → Delete memories**. This deletes every memory in the workspace, including the memories that agents and automations keep for themselves, and every Dreams run. Conversation history, settings, skills, and Agent Profiles stay.

## Related docs

- [Skills & Self-Learning](./skills) — memory keeps facts, skills keep methods: a workflow that worked gets reused next time
- [Observability](../self-hosting/observability) — review Dreams runs and session traces in Dashboard
