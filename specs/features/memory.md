# DotCraft Memory Design Specification

| Field | Value |
|-------|-------|
| **Version** | 0.9.1 |
| **Status** | Living |
| **Date** | 2026-10-19 |
| **Parent Specs** | [Session Core](../architecture/session-core.md) |
| **Related Specs** | [Prompt Composition](../architecture/prompt-composition.md), [World State](../architecture/world-state.md) |

Purpose: Define DotCraft's memory: the agent-maintained store of memory files and their index, which store a Thread uses, the stores its creator attaches beside it and how writes to them are admitted, how memory enters the prompt, how memory is saved while responding and after a Turn, how clients present it, the switches that govern it, and how it is deleted.

## 1. Scope

In scope:

- Store layout, the memory file format, and the index.
- Which store a Thread reads and writes.
- Stores attached to a Thread beside its own, their access, and the gate that admits writes to them.
- How memory enters the prompt.
- Saving memory while responding and in the background after a Turn.
- How clients present memory reads and writes.
- The memory switches and the features that depend on them.
- Deleting memory.

Out of scope:

- Consolidating a store, owned by Dreams.
- Short-term context compaction and token-pressure recovery.
- Per-Thread memory controls.
- Relevance-based retrieval, vector or semantic indexes, and cross-workspace or user-global memory. Standing user-level guidance belongs in the user's `AGENTS.md`.
- Where an attached store's content comes from, who may change it, and how it stays consistent across hosts. These belong to the host that attaches the store.
- Attaching stores over AppServer or from a client. Only an in-process host attaches stores.

## 2. Core Concepts

| Concept | Definition |
|---------|------------|
| **Memory** | Durable knowledge that applies to future Threads and cannot be derived from the workspace: who the user is, how they want the agent to work, project context the repository does not record, and where outside information lives. |
| **Memory store** | One directory holding memory files and their index. |
| **Memory file** | One Markdown file holding one memory. |
| **Memory index** | `MEMORY.md` in a store: one pointer line per memory file. |
| **Memory scope** | The owner of a store other than the workspace. |
| **Own store** | The one store a Thread resolves from its scope (§4). It holds what the Thread's principal accumulates. |
| **Attached store** | A store that whoever creates a Thread attaches beside its own store, owned by someone other than the Thread's principal and shared with others. |
| **Access** | Whether a Thread may only read an attached store (`read`) or also write it (`readWrite`). |
| **Memory write gate** | A host-supplied check that admits or rejects each write to a `readWrite` attached store before it reaches disk. |
| **Memory extraction** | A background pass after a Turn that saves knowledge the agent did not save while responding. |

## 3. Store Layout And Format

A Thread with no scope uses the workspace store `<state>/memory/`. A Thread with a scope uses `<state>/scopes/<scope>/memory/`. An attached store lives at `<state>/memory-stores/<id>/`. `<state>` is the workspace state directory: `.craft`, or the data directory an embedding host names in its place.

```text
<store>/
  MEMORY.md
  <name>.md
```

The runtime creates a store directory before the agent needs it, so the agent writes to it directly. Workspace initialization writes no memory files. Every store, attached ones included, uses the file format and index below.

### 3.1 Memory files

A memory file is named `<name>.md` and starts with frontmatter:

```markdown
---
name: <kebab-case slug matching the file name>
description: <one line used to judge relevance later>
metadata:
  type: user | feedback | project | reference
---

<the memory>
```

| Type | Holds |
|------|-------|
| `user` | Who the user is: role, expertise, and preferences. |
| `feedback` | How the user wants the agent to work, both corrections and approaches the user confirmed. |
| `project` | Ongoing goals, decisions, or constraints the repository does not record, with absolute dates. |
| `reference` | Where to find information outside the workspace. |

A `feedback` or `project` body leads with the rule or fact, followed by a **Why:** line and a **How to apply:** line. A body may link related memories as `[[name]]`. A Markdown file without frontmatter or with an unknown type is still a memory file and is listed without a type.

### 3.2 Index

`MEMORY.md` is an index, not a memory. It has no frontmatter and holds one line per memory file, under about 150 characters:

```markdown
- [Title](name.md) — one-line hook
```

