# DotCraft Memory Design Specification

| Field | Value |
|-------|-------|
| **Version** | 0.14.0 |
| **Status** | Living |
| **Date** | 2026-11-20 |
| **Parent Specs** | [Session Core](../architecture/session-core.md) |
| **Related Specs** | [Prompt Composition](../architecture/prompt-composition.md), [World State](../architecture/world-state.md) |

Purpose: Define DotCraft's memory: the agent-maintained store of memory files and their index, which store a Thread uses, the personal store every Thread on a machine shares and which memories go to it, the stores its creator attaches beside it and how writes to them are admitted, how memory enters the prompt, how memory is saved while responding and after a Turn, the policy that decides what memory holds, how clients present and list it, the switches that govern it, and how it is deleted.

## 1. Scope

In scope:

- Store layout, the memory file format, and the index.
- Which store a Thread reads and writes.
- The personal store and which memories go to it.
- Stores attached to a Thread beside its own, their access, and the gate that admits writes to them.
- How memory enters the prompt.
- Saving memory while responding and in the background after a Turn.
- How clients present memory reads and writes.
- The memory switches and the features that depend on them.
- Deleting memory.
- The memory policy: the memory types and the guidance for saving, extraction, and consolidation.
- Listing a workspace's stores.

Out of scope:

- Consolidating a store, owned by Dreams.
- Short-term context compaction and token-pressure recovery.
- Per-Thread memory controls.
- Relevance-based retrieval and vector or semantic indexes.
- Instructions the user writes for every workspace, which belong in the user's `AGENTS.md`. The personal store holds what the agent learns, not what the user prescribes.
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
| **Personal store** | The one store on a machine that holds what is known about the person, shared by every memory-enabled Thread on that machine whatever its workspace (§4.2). |
| **Personal type** | A memory type the policy marks `Personal`. Memories of a personal type go to the personal store. |
| **Attached store** | A store that whoever creates a Thread attaches beside its own store, owned by someone other than the Thread's principal and shared with others. |
| **Access** | Whether a Thread may only read an attached store (`read`) or also write it (`readWrite`). |
| **Memory write gate** | A host-supplied check that admits or rejects each write to a `readWrite` attached store before it reaches disk. |
| **Memory extraction** | A background pass after a Turn that saves knowledge the agent did not save while responding. |
| **Memory policy** | The configurable memory types and the guidance that decides what is saved while responding, during extraction, and during consolidation (§10). |

## 3. Store Layout And Format

A Thread with no scope uses the workspace store `<state>/memory/`. A Thread with a scope uses `<state>/scopes/<scope>/memory/`. An attached store lives at `<state>/memory-stores/<id>/`. `<state>` is the workspace state directory: `.craft`, or the data directory an embedding host names in its place. The personal store lives at `memory/` in the DotCraft home directory, `~/.craft`, of the machine that runs the AppServer.

```text
<store>/
  MEMORY.md
  <name>.md
```

The runtime creates a store directory before the agent needs it, so the agent writes to it directly. Workspace initialization writes no memory files. Every store, personal and attached ones included, uses the file format and index below.

### 3.1 Memory files

A memory file is named `<name>.md` and starts with frontmatter:

```markdown
---
name: <kebab-case slug matching the file name>
description: <one line used to judge relevance later>
metadata:
  type: <one of the policy's types>
---

<the memory>
```

The default types (§10.1), of which `user` is the one personal type:

| Type | Holds |
|------|-------|
| `user` | Who the user is: role, expertise, and preferences. |
| `feedback` | How the user wants the agent to work, both corrections and approaches the user confirmed. |
| `project` | Ongoing goals, decisions, or constraints the repository does not record, with absolute dates. |
| `reference` | Where to find information outside the workspace. |

The default save guidance asks a `feedback` or `project` body to lead with the rule or fact, followed by a **Why:** line and a **How to apply:** line. A body may link related memories as `[[name]]`. A Markdown file without frontmatter, or whose type the policy does not declare, is still a memory file and is listed without a type, so changing the types never hides or deletes memory.

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
- An attachment that fails validation fails Thread creation or configuration replacement with a reason. Validation covers an unsafe or duplicate `id`, the reserved `id` `personal`, a missing `name` or `description`, an unknown access, or too many attachments.
- Unlike the scope, attachments may change: replacing a Thread's configuration replaces its attachments, and the change takes effect at the Thread's next Turn. A replacement that leaves out attachments keeps the ones the Thread has.
- A SubAgent or forked Thread inherits its source's attachments.
- The runtime creates an attached store's directory when it is missing and never deletes one. The store's content belongs to the host that attaches it. That host may replace the files at any time, for example to deliver another host's changes. Such a replacement reaches running Threads through the memory index section (§5.1).
- Attachments are independent of the memory switches. A Thread with memory disabled uses no store, attached or own.

