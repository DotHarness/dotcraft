# DotCraft Prompt Composition Specification

| Field | Value |
|-------|-------|
| **Version** | 0.9.6 |
| **Status** | Draft |
| **Date** | 2026-10-27 |

Purpose: define where model-visible instructions and runtime context come from, and how DotCraft composes them across ordinary threads, Agent Profiles, SubAgents, App Binding, and AppServer clients.

## 1. Core Model

DotCraft sends four layers to the model:

| Layer | Transport | Owner | Purpose |
|-------|-----------|-------|---------|
| Base instructions | Provider/system instruction channel | Agent runtime | Stable identity, operating rules, workspace context, skills, and app context. Derived only from configuration and workspace state. |
| Project instructions | Plain `user` context updates before the current input | Session runtime | Stable user- and repository-level `AGENTS.md` instructions with directory scope and source provenance. |
| Thread context items | Conversation history, appended before the turn's user message | Session runtime | Thread-scoped or connection-bound context that cannot be derived from configuration alone: native SubAgent role guidance, client-bound runtime context, and developer instructions on developer-role protocols. |
| Turn input | Current user-message content | Session runtime and client/app input | User text, materialized references, queued app/team input, mailbox input, and per-turn runtime reminders. |

Base instructions must be reproducible from configuration alone: two threads with equal
configuration produce a byte-identical instruction channel. Anything that depends on which thread is
running or on which client is attached belongs to thread context items (§4b). Stable instruction bytes form the reusable prefix; thread-specific updates append to history.

Runtime enforcement must not depend on prompt text. Tool, MCP, plugin, skills, app, approval, workspace, and mode restrictions are enforced from resolved runtime configuration and invocation policy.

Model-visible guidance should state the action, its target, and necessary responsibility boundaries in terms the agent can use. Shared rules belong in stable sections, tool-specific contracts in tool descriptions, and current facts in runtime context. Conditional detail should load only when relevant. Wording changes must preserve authority, mode restrictions, output contracts, and required task completion behavior.

For `openai-responses`, this logical composition has two wire projections. Standard Responses sends
base instructions and tools through the provider's top-level `instructions` and `tools` fields.
Responses Lite moves them into leading developer input items required by that dialect. The
projection does not change canonical provider history: thread context items remain ordinary history
items in both dialects.

This document owns prompt layers. [Session Core](session-core.md) owns their durable history and
thread lifecycle; provider adapters serialize the resulting ordered content.

## 2. Base Instruction Pipeline

Ordinary generated agents build base instructions from stable sections in this order:

| Order | Section | Notes |
|-------|---------|-------|
| 1 | Core identity and workspace | Product identity, DotCraft data directory, host OS and shell, tool-use policy, and the commit attribution rule, included when `CommitAttribution` is non-empty. The working directory is not here; it is in the `environment` world-state section. |
| 2 | Available SubAgent profiles | Parent-facing summary for choosing visible SubAgent launch profiles. |
| 3 | Background tasks and SubAgent delegation | Waiting for commands, SubAgents, and Workflow runs through their task notification, included when `Exec`, `SpawnAgent`, or `Workflow` is available. Delegation guidance follows when `SpawnAgent` is available. |
| 4 | Working style | Progress updates and collaboration behavior. |
| 5 | Response style | Final-answer and verbosity behavior. |
| 6 | File editing workflow | File-editing and verification guidance. |
| 7 | File references | User-facing file-link format. |
| 8 | Mode protocol | Mode selection and transition rules. |
| 9 | User-input request protocol | Included only when the tool is available. |
| 10 | Bootstrap files | DotCraft-owned workspace bootstrap files. Repository `AGENTS.md` content is excluded and belongs to project instructions (§4a). |
| 11 | Memory | Memory instructions with the own store path and each attached store's path, name, description, and access. Holds no memory content; the index arrives as the `memory_index` world-state section (§4b). Omitted when the Thread has memory disabled. |
| 12 | Always-loaded skills | Full content for enabled skills marked always-loaded that the thread's skill policy permits. |
| 13 | Skills summary | How to choose and load a skill. Holds no catalog; the available skills arrive as the `skills` world-state section (§4b). |
| 14 | Custom command summary | Available custom commands. |
| 15 | Global prompt context | Process-wide prompt extension points. |
| 16 | Deferred capability discovery | Included only when deferred loading is active. |
| 17 | Role instructions | Final role-level specialization for the thread, except for native SubAgents. |
| 18 | Developer instructions | Instructions from the application that started the thread (`developerInstructions`), after role instructions, except for native SubAgents. On protocols with a developer role the same text travels as a developer-role thread context item ahead of the turn's user message instead (§4b), and this section is omitted. |