The agent maintains the index; the runtime never generates or rewrites it. The runtime loads at most the first 200 lines and 25,000 bytes of the index. Beyond either limit it loads the leading part, cut at a line boundary, followed by a notice naming the limit that was exceeded. When the index holds lines that are not pointers, or pointer lines longer than 200 characters, the loaded index ends with a notice asking the agent to move that content into memory files.

## 4. Memory Scope

Durable knowledge belongs to whoever accumulates it. A Thread names its scope in its configuration. The scope is a single safe path segment derived from configuration, never from a Thread identity, so the memory instructions stay independent of which Thread is running.

A store is split off for an independent, recurring principal, or when an Agent Profile asks for its own:

- An automation names itself as the scope of its runs.
- An Agent Profile declares `memory: shared` or `memory: own`, and `shared` is the default. A shared Profile uses the workspace store, because several Profiles used by one person accumulate that person's knowledge. An own Profile uses the scope `agent-<hex>`, where `<hex>` is the lowercase SHA-256 of the Profile's canonical name.
- A scope set by whoever creates the Thread takes precedence over the Profile's.

An own store is keyed by the Profile's canonical name, which is its identity, so renaming a Profile starts a new store. Memory is independent of Profile management: removing, renaming, exporting, or importing a Profile never reads, moves, or deletes a store.

A Thread's scope is fixed for its lifetime, so memory never changes scope mid-Thread. A SubAgent resolves its root's scope rather than opening one of its own. A scope redirects memory and nothing else: Dreams scheduling, Skills, bootstrap documents, the working directory, and the path blacklist all stay where they are.

### 4.1 Attached stores

Some knowledge belongs to a group rather than to one principal: what several agents serving one organization learn about its business, or a reference collection maintained elsewhere. Putting it in each principal's own store duplicates it and lets the copies drift apart. Putting everything in one shared store mixes it with experience that suits only one principal. So a Thread keeps its own store and may also use stores attached beside it.

Whoever creates a Thread attaches stores in its configuration. Each attachment names:

| Field | Meaning |
|-------|---------|
| `id` | A single safe path segment, unique among the Thread's attachments. It names the directory, so two Threads that attach the same `id` in one workspace use the same store. |
| `name` | A short title shown to the model and in presentation. |
| `description` | One or two sentences saying what the store holds and who else uses it. The model relies on it to decide what belongs there. |
| `access` | `read` or `readWrite`. |

Rules:

- A Thread has at most eight attached stores. Its own store is never one of them, and an attachment never replaces it.
- An attachment that fails validation fails Thread creation or configuration replacement with a reason. Validation covers an unsafe or duplicate `id`, a missing `name` or `description`, an unknown access, or too many attachments.
- Unlike the scope, attachments may change: replacing a Thread's configuration replaces its attachments, and the change takes effect at the Thread's next Turn. A replacement that leaves out attachments keeps the ones the Thread has.
- A SubAgent or forked Thread inherits its source's attachments.
- The runtime creates an attached store's directory when it is missing and never deletes one. The store's content belongs to the host that attaches it. That host may replace the files at any time, for example to deliver another host's changes. Such a replacement reaches running Threads through the memory index section (§5.1).
- Attachments are independent of the memory switches. A Thread with memory disabled uses no store, attached or own.

## 5. Memory In The Prompt

Memory reaches the model through three surfaces. All three are absent when the Thread has memory disabled.

| Surface | Content | Layer |
|---------|---------|-------|
| Memory instructions | Own store path, file format, save rules, and recall rules. For each attached store: its path, name, description, and access, and the rules for choosing a store (§6.1). | Base instructions, the Memory section of [Prompt Composition](../architecture/prompt-composition.md). It holds no memory content, so it changes only with configuration, including a change of attachments. |
| Memory index | The loaded `MEMORY.md` of the own store and of each attached store. | The `memory_index` [World State](../architecture/world-state.md) section. |
| Memory files | One memory's full text. | Read on demand with ordinary file tools. |

### 5.1 Memory index section

`memory_index` follows the World State contract:

- When the model holds no index, at a Thread's first sampling step or after a baseline reset, the section renders the whole loaded index, or a line stating that the index is empty.
- After the index changes, it renders only the added, changed, and removed entries, marked as an update to the index shown earlier.