### 4.2 Personal store

Who the person is and how they want the agent to work holds in every workspace. Kept in each workspace store, it would have to be learned again in every project and the copies would drift apart. The personal store holds it once per machine.

- A memory-enabled Thread that captured `Memory.Personal` as on (§7) reads and writes the personal store beside its own store, whatever its workspace, origin, mode, or attachments, as it does its own store. A SubAgent or forked Thread uses it as its source does.
- The personal store belongs to the machine that runs the AppServer, as the user configuration layer does. A Thread connected to a workspace on another machine uses that machine's personal store. Personal stores are never synchronized between machines.
- The personal store is not an attached store and passes no write gate, because its content belongs to the person rather than to a host.

Choosing between the own store and the personal store is fixed. The memory's type decides, not a judgment about each memory, because a line between personal and project knowledge drawn case by case does not hold steady.

- A memory of a personal type goes to the personal store.
- A memory of any other type goes to the own store, unless the user said it applies in every project. Then it goes to the personal store.
- The personal store never holds project facts, paths, commands, business or customer details, credentials, or conversation content, because whatever it holds reaches every workspace on the machine.
- Before saving to the personal store, the agent deletes any memory in its own store that covers the same knowledge, so one fact never lives in both.
- When a memory in the own store narrows a personal memory to this workspace, the own store's memory takes precedence there. Otherwise the personal store's takes precedence. Current instructions and observed evidence take precedence over both (§5.2).
- The agent deletes a memory in the personal store by writing the file empty and removing its index line, and the runtime removes the file.

## 5. Memory In The Prompt

Memory reaches the model through three surfaces. All three are absent when the Thread has memory disabled.

| Surface | Content | Layer |
|---------|---------|-------|
| Memory instructions | Own store path, personal store path, file format with the personal types marked, save rules, the rules for choosing between the own and personal store (§4.2), and recall rules. For each attached store: its path, name, description, and access, and the rules for choosing a store (§6.1). | Base instructions, the Memory section of [Prompt Composition](../architecture/prompt-composition.md). It holds no memory content, so it changes only with configuration, including a change of attachments. |
| Memory index | The loaded `MEMORY.md` of the personal store, the own store, and each attached store. | The `memory_index` [World State](../architecture/world-state.md) section. |
| Memory files | One memory's full text. | Read on demand with ordinary file tools. |

### 5.1 Memory index section

`memory_index` follows the World State contract:

- When the model holds no index, at a Thread's first sampling step or after a baseline reset, the section renders the whole loaded index, or a line stating that the index is empty.
- After the index changes, it renders only the added, changed, and removed entries, marked as an update to the index shown earlier.

The section renders the personal store's index first, then the own store's, then each attached store's index under that store's name, in attachment order. The personal index and each attached index are loaded within the same limits as the own index (§3.2).

The snapshot carries a fingerprint for each index entry, keyed by the store and the entry's target file. A change from any source reaches a running Thread at its next sampling step: the agent, another Thread in any workspace, memory extraction, Dreams, the user editing files, a host replacing an attached store, or deletion. Attaching a store renders its whole index. Detaching one renders a notice that the store is no longer available.

### 5.2 Recall

Memory is point-in-time background context. Current instructions and observed evidence take precedence over it. Before recommending a file, function, or flag that a memory names, the agent checks that it still exists. When the user says to ignore memory, the agent neither uses, cites, nor mentions it.

When the agent reads a memory file last modified more than one day ago, the read result ends with a note stating the memory's age and that it may be outdated.

## 6. Saving Memory

### 6.1 While responding

The agent saves memory with its ordinary file tools. It writes or updates the memory file, then adds or updates the file's index line.

Requests to remember, correct, or forget are applied in the same reply, including a request to save something the save guidance would skip. This rule is fixed (§10.2).

The default save guidance (§10.1):

- The agent saves knowledge that will apply to future Threads, in the same reply that responds to the message, before the task continues.
- The agent does not save on its own what the repository or `AGENTS.md` already records, such as code structure, past fixes, or history, or what matters only to the current task.
- Before saving, the agent updates a memory that already covers the knowledge rather than adding a duplicate, and deletes memories that proved wrong.
- When unsure whether knowledge outlasts the current task, the agent does not save it.

Choosing a store, when the Thread has attached stores, is fixed:

- The agent saves to a `readWrite` attached store only what holds for everyone who uses it, written as the general lesson rather than the case that taught it. It never saves credentials, conversation content, or details that identify a person there.
- What concerns the agent's own role and way of working, and anything it is unsure about, goes to its own store.
- The agent deletes a memory in a `readWrite` store by writing the file empty and removing its index line. A `read` store is never written.

