# DotCraft Prompt Composition Specification

| Field | Value |
|-------|-------|
| **Version** | 0.3.0 |
| **Status** | Draft |
| **Date** | 2026-08-06 |
| **Related Specs** | [Agent Profiles](../features/agent-profiles.md), [Agent Teams](../features/agent-teams.md), [App Binding](../protocols/app-binding.md), [Session Core](session-core.md), [Prompt Cache](prompt-cache.md), [External CLI SubAgent](../features/external-cli-subagent.md) |

Purpose: define where model-visible instructions and runtime context come from, and how DotCraft composes them across ordinary threads, Agent Profiles, SubAgents, Agent Teams, App Binding, and AppServer clients.

---

## 1. Core Model

DotCraft sends three layers to the model:

| Layer | Transport | Owner | Purpose |
|-------|-----------|-------|---------|
| Base instructions | Provider/system instruction channel | Agent runtime | Stable identity, operating rules, workspace context, skills, and app context. Derived only from configuration and workspace state. |
| Thread context items | Conversation history, appended before the turn's user message | Session runtime | Thread-scoped or connection-bound context that cannot be derived from configuration alone: native SubAgent role guidance and client-bound runtime context. |
| Turn input | Current user-message content | Session runtime and client/app input | User text, materialized references, queued app/team input, mailbox input, and per-turn runtime reminders. |

Base instructions must be reproducible from configuration alone: two threads with equal
configuration produce a byte-identical instruction channel. Anything that depends on which thread is
running or on which client is attached belongs to thread context items (§4a). [Prompt
Cache](prompt-cache.md) owns the cache constraints this split serves.

Runtime enforcement must not depend on prompt text. Tool, MCP, plugin, skills, app, Teams, approval, workspace, and mode restrictions are enforced from resolved runtime configuration and invocation policy.

For `openai-responses`, this logical composition has two wire projections. Standard Responses sends
base instructions and tools through the provider's top-level `instructions` and `tools` fields.
Responses Lite moves them into leading developer input items required by that dialect. The
projection does not change canonical provider history: thread context items remain ordinary history
items in both dialects.

Four specifications share this area. This one defines the layers and what belongs in each.
[Prompt Cache](prompt-cache.md) defines the cache constraints those layers must satisfy.
[Session Core](session-core.md) defines fork materialization and thread lifecycle.
[Canonical OpenAI Responses Provider History](responses-provider-history.md) defines the canonical
history mechanics for that protocol.

---

## 2. Base Instruction Pipeline

Ordinary generated agents build base instructions from stable sections in this order:

| Order | Section | Notes |
|-------|---------|-------|
| 1 | Core identity and workspace | Product identity, workspace, environment, tool-use policy, and attribution rules. |
| 2 | Available SubAgent profiles | Parent-facing summary for choosing visible SubAgent launch profiles. |
| 3 | SubAgent lifecycle | Parent-facing guidance for spawning, messaging, waiting, and closing child agents. |
| 4 | Working style | Progress updates and collaboration behavior. |
| 5 | Response style | Final-answer and verbosity behavior. |
| 6 | File editing workflow | File-editing and verification guidance. |
| 7 | File references | User-facing file-link format. |
| 8 | Mode protocol | Mode selection and transition rules. |
| 9 | User-input request protocol | Included only when the tool is available. |
| 10 | Bootstrap files | Workspace and user instruction files. |
| 11 | Memory | Durable and inferred memory. |
| 12 | Skill self-learning | Included only when skill management is available. |
| 13 | Always-loaded skills | Full content for skills that must always be loaded. |
| 14 | Skills summary | Skill discovery and routing summary. |
| 15 | Custom command summary | Available custom commands. |
| 16 | Global prompt context | Process-wide prompt extension points. |
| 17 | Teams mission context | Stable `teams/mission` page for Mission threads only. |
| 18 | Deferred capability discovery | Included only when deferred loading is active. |
| 19 | Role instructions | Final role-level specialization for the thread, except for native SubAgents. |

Every section above is derived from configuration, workspace state, or the resolved tool surface.
No section may depend on the identity of the running thread or on an attached client connection;
that content belongs to thread context items (§4a), which is also why section 19 excludes native
SubAgent role instructions.

