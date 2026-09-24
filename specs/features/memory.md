# DotCraft Memory Design Specification

| Field | Value |
|-------|-------|
| **Version** | 0.3.0 |
| **Status** | Living |
| **Date** | 2026-09-24 |
| **Parent Specs** | [Session Core](../architecture/session-core.md), [AppServer Protocol](../protocols/appserver-protocol.md) |
| **Related Specs** | [Dreams](dreams.md), [Prompt Composition](../architecture/prompt-composition.md), [Desktop Client](../clients/desktop-client.md) |

Purpose: Define DotCraft's workspace memory: the agent-maintained `MEMORY.md` store, which store a Thread uses, how memory enters the prompt, the workspace switch that enables it, and the features that depend on it.

## 1. Scope

In scope:

- Which store a Thread reads and writes.
- What the agent saves in `MEMORY.md` and when.
- How memory enters the prompt.
- The workspace memory switch and the features that depend on it.
- Deleting memory.

Out of scope:

- Short-term context compaction and token-pressure recovery.
- Dreams generation and review, defined in [Dreams](dreams.md).
- Per-Thread memory controls.
- Vector retrieval, semantic indexes, or cross-workspace memory sharing.

DotCraft does not extract memory from conversation history in the background. The agent is the only writer of `MEMORY.md`. Dreams maintains its own inferred store and never writes `MEMORY.md`.

## 2. Core Concepts

| Concept | Definition |
|---------|------------|
| **Memory** | Durable lessons the user taught the agent, stored in `MEMORY.md` and carried into later Threads. |
| **Memory scope** | The owner of a store. A Thread that names one reads and writes `<state>/scopes/<scope>/memory/`; a Thread that names none reads and writes the workspace's `<state>/memory/`. |
| **Memory switch** | The workspace setting `Memory.Enabled`. It decides whether new Threads use and maintain memory. |

## 3. Memory Scope

Durable knowledge belongs to whoever accumulates it. A Thread names its scope in its configuration;
the scope is a single safe path segment derived from configuration, never from a Thread identity, so
the memory prompt section stays independent of which Thread is running.

A store is split off when the execution line is an independent, recurring principal. An automation is
such an owner and names itself. An ordinary Thread, including one running an Agent Profile, is not:
several Profiles used by one person share the workspace store, because the knowledge is that person's.

A Thread's scope is fixed for its lifetime, so memory never changes scope mid-Thread and the stable
prompt prefix is not rewritten. A SubAgent resolves its root's scope rather than opening one of its own.
A scope redirects memory and nothing else: Dreams, Skills, bootstrap documents, the working directory
and the path blacklist all stay where they are.

## 4. Agent-Maintained Memory

When memory is enabled, the memory prompt section supplies the resolved store path and the save rules, even when the store is empty. The agent maintains `MEMORY.md` with its ordinary file tools. Basic operations require no built-in Memory Skill.

Save rules:

- Explicit requests to remember, correct, or forget information are applied promptly. An explicit request to save a test record is honored.
- Otherwise the agent saves a lesson only when the user taught or corrected it and it will apply to future Threads, such as a standing preference or an approach the user steered the agent toward or away from.
- The agent does not save facts it worked out itself, task status, or information the repository already records.
- When the agent is unsure whether a lesson lasts beyond the current task, it does not save it.
- A lesson is saved in the same reply that responds to the user's message, before the task continues.

Memory contents are background context, subordinate to current instructions and verified evidence. At most the first 10,000 characters of `MEMORY.md` enter the stable prompt page, with a truncation notice when necessary; the file itself is never truncated. Agents read the actual file before editing and prefer targeted edits; a prompt excerpt must never be used as a complete replacement.

## 5. Memory Switch

| Setting | Default | Meaning |
|---------|---------|---------|
| `Memory.Enabled` | `true` | New Threads use and maintain memory. |

When memory is disabled, a Thread neither uses nor maintains memory. The whole memory prompt section is omitted: no store path, no save rules, no `MEMORY.md` content, and no Dream Memory.

A Thread captures the switch at creation and keeps it for its lifetime, like its scope. Changing the setting affects new Threads only; running and resumed Threads keep the value they captured, and replacing a Thread's configuration preserves it. A SubAgent or forked Thread inherits its source's value. A Thread persisted before the switch existed is treated as enabled.

Features that depend on memory stop when it is disabled and resume with their stored settings when it is enabled again:

- Dreams does not start scheduled or manual runs. See [Dreams](dreams.md).
- Welcome suggestions are not generated from memory, and clients fall back to their default suggestions.

Clients show dependent settings as disabled while memory is off, with their stored values unchanged.

## 6. Deleting Memory

AppServer `memory/reset` deletes the contents of the current workspace's memory directory, including files left by earlier versions, and the Dreams-derived memory defined in [Dreams](dreams.md). It does not delete sessions, configuration, skills, plugins, plans, or automation tasks, and it does not change `Memory.Enabled`.

## 7. Welcome Suggestions

Personalized welcome suggestions read `MEMORY.md` only. They are generated in the background after a successful Turn when the memory evidence has changed since the cached result, and are skipped when memory is disabled or `MEMORY.md` is empty.