The section renders the own store's index first, then each attached store's index under that store's name, in attachment order. Each attached index is loaded within the same limits as the own index (§3.2).

The snapshot carries a fingerprint for each index entry, keyed by the store and the entry's target file. A change from any source reaches a running Thread at its next sampling step: the agent, another Thread, memory extraction, Dreams, the user editing files, a host replacing an attached store, or deletion. Attaching a store renders its whole index. Detaching one renders a notice that the store is no longer available.

### 5.2 Recall

Memory is point-in-time background context. Current instructions and observed evidence take precedence over it. Before recommending a file, function, or flag that a memory names, the agent checks that it still exists. When the user says to ignore memory, the agent neither uses, cites, nor mentions it.

When the agent reads a memory file last modified more than one day ago, the read result ends with a note stating the memory's age and that it may be outdated.

## 6. Saving Memory

### 6.1 While responding

The agent saves memory with its ordinary file tools. It writes or updates the memory file, then adds or updates the file's index line.

Save rules:

- Requests to remember, correct, or forget are applied in the same reply, including a request to save something the rules below would skip.
- Otherwise the agent saves knowledge of the four types that will apply to future Threads, in the same reply that responds to the message, before the task continues.
- The agent does not save on its own what the repository or `AGENTS.md` already records, such as code structure, past fixes, or history, or what matters only to the current task.
- Before saving, the agent updates a memory that already covers the knowledge rather than adding a duplicate, and deletes memories that proved wrong.
- When unsure whether knowledge outlasts the current task, the agent does not save it.

Choosing a store, when the Thread has attached stores:

- The agent saves to a `readWrite` attached store only what holds for everyone who uses it, written as the general lesson rather than the case that taught it. It never saves credentials, conversation content, or details that identify a person there.
- What concerns the agent's own role and way of working, and anything it is unsure about, goes to its own store.
- The agent deletes a memory in a `readWrite` store by writing the file empty and removing its index line. A `read` store is never written.

The own store is writable for every memory-enabled Thread, regardless of its mode, working directory, worktree, or workspace-boundary approval policy. So is every `readWrite` attached store, subject to the memory write gate. A `read` store may be read and searched but never written. The exemption covers these stores and nothing else. File operations on any store always run on the machine that hosts the workspace, even when the Thread is connected to a remote workspace. Writes to a store are serialized with each other and with Dreams applying or restoring a run.

#### 6.1.1 Memory write gate

An in-process host may supply a memory write gate. Before a file tool creates or edits a file in a `readWrite` attached store, the runtime passes the gate:

- the store `id`;
- the file's path relative to the store;
- the operation: create, edit, or delete;
- the file's content before the write, or none for a new file;
- the content the write would leave, or none for a deletion;
- the Thread's identity and the Turn the write belongs to.

File tools have no delete operation, so the agent deletes an attached memory by writing it empty. A write that would leave a memory file in an attached store empty is a delete. Once admitted, the runtime removes the file.

The gate admits or rejects the write:

- **Admitted:** the runtime applies the write locally, exactly as computed.
- **Rejected:** the tool call fails with the gate's reason and the store is left unchanged. The reason is model-visible, so the agent can rewrite the memory, save it to its own store, or drop it.

A gate that throws or does not answer within thirty seconds rejects the write with a generic reason. With no gate supplied, every write to a `readWrite` store is admitted. Writes to the own store never pass a gate.

The gate is where a host enforces what instructions cannot guarantee: who may write the store, what content it refuses, and whether the store changed elsewhere since the agent read it. When the previous content no longer matches the host's copy, the host rejects the write. It may first replace the local file, so that reading it again shows the current content.

Only the file tools are gated. Writing a store by any other means, such as a shell command, is not a memory operation. The host's copy of the store prevails over it.

A write that leaves the index beyond 80% of either load limit ends with a notice asking the agent to compact the index now: one line per entry, detail moved into memory files, and stale entries merged or dropped.

### 6.2 Memory extraction

Memory extraction catches knowledge the agent should have saved while responding. The extraction agent decides whether anything is worth saving, so an extraction that saves nothing is a normal outcome.