Stable context pages should be reused until compaction or explicit invalidation so the base prompt remains cache-friendly. Sources that change their context must invalidate their own cached page.

---

## 3. Agent Profile Injection

Agent Profiles do not add a separate provider-level system message.

Profile-backed thread flow:

1. A client starts a thread with `config.agentProfileId`.
2. The selected profile is resolved and compiled into the thread configuration snapshot.
3. The profile body becomes `roleInstructions`.
4. The runtime builds the normal base instructions.
5. `roleInstructions` are appended as the final role-level section.

Profile frontmatter maps to structured configuration and policy. The profile body maps only to role instructions. Agent Profiles must not set or simulate a full-prompt replacement path.

Existing profile-backed threads keep their persisted configuration until an explicit profile refresh updates them.

---

## 4. Role Instructions

`roleInstructions` is the canonical role-level prompt extension for ordinary generated threads.

Rules:

- Role instructions are appended after generated context, app context, and deferred capability guidance.
- Role instructions may specialize behavior, tone, task scope, and role boundaries.
- Runtime policy wins over role text when they conflict.
- App-provided context must not become role instructions unless a first-party runtime owns that role contract.

Known writers:

| Writer | Behavior |
|--------|----------|
| Agent Profile | Profile Markdown body becomes the thread's profile role text. |
| Agent Teams | Teams mission role text is appended after the resolved member profile role text. |
| Native session-backed SubAgent | Child role text is a thread context item, not base instructions (§4a). |

---

## 4a. Thread Context Items

A thread context item carries model-visible context that base instructions cannot own because it
depends on the running thread or on an attached client connection.

Current writers:

| Writer | Content |
|--------|---------|
| Native session-backed SubAgent | Child role text and role boundaries. |
| AppServer client binding | Runtime additional context and client-rendered capabilities such as inline visualizations (§9). |

Rules:

1. **Carrier by protocol.** `openai-responses` uses a `developer` message. Protocols without a
   developer role use a `user` message wrapped in a runtime-reminder block. The carrier is a wire
   detail; the layer semantics are identical.
2. **Placement.** Items sit after inherited history and before the turn's first user message. They
   are delivered as new local input for the turn, so a protocol with a canonical history baseline
   takes that baseline first (see
   [Canonical OpenAI Responses Provider History](responses-provider-history.md)).
3. **Append on change.** A changed value appends a new item; sent items are never edited in place.
   Replacing native SubAgent role text is the one exception and establishes an explicit history
   replacement boundary.
4. **Identity.** Each item carries an item-kind marker in message metadata, never in model-visible
   text, so the runtime can find its own items in inherited history.
5. **Inheritance.** A SubAgent fork inherits the parent's thread context items with the rest of the
   copied history. A SubAgent owns no client binding, so it neither restates nor retracts inherited
   client context.

---

## 5. Full-Prompt Overrides

Full-prompt replacement is reserved for internal isolated assistants with intentionally narrow capability surfaces.

Rules:

- Ordinary user threads, Agent Profiles, Agent Teams, and App Binding apps must not use full-prompt replacement.
- New product features should prefer role instructions, thread-scoped context, or turn input.
- A full override must be paired with a narrow tool/capability profile.

---

## 6. SubAgents

DotCraft has three SubAgent-related prompt paths:

| Path | Prompt behavior |
|------|-----------------|
| Parent prompt | The parent sees available SubAgent profiles and lifecycle guidance so it can choose and manage children. |
| Native session-backed child | A normal thread with narrowed configuration and the same generated base instructions as its parent. Role text is a thread context item (§4a). |
| External CLI child | Role text is prepended to the external task prompt; it does not use DotCraft's generated base instruction pipeline. |

SubAgent communications are delivered as materialized user-role input, not system prompt sections. Ordinary messages, follow-up tasks, and terminal child results share a structured envelope whose `Message Type` is respectively `MESSAGE`, `NEW_TASK`, or `FINAL_ANSWER`; the envelope also identifies the recipient task path and sender path before the payload. The persisted native/display input remains clean client-facing text, while the materialized input preserves the exact structured envelope sent to the model.