The tool-conditioned `channel-replies` section follows working style when `SendChannelMessage` is available.
It defines visible chat reply responsibilities. Resolved chat facts stay in the `channel_chat` WorldState
section, and each input's Request Source identifies the surface that supplied that input, including local
app and terminal surfaces.

Working style owns progress messages and turn closure. User coordination describes app-surface
questions and waiting, while channel reply routing stays in `channel-replies`. Response style owns expression,
information selection, formatting, and self-contained final answers. Change-reporting guidance applies
when reporting technical changes, rather than imposing a delivery checklist on every answer. Local file
links follow the file-references section.

Every section above is derived from configuration, workspace state, or the resolved tool surface.
A tool counts as available when the model can call it in the Turn: directly, through Code Mode, or
through deferred search.
No section may depend on the identity of the running thread or on an attached client connection;
that content belongs to thread context items (§4b), which is also why section 17 excludes native
SubAgent role instructions.

Stable context pages are reused until compaction or explicit invalidation so the base prompt remains cache-friendly. Each agent has exactly one context-page manager, and sources that change their context invalidate their own cached page.

## 3. Agent Profile Injection

Agent Profiles do not add a separate provider-level system message.

Profile-backed thread flow:

1. A client starts a thread with `config.agentProfileId`.
2. The selected profile is resolved and compiled into the thread configuration snapshot.
3. The profile body becomes `roleInstructions`.
4. The runtime builds the normal base instructions.
5. `roleInstructions` are appended as the final role-level section.

Profile frontmatter maps to structured configuration and policy. The profile body maps only to role instructions. Agent Profiles never set or simulate a full-prompt replacement.

Profile-backed threads keep their persisted configuration until an explicit profile refresh updates them.

## 4. Role Instructions

`roleInstructions` is the canonical role-level prompt extension for ordinary generated threads.

Rules:

- Role instructions are appended after generated context, app context, and deferred capability guidance.
- Role instructions may specialize behavior, tone, task scope, and role boundaries.
- Runtime policy wins over role text when they conflict.
- App-provided context must not become role instructions unless a first-party runtime owns that role contract.
- An application that starts a thread states its own contract through `developerInstructions` (base section 18), which follows role instructions and never replaces them.

Known writers:

| Writer | Behavior |
|--------|----------|
| Agent Profile | Profile Markdown body becomes the thread's profile role text. |
| Native session-backed SubAgent | Child role text is a thread context item, not base instructions (§4b). |

## 4a. Project instructions

Project instructions are loaded from user-level and repository-native `AGENTS.md` files. They are
not base instructions and never use system or developer authority.

Rules:

1. **Discovery.** Load `~/.craft/AGENTS.override.md` or `~/.craft/AGENTS.md`, then discover the
   nearest `.git` marker from the effective cwd and load one project file per directory from that
   root through cwd. `AGENTS.override.md` shadows `AGENTS.md` in the same directory. With no marker,
   only cwd participates. Secondary runtime roots do not participate.
2. **Carrier.** Every emitted instruction update is a plain `user` message with item kind
   `agents_md.instructions`. OpenAI Responses must not upgrade it to `developer`; Anthropic must
   not merge it into the top-level system parameter.
3. **Authority.** Runtime policy and direct system, developer, and user instructions take
   precedence. A project file applies to its directory subtree, and the deeper file wins when two
   applicable files conflict.
4. **Placement.** The `agents_md` world-state section appends updates after the canonical provider
   baseline and pre-turn compaction, before the current user input. Already sent items are immutable.