**Eligibility.** A Turn that completes successfully is eligible when its Thread is not a SubAgent, is not an internal Thread, has memory enabled, and captured extraction as enabled (§7).

**Cursor.** Each Thread keeps an extraction cursor: the last Turn that extraction has considered. When a Turn after the cursor wrote to the Thread's store, extraction does not run for that range and the cursor moves to the latest Turn.

**Cadence.** Of the remaining eligible Turns, extraction runs on every seventh. At most one extraction runs per Thread at a time. A request that arrives while one runs replaces any earlier waiting request and runs after the current one finishes, considering only Turns after the advanced cursor.

**Execution.** Extraction runs as an ephemeral internal fork of the Thread, taken after the latest completed Turn. The fork keeps the Thread's instructions and tool surface and receives one task message. The message asks it to consider only the Turns after the cursor without verifying them against the workspace, to read files before writing, and to update existing memories rather than duplicate them. It also lists the store's memory files by type, name, last-modified date, and description. Tool output quoted in the conversation is data and is never saved as an instruction.

The fork may read anything the Thread may read, attached stores included. It may create and edit Markdown files inside the Thread's own store, and every other call is rejected with a reason, including writes to attached stores. Knowledge for an attached store is saved only while responding, where the Turn that produced it is known. It makes at most five model requests and adds no Items to the Thread.

**Outcome.** On success the cursor advances to the forked Turn. On failure it stays, so the same Turns are considered next time. Extraction never fails or delays a Turn. Traces record each run and the files it wrote.

### 6.3 Consolidation

Dreams periodically consolidates a store: it merges duplicates, corrects drifted facts, prunes the index, and reconciles memory with `AGENTS.md`. It never consolidates an attached store, whose host owns its upkeep.

## 7. Memory Switches

| Setting | Default | Meaning |
|---------|---------|---------|
| `Memory.Enabled` | `true` | New Threads use and maintain memory. |
| `Memory.AutoExtract` | `false` | New memory-enabled Threads run memory extraction. |

A Thread captures both switches at creation and keeps them for its lifetime, like its scope. Changing a setting affects new Threads only; running and resumed Threads keep the values they captured, and replacing a Thread's configuration preserves them. A SubAgent or forked Thread inherits its source's values. Whoever creates a Thread may capture extraction as disabled; automations do.

When memory is disabled, a Thread neither uses nor maintains memory: the memory instructions and the memory index are absent, and no extraction runs.

Features that depend on memory stop while it is disabled and resume with their stored settings when it is enabled again:

- Memory extraction does not run.
- Dreams does not start scheduled or manual runs.
- Welcome suggestions are not generated from memory, and clients fall back to their default suggestions. Conversation prompt suggestions are independent of this switch.

Clients show dependent settings as disabled while memory is off, with their stored values unchanged.

## 8. Presentation

A file tool call whose target lies inside the Thread's own store or one of its attached stores is a memory operation. When recording the Item, Core marks the call in its trusted presentation options as `memory: "recall"` for a read or search, or `memory: "update"` for a write or edit. For an attached store it adds `memoryStore` with the store's `id` and `name`. A write the gate rejected is still marked `memory: "update"`, with the call's failure. Only Core resolves the stores, so clients rely on this mark rather than on paths.

Clients group recall operations as recalled memories and show an update as a memory update whose changes can be reviewed like a file edit. They count memory files, not writes that change only `MEMORY.md`. Extraction adds no Items, so its writes do not appear in the conversation.

## 9. Deleting Memory

AppServer `memory/reset` deletes the contents of every memory store in the current workspace: the workspace store and every scope store. It also deletes all Dreams artifacts. It leaves attached stores untouched, because their content belongs to the host that attaches them. It keeps the store directories, sessions, configuration, skills, plugins, plans, automation tasks, Agent Profiles, and the memory switches.

## 10. Welcome Suggestions

Personalized welcome suggestions receive an explicit snapshot of the workspace store as generation input, alongside dedicated system instructions: the loaded index and the listing of its memory files by type, name, and description. The same captured content supplies the generation request and determines the cache fingerprint. Suggestions are generated in the background after a successful Turn when that content has changed since the cached result. They are skipped when memory is disabled or the store holds no memory files.