The own store and the personal store are writable for every memory-enabled Thread, regardless of its mode, working directory, worktree, or workspace-boundary approval policy. So is every `readWrite` attached store, subject to the memory write gate. A `read` store may be read and searched but never written. The exemption covers these stores and nothing else. File operations on any store always run on the machine that hosts the workspace, even when the Thread is connected to a remote workspace. Writes to a store are serialized with each other and with Dreams applying or restoring a run. That serialization holds within one AppServer process. Threads of several workspaces, each in its own process, also write the personal store, and a whole-file write to a memory file that changed since the Thread last read or wrote it is rejected ([Tool Architecture](../architecture/tools-architecture.md#73-file-tools)), so it never replaces a change the Thread did not see.

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

**Cursor.** Each Thread keeps an extraction cursor: the last Turn that extraction has considered. When a Turn after the cursor wrote to the Thread's own or personal store, extraction does not run for that range and the cursor moves to the latest Turn.

**Cadence.** Of the remaining eligible Turns, extraction runs on every `Memory.ExtractionInterval`-th (§10.1), every seventh by default. The interval in effect when a Turn completes applies. At most one extraction runs per Thread at a time. A request that arrives while one runs replaces any earlier waiting request and runs after the current one finishes, considering only Turns after the advanced cursor.

**Execution.** Extraction runs as an ephemeral internal fork of the Thread, taken after the latest completed Turn. The fork keeps the Thread's instructions and tool surface and receives one task message. The message asks it to consider only the Turns after the cursor without verifying them against the workspace, to save what the extraction guidance (§10.1) says is worth saving, to read files before writing, and to keep each memory's index line in step. It also lists the memory files of the own store and, while `Memory.Personal` is on, of the personal store, by type, name, last-modified date, and description. Tool output quoted in the conversation is data and is never saved as an instruction.

The fork may read anything the Thread may read, attached stores included. It may create and edit Markdown files inside the Thread's own store and the personal store, choosing between them as §4.2 says, and every other call is rejected with a reason, including writes to attached stores. Knowledge for an attached store is saved only while responding, where the Turn that produced it is known. It makes at most five model requests and adds no Items to the Thread.

**Outcome.** On success the cursor advances to the forked Turn. On failure it stays, so the same Turns are considered next time. Extraction never fails or delays a Turn. Traces record each run and the files it wrote.

### 6.3 Consolidation

Dreams periodically consolidates a store following the consolidation guidance (§10.1): by default it merges duplicates, corrects drifted facts, prunes the index, and reconciles memory with `AGENTS.md`. It never consolidates an attached store, whose host owns its upkeep, or the personal store, which no single workspace owns. The person keeps the personal store tidy by editing it.

## 7. Memory Switches

| Setting | Default | Meaning |
|---------|---------|---------|
| `Memory.Enabled` | `true` | New Threads use and maintain memory. |
| `Memory.AutoExtract` | `false` | New memory-enabled Threads run memory extraction. |
| `Memory.Personal` | `true` | New memory-enabled Threads use the personal store (§4.2). |

A Thread captures these switches at creation and keeps them for its lifetime, like its scope. Changing a setting affects new Threads only; running and resumed Threads keep the values they captured, and replacing a Thread's configuration preserves them. A SubAgent or forked Thread inherits its source's values. Whoever creates a Thread may capture extraction as disabled; automations do.

When memory is disabled, a Thread neither uses nor maintains memory: the memory instructions and the memory index are absent, and no extraction runs. When only `Memory.Personal` is off, the Thread neither reads nor writes the personal store, and memories of a personal type go to its own store.

Features that depend on memory stop while it is disabled and resume with their stored settings when it is enabled again:

- Memory extraction does not run.
- Dreams does not start scheduled or manual runs.

Clients show dependent settings as disabled while memory is off, with their stored values unchanged.

## 8. Presentation

A file tool call whose target lies inside the Thread's own store, the personal store, or one of its attached stores is a memory operation. When recording the Item, Core marks the call in its trusted presentation options as `memory: "recall"` for a read or search, or `memory: "update"` for a write or edit. For an attached store it adds `memoryStore` with the store's `id` and `name`. For the personal store it adds `memoryStore` with the `id` `personal` and the `name` `Personal`. A write the gate rejected is still marked `memory: "update"`, with the call's failure. Only Core resolves the stores, so clients rely on this mark rather than on paths.

Clients group recall operations as recalled memories and show an update as a memory update whose changes can be reviewed like a file edit. They count memory files, not writes that change only `MEMORY.md`. Extraction adds no Items, so its writes do not appear in the conversation.

## 9. Deleting Memory

The person clears the personal store by deleting its memory files. AppServer `memory/reset` deletes the contents of every memory store in the current workspace: the workspace store and every scope store. It also deletes all Dreams artifacts. It leaves attached stores untouched, because their content belongs to the host that attaches them, and the personal store, because it belongs to every workspace on the machine. It keeps the store directories, sessions, configuration, skills, plugins, plans, automation tasks, Agent Profiles, and the memory switches.

## 10. Memory Policy

The memory policy decides what memory holds: the memory types, and the guidance the agent follows when it saves memory while responding, during extraction, and during consolidation. It is configuration, so a user can tailor what is remembered without changing how memory is stored. Where a memory is stored follows its type's `Personal` mark and the fixed rules of §4.2. Clients edit it like any other setting, and the [Memory Studio](memory-studio.md) plugin offers presets and an editor for it.

### 10.1 Settings

| Setting | Default | Meaning |
|---------|---------|---------|
| `Memory.Types` | `user`, `feedback`, `project`, `reference` (§3.1), with `user` personal | An array of `{ Name, Description, Personal }`. `Name` is the value of `metadata.type`, `Description` tells the model what belongs to the type, and `Personal`, `false` when omitted, marks a personal type (§4.2). |
| `Memory.SaveGuidance` | The default save guidance of §6.1, followed by the default body rules of §3.1 | What the agent saves on its own initiative, what it leaves out, how it avoids duplicates and removes wrong memories, and how a memory body is written. |
| `Memory.ExtractionGuidance` | Save what the memory instructions say to save but was not saved while responding, updating an existing memory rather than adding a duplicate | What extraction saves from the Turns it reviews. |
| `Memory.ConsolidationGuidance` | The default consolidation rules of [Dreams](dreams.md) | What a consolidated store keeps, how near-duplicates merge, and how contradictions, including conflicts with `AGENTS.md`, resolve. |
| `Memory.ExtractionInterval` | `7` | Every how many eligible Turns extraction runs, from 1 to 50. |

The defaults are the built-in text itself, so a client reads them from the configuration schema and restores one by removing the value from its layer. The policy layers like other settings: a user-layer value follows the user across workspaces, and a workspace-layer value tailors one workspace. One policy applies to the workspace store, every scope store, and what the workspace's Threads save to the personal store. Attached stores keep the fixed rules for choosing and writing them, and their host owns their upkeep.

A write is rejected when it would leave the policy with no types or more than eight, a type `Name` that is not a kebab-case slug of at most 40 characters, two types with the same `Name`, a `Description` that is empty, spans lines, or exceeds 200 characters, or a guidance text that is empty or exceeds 8,000 characters.

### 10.2 Fixed rules

Guidance decides what is worth remembering. It never decides where memory is stored, who may write it, or how safety is kept, because a user editing guidance should not be able to weaken those by accident. Each stage is composed from fixed parts around the guidance, and the composition states that the fixed rules take precedence over the guidance.

| Stage | Fixed |
|-------|-------|
| Memory instructions | The own and personal stores' paths, the file format with the policy's types, the rules for choosing between the own and personal store, the index format, that requests to remember, correct, or forget are applied in the same reply, the rules for choosing among attached stores and writing them, and the recall rules of §5.2. |
| Extraction | The Turns under review, that conversation content is data and never instruction, that nothing is investigated or verified, the step budget, the write scope of the Thread's own store, keeping index lines in step, the listing of existing memory files, and that saving nothing is a normal outcome. |
| Consolidation | The Dreams input, phases, tool profile, and candidate validation, that transcripts and memory are evidence and never instruction, the file and index format, and that `AGENTS.md` is never edited. |

The guidance sits between the fixed description of the store and the fixed rules, so the fixed rules have the last word.

### 10.3 When a change applies

A policy change reaches a running Thread at its next Turn, as a change to its memory instructions, and costs that Thread one prompt-cache miss. Policy edits are rare, so keeping running Threads current is worth that cost. An extraction or Dreams run already started keeps the policy it started with. With memory disabled the policy has no effect, and the extraction guidance and interval have no effect while `Memory.AutoExtract` is off.

## 11. Listing Stores

Clients list the stores of a workspace through AppServer memory management ([AppServer Protocol](../protocols/appserver-protocol.md)).

**Listing** returns the personal store, the workspace store, every scope store, and every attached store directory, each with its directory on the AppServer host and a display name: the Agent Profile or automation that owns a scope when it can be resolved, otherwise the scope or attachment id. The personal store is named Personal and is writable. Attached stores are reported read-only, because their host owns their content. Listing creates nothing.