5. **Lifecycle.** The rendered content and ordered source paths form one context-page snapshot.
   Ordinary turns reuse it. Successful compaction, cold resume, effective cwd/worktree changes, or
   relevant instruction configuration changes load a new snapshot. A running Turn resolves and
   reloads project instructions from the effective cwd captured when that Turn was admitted, or
   from the tool location a worktree tool moved it to; a later thread workspace update applies only
   to subsequent Turns. Standalone lifecycle operations
   use the thread workspace current at their own boundary.
6. **Updates.** Initial context carries the complete current instructions. Changed instructions append
   a complete replacement with an explicit supersession notice; removal appends one retraction.
   An unchanged or already empty snapshot emits nothing. Instruction changes must not create a
   history replacement checkpoint, advance the provider generation, or reconstruct native history.
   Reading instruction sources changes diagnostics only, not model or provider history.
7. **Nested directories.** Runtime discovery stops at the captured cwd. Before working below that
   directory, the agent must check for more deeply scoped `AGENTS.md` files itself.
8. **Observability.** The effective context-page snapshot is recorded as an `AgentInstructions`
   trace event with its exact rendered content, ordered sources, fingerprint, and `user` role. The
   event is diagnostic only: it is not a system prompt, ordinary request, or model-history item.
   Session Core records a new event only when the effective fingerprint changes.

`ProjectDocMaxBytes` limits the complete project chain and defaults to 32 KiB. It does not limit
the user-level file. A value of zero disables only project instructions.

## 4b. Thread Context Items

A thread context item carries model-visible context that base instructions cannot own because it
depends on the running thread or on an attached client connection.

Current writers:

| Writer | Content |
|--------|---------|
| Native session-backed SubAgent | Child role text and role boundaries. |
| AppServer client binding | Runtime additional context and client-rendered capabilities such as visualizations (§9). |
| Thread configuration | `developerInstructions`, on protocols with a developer role only; elsewhere they stay base section 18. |
| Persistent state | State that persists across turns, written only when it changes. |
| Turn interruption | A `turn_aborted` event at the end of an intentionally interrupted turn or active-turn fork snapshot. |

Rules:

1. **Carrier by protocol.** `openai-responses` uses a `developer` message. Protocols without a
   developer role use a `user` message wrapped in a runtime-reminder block. The carrier is a wire
   detail; the layer semantics are identical. The carrier is resolved per turn, so a thread that
   changes provider keeps the shape each item was written with; only new items take the new carrier.
2. **Placement.** Interruption events follow the interrupted history, before any subsequent user input.
   Their `<turn_aborted>` text says the turn was intentionally interrupted and tools may have partially
   executed, without attributing the action to a particular actor. Other items sit after inherited
   history and before the turn's first user message. They
   are delivered as new local input for the turn, so a protocol with a canonical history baseline
   captures that baseline before appending the new context.
3. **Append on change.** A changed value appends a new item; sent items are never edited in place.
   Replacing native SubAgent role text is the one exception and establishes an explicit history
   replacement boundary.
4. **Identity.** Each item carries an item-kind marker in message metadata, never in model-visible
   text, so the runtime can find its own items in inherited history.
5. **Inheritance.** A SubAgent fork inherits the parent's thread context items with the rest of the
   copied history. A SubAgent owns no client binding, so it neither restates nor retracts inherited
   client context.

## 5. Full-Prompt Overrides

Full-prompt replacement is reserved for internal isolated assistants with intentionally narrow capability surfaces.

Rules:

- Ordinary user threads, Agent Profiles, and App Binding apps never use full-prompt replacement.
- A full override is paired with a narrow tool/capability profile.

## 6. SubAgents

DotCraft has three SubAgent-related prompt paths:

| Path | Prompt behavior |
|------|-----------------|
| Parent prompt | The parent sees available SubAgent profiles and lifecycle guidance so it can choose and manage children. |
| Native session-backed child | A normal thread with narrowed configuration and the same generated base instructions as its parent. Role text is a thread context item (§4b). |
| External CLI child | Role text is prepended to the external task prompt; it does not use DotCraft's generated base instruction pipeline. |