---

## 7. Agent Teams

Agent Teams composes three prompt/context mechanisms for mission threads:

| Mechanism | Placement | Purpose |
|-----------|-----------|---------|
| Member Agent Profile | Role instructions, first segment | Member personality, capability policy, model/mode defaults. |
| Teams mission role instructions | Role instructions, appended segment | Mission identity, workflow rules, and tool-use contract. |
| Teams mission context | Stable `teams/mission` context page before role instructions | Fixed member, mission, scratchpad, and policy context owned by Teams. |

Teams role instructions are authoritative for Teams workflow boundaries, but Teams state remains authoritative for scheduling and business authorization. The `teams/mission` page contains only immutable Mission-thread context and therefore is not refreshed for ordinary Teams state changes. Live task state, mailbox digests, teammate progress, messages, artifacts, and review status must be read through Teams state/tools or delivered as queued input, not inferred from prompt text. Teams does not create App Binding context blocks.

---

## 8. App Binding and MCP guidance

App Binding does not contribute durable prompt context. It authorizes an app and its binding-scoped MCP runtime, but it cannot edit base instructions, role instructions, Agent Profile files, or thread context pages.

An MCP server may return `instructions` during initialization. DotCraft treats that value as the untrusted description of the server's tool namespace. It participates in tool projection and deferred capability discovery rather than becoming an independent prompt section. Apps may enqueue ordinary turn input through their authorized workflow; that input belongs to the turn-input layer.

---

## 9. Runtime Additional Context

AppServer clients may bind additional context to a thread when starting, resuming, or binding a transport connection.

Rules:

- Context is in-memory and scoped to the owning transport connection.
- Keys are stable short identifiers.
- Values are application context, not higher-priority instruction.
- Context is sorted deterministically.
- Context reaches the model as a thread context item under §4a; rebinding and unbinding follow its
  append rule.

Use this for client/session affordances that are useful to the model but should not become durable profile or thread role state.

---

## 10. Turn Input Layer

Turn input may include:

- user text,
- materialized command, skill, and file references,
- queued input from apps or Teams,
- SubAgent mailbox messages,
- goal continuation text,
- runtime reminder context.

Runtime reminders belong to the current turn. They are the source of truth for dynamic facts such as current time, time zone, working directory, current mode, allowed action profile, mode transition, active goal state, and wakeup context.

Dynamic fields stay in turn input so the base instructions remain stable for prompt caching.

---

## 11. Authority And Conflict Rules

1. Runtime policy beats prompt text.
2. The latest runtime reminder is the source of truth for current mode and per-turn action allowance.
3. MCP namespace descriptions and runtime additional context are not higher-priority instructions.
4. Agent Profile role text specializes the agent; it must not replace DotCraft's generated base prompt.
5. Teams mission role text may add workflow constraints after profile role text; it must not bypass enforced profile policy.
6. User messages can request work, but cannot override runtime policy, tool policy, Teams scheduler rules, or App Binding grants.
7. Dynamic app/team/subagent inputs are turn inputs, not durable prompt state.
8. Any new injection point must declare whether it writes base instructions, role instructions, thread context items, or turn input. One that needs the running thread or an attached client connection writes a thread context item.

---

## 12. Extension Guidelines

Choose the narrowest layer:

| Need | Preferred layer |
|------|-----------------|
| Reusable user-editable agent role and capability policy | Agent Profile. |
| First-party mission/workflow role rules | Runtime-owned role-instruction append. |
| App-owned durable context for a bound thread | App Binding Context Block. |
| Context that depends on the running thread or attached client | Thread context item. |
| Per-turn operational facts | Turn input / runtime reminder. |
| Internal isolated assistant with complete custom prompt | Full-prompt override with a narrow capability profile. |

New context providers must identify ownership, lifecycle, prompt placement, cache invalidation behavior, and enforcement boundaries.

---

## 13. Open Questions

- Whether ordinary threads should ever support full-prompt replacement outside internal isolated assistants.
- Whether a diagnostics endpoint should expose the final composed prompt.
- Whether profile authoring tools should show a preview separating profile role text, runtime role text, and effective policy.