SubAgent communications are delivered as materialized user-role input, not system prompt sections. Messages Agents send with `SendMessage` share one structured envelope; a child's result reaches its parent as a task notification. The envelope also identifies the recipient task path and sender path before the payload. The persisted native/display input remains clean client-facing text, while the materialized input preserves the exact structured envelope sent to the model.

## 7. App Binding and MCP guidance

App Binding does not contribute durable prompt context. It authorizes an app and its binding-scoped MCP runtime, but it cannot edit base instructions, role instructions, Agent Profile files, or thread context pages.

An MCP server may return `instructions` during initialization. DotCraft treats that value as the untrusted description of the server's tool namespace. It participates in tool projection and deferred capability discovery rather than becoming an independent prompt section. Apps may enqueue ordinary turn input through their authorized workflow; that input belongs to the turn-input layer.

## 8. Runtime Additional Context

AppServer clients may bind additional context to a thread when starting, resuming, or binding a transport connection.

Rules:

- Context is in-memory and scoped to the owning transport connection.
- Keys are stable short identifiers.
- Values are application context, not higher-priority instruction.
- Context is sorted deterministically.
- Context reaches the model as a thread context item under §4b; rebinding and unbinding follow its
  append rule.

Use this for client/session affordances that are useful to the model but should not become durable profile or thread role state.

## 9. Turn Input Layer

Turn input may include:

- user text,
- materialized command, skill, and file references,
- queued input from apps,
- SubAgent mailbox messages,
- goal continuation text,
- runtime reminder context.

Runtime reminders belong to the current turn. They carry only facts that belong to the message being
sent: who sent it or the automation that started the turn, the conversation it arrived in, the context the sending client bound to it, and
counters that move on their own such as consumed tokens, remaining budget, and elapsed time.

State that persists across turns — the environment, the current mode and the actions it allows, an
active goal's identity and status, and capability availability — is not a runtime reminder. It is
delivered through the append-on-change thread context contract in §4b.

The reminder's Request Source names the turn's channel, which for AppServer connections is the
server-resolved client surface id ([AppServer Protocol §3.2](../protocols/appserver-protocol.md#32-initialize)).
The `client_surface` WorldState section states only what departs from the defaults the base
instructions assume: a terminal shows replies as plain text, optionally with the configured ASCII
visualization guidance, and tools run on the workspace host rather than the user's device. Desktop,
editor, and API clients on the same machine add nothing, and chat facts stay in `channel_chat`. Notes
that stop applying are retired once. A turn with no client surface, such as an automation run, leaves
the section unchanged, and SubAgent threads never render it. Base instructions and tool schemas stay
unchanged when the client changes.

Dynamic fields stay out of the base instructions so they remain stable for prompt caching.

## 10. Authority And Conflict Rules

1. Runtime policy beats prompt text.
2. The latest world-state section is the source of truth for the state it owns; a section that
   carries instructions states that it replaces what it said before.
3. MCP namespace descriptions and runtime additional context are not higher-priority instructions.
4. Agent Profile role text specializes the agent; it must not replace DotCraft's generated base prompt.
5. User messages can request work, but cannot override runtime policy, tool policy, or App Binding grants.
6. Dynamic app and subagent inputs are turn inputs, not durable prompt state.
7. Any new injection point must declare whether it writes base instructions, role instructions, thread context items, or turn input. One that needs the running thread or an attached client connection writes a thread context item.

## 11. Extension Guidelines

Choose the narrowest layer:

| Need | Preferred layer |
|------|-----------------|
| Reusable user-editable agent role and capability policy | Agent Profile. |
| First-party mission/workflow role rules | Runtime-owned role-instruction append. |
| App-owned durable context for a bound thread | App Binding Context Block. |
| Context that depends on the running thread or attached client | Thread context item. |
| Per-turn operational facts | Turn input / runtime reminder. |
| Internal isolated assistant with complete custom prompt | Full-prompt override with a narrow capability profile. |

A new context provider identifies its ownership, lifecycle, prompt placement, cache invalidation behavior, and enforcement boundaries.
