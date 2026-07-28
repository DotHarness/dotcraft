# DotCraft Agent Profiles Specification

| Field | Value |
|-------|-------|
| **Version** | 0.3.0 |
| **Status** | Draft |
| **Date** | 2026-07-28 |
| **Related Specs** | [Prompt Composition](../architecture/prompt-composition.md), [Agent Teams](agent-teams.md), [Session Core](../architecture/session-core.md), [AppServer Protocol](../protocols/appserver-protocol.md), [App Binding](../protocols/app-binding.md) |

Purpose: define Agent Profiles as reusable agent configuration templates. A profile gives a thread a role, default runtime preferences, and enforceable capability policy without replacing DotCraft's generated base instructions.

---

## 1. Scope

Agent Profiles define:

- Markdown profile documents with YAML frontmatter and a Markdown role body.
- Deterministic source resolution across built-in, plugin, user, workspace, and managed profiles.
- Compilation into a persisted thread configuration snapshot.
- Profile-backed ordinary threads and Agent Teams mission threads.
- Management APIs for profile authoring, validation, readback, and explicit thread refresh.
- Policy rules for tools, MCP, plugins/apps, skills, approvals, and Teams reserved tools.

Out of scope:

- Full-prompt replacement for ordinary profile-backed threads.
- Automatic mutation of existing threads when profile files change.
- Profile inheritance or field-by-field merging between same-id profiles.
- A visual profile builder or agent-profile generation workflow.
- SubAgent-specific profile migration beyond the prompt-composition rules.

---

## 2. Principles

- The thread configuration snapshot is the runtime contract. Agents, tool filtering, approvals, and prompt rendering read the resolved thread configuration, not the profile document.
- Profile prompt text is not security. Security-sensitive behavior must be enforced by runtime policy.
- Profile resolution is deterministic. Higher-priority sources shadow lower-priority profiles as whole documents.
- Existing threads are stable. A profile file change affects new threads only, unless a client explicitly refreshes an existing profile-backed thread.
- Profiles specialize the generated DotCraft prompt through role instructions. They must not replace the base prompt.
- Model policy is atomic. A profile either inherits the complete effective provider preference or pins one complete Profile model preset; canonical profiles do not merge individual model-option fields with workspace defaults.
- Overlays are narrow. Profile-backed thread creation may override ordinary runtime model choices, but must not use request-time overlays to broaden capabilities.
- Teams owns coordination. Team profiles define member capability and role style; Teams state, scheduler rules, reserved tools, and mission context remain owned by Teams.

---

## 3. Profile Document

A profile is a Markdown file with YAML frontmatter. The frontmatter declares structured configuration. The Markdown body declares role instructions.

Minimal shape:

```markdown
---
name: team-reviewer
description: Read-only reviewer focused on correctness, risks, and tests.
avatar: 457
tools:
  deny: [WriteFile, EditFile, Exec, WriteStdin]
permissions:
  approvalPolicy: default
---

You are the Reviewer. Focus on correctness, risk, and missing tests.
Do not edit files unless the mission explicitly reassigns you.
```

Required fields:

| Field | Meaning |
|-------|---------|
| `name` | Stable profile id. |
| `description` | Human-readable purpose and selection hint. |

Supported frontmatter groups:

| Field | Meaning |
|-------|---------|
| `providerPreference` | Optional fixed model preset for new profile-backed threads. When present it contains `providerId`, `model`, reasoning enabled/effort, speed, and context-window mode. Reasoning output visibility is selected from the model catalog at runtime rather than authored in a profile. |
| `mode`, `promptProfile` | Other runtime defaults for new profile-backed threads. |
| `avatar` | Optional packed non-negative integer client visual identity metadata. Bits 0-3 encode `palette`, bits 4-6 encode `face`, and bits 7-9 encode `accessory`. It is not compiled into thread configuration or model-visible instructions. |
| `tools` | Built-in, dynamic, deferred, and agent-control tool policy. |
| `mcp` | MCP server and MCP tool policy. |
| `plugins` | Plugin/app capability policy. |
| `skills` | Skill preload, list/read access, and skill-management policy. |
| `permissions` | Approval and workspace-boundary policy. |
| `teams` | Teams reserved-tool behavior. |
| `locked` | Managed-source governance constraints only. |

Validation rules:

- `name` must be stable and safe for storage and API use.
- `description` must be present for valid authoring and selection UX.
- Unknown fields are rejected unless explicitly marked experimental.
- The Markdown body maps to role instructions, not a base-prompt replacement.
- An omitted `providerPreference` captures the complete effective workspace/global provider preference when a new thread is created.
- A present `providerPreference` requires a non-empty `providerId`, `model`, `reasoning`, `speed`, and `contextWindow`.
- An empty or partial `providerPreference` is invalid; omission is the only inherited form.
- `providerPreference.reasoning` contains only `enabled` and `effort`. `output` is not a Profile field and is rejected as unknown.
- Canonical profiles do not support partial model inheritance such as pinning a model while inheriting reasoning or overriding reasoning while inheriting the model.
- A profile with a fixed provider preference may remain structurally valid when its provider or model is unavailable in the current workspace. Management APIs report that runtime diagnostic, and thread creation fails without changing thread state until the provider becomes runnable.
- Non-managed profiles must not declare managed-only locks.
- Profiles from lower-trust sources must not silently grant high-risk tools, MCP servers, skill management, approval bypass, or broad filesystem/shell access outside their trust boundary.

A fixed provider preference uses this shape:

```yaml
providerPreference:
  providerId: openai
  model: gpt-5.6
  reasoning:
    enabled: true
    effort: high
  speed: fast
  contextWindow:
    mode: max
```

---

## 4. Sources And Resolution

Source priority, from lowest to highest:

1. `builtIn`
2. `plugin`
3. `user`
4. `workspace`
5. `managed`

Resolution rules:

- The highest-priority valid profile with the requested id wins.
- Same-id profiles do not merge field by field.
- Built-in, plugin, and managed profiles are read-only through ordinary profile CRUD.
- User and workspace profiles are writable through ordinary profile CRUD.
- Invalid profiles do not block discovery of other profiles; they appear with diagnostics when identifiable.
- Diagnostics should explain selected source, shadowed sources, restrictions, locked fields, validation errors, and stale thread fingerprints.

Managed policy may constrain lower-priority profiles through explicit locks. Without such a lock, priority is whole-document shadowing.

---

## 5. Thread Resolution

When a client starts a thread with `config.agentProfileId`:

1. Resolve the profile id for the target workspace and user context.
2. Validate the selected profile and source restrictions.
3. Compile profile frontmatter into structured thread configuration fields.
4. Map the profile body into `roleInstructions`.
5. Persist profile provenance: `agentProfileId`, `agentProfileSource`, and `agentProfileFingerprint`.
6. Materialize the profile model preset as one complete provider preference, deriving reasoning output visibility from the selected model's catalog default.
7. Apply allowed runtime model overlays and normalize the resulting provider preference as one unit.
8. Reject unsupported overlays that would broaden capabilities or replace the base prompt.
9. Create the thread with the resolved configuration snapshot.

Existing profile-backed threads do not reread profile documents automatically. `agent/profiles/refreshThread` is the explicit operation for recompiling the currently resolved profile and updating a thread snapshot.

Refresh model behavior is deterministic:

- refreshing from a profile without `providerPreference` preserves the existing thread's complete provider/model/reasoning/speed/context-window snapshot;
- refreshing from a profile with `providerPreference` replaces that complete snapshot atomically;
- changing workspace/global provider preferences never mutates an existing thread;
- returning an existing thread to current workspace model defaults is a separate explicit model-reset operation, not a side effect of profile refresh.

If the profile is missing, invalid, blocked by source restrictions, or incompatible with requested overlays, thread creation or refresh fails before changing thread state.

---

## 6. Effective Thread Contract

A profile-backed thread stores:

| Field | Meaning |
|-------|---------|
| `agentProfileId` | Requested profile id. |
| `agentProfileSource` | Source selected during resolution. |
| `agentProfileFingerprint` | Stable fingerprint for stale-thread detection. |
| `roleInstructions` | Profile body, optionally followed by first-party runtime role text. |
| Model snapshot | Complete provider, model, reasoning, speed, and context-window values resolved at thread creation. |
| Runtime defaults | Mode and prompt profile when set by the profile. |
| Capability policies | Tool, MCP, plugin/app, skills, approval, workspace-boundary, and Teams policy. |

Policy semantics:

- Omitted policy means the existing runtime default applies.
- Omitted `allow` means no allow-list is applied for that policy dimension.
- Empty `allow` means no capability is allowed for that dimension except explicit runtime-reserved capabilities.
- Empty or omitted `deny` means no deny-list is applied.
- Deny wins over allow.
- Legacy exact-name tool filters compose with structured tool policy. The effective surface is the intersection of all allows after all denies are applied.
- Profile policy must be enforced both when tools are shown to the model and when calls are invoked.

---

## 7. Prompt Composition

Agent Profiles use the role-instruction layer described in [Prompt Composition](../architecture/prompt-composition.md).

Rules:

- The generated DotCraft base instructions remain present for ordinary profile-backed threads.
- The profile body is appended as role instructions near the end of the base instruction pipeline.
- App Binding context and runtime additional context remain context, not higher-priority instructions.
- Runtime reminders such as current time, mode, goal, and wakeup context belong to the turn-input layer.
- Full-prompt replacement is reserved for isolated internal assistants with intentionally narrow tool surfaces; profiles, Teams, and App Binding must not use it.

Role instruction writers:

| Writer | Role-instruction behavior |
|--------|---------------------------|
| Agent Profile | Profile body becomes the base role text for the thread. |
| Agent Teams | Teams mission role text is appended after the member profile role text. |
| Native session-backed SubAgent | Child role text is written as the child thread's role instructions, normally with a light prompt profile. |

---

## 8. Enforcement

The resolved profile policy affects:

- initial tool list,
- deferred tool discovery,
- dynamic tool injection,
- MCP tools,
- plugin/app tools,
- skills list/read/manage tools,
- actual invocation,
- approval and workspace-boundary handling.

Invocation enforcement is mandatory even when discovery filtering is also present. A stale or hidden call to a denied capability must fail safely.

Teams coordination capabilities are reserved when Teams owns the thread. A normal profile allow-list must not remove required Teams coordination tools while `teams.reservedTools = keep`.

---

## 9. Management API

Profile management uses Markdown as the primary authoring format.

| Method | Purpose |
|--------|---------|
| `agent/profiles/list` | List profiles, source metadata, validity, fingerprints, diagnostics, and stale thread hints. |
| `agent/profiles/read` | Read one profile as raw Markdown plus parsed summary and diagnostics. |
| `agent/profiles/validate` | Validate raw Markdown without writing. |
| `agent/profiles/upsert` | Create or replace a writable user/workspace profile. |
| `agent/profiles/remove` | Remove a writable user/workspace profile. |
| `agent/profiles/refreshThread` | Explicitly refresh a profile-backed thread from the current resolved profile. |
| `agent/profiles/builderDraft/read` | Read the transient working draft for a bound conversational builder thread. |
| `agent/profiles/builderDraft/update` | Replace the transient working draft for a bound conversational builder thread without persisting a profile file. |

Rules:

- Writes validate before changing storage.
- Upsert requires the requested id to match the frontmatter `name`.
- Read-only sources return clear errors for write attempts.
- List/read responses include diagnostics instead of forcing clients to parse Markdown.
- Error codes should be stable; user-facing localization belongs to clients.

---

## 10. Agent Teams Integration

Teams members may reference an Agent Profile. Default role mapping:

| Team role | Default profile |
|-----------|-----------------|
| Leader | `team-leader` |
| Explorer | `team-explorer` |
| Builder | `team-builder` |
| Reviewer | `team-reviewer` |
| Operator | `team-operator` |

Mission-thread creation and repair follow this order:

1. Resolve the member profile, falling back to the role default when allowed.
2. Compile the profile into the thread configuration snapshot.
3. Append Teams-owned role instructions after profile role instructions.
4. Bind Teams app context blocks for mission, role, and policy context.
5. Preserve Teams reserved tools required for mission coordination.
6. Report missing, invalid, fallback, and stale profile diagnostics to Teams views.

Profiles affect member capability and role style. They do not replace Teams scheduling, mailbox, task state, artifact, review-gate, or mission-finalization rules.

---

## 11. Governance And Diagnostics

Governance features:

- Managed profiles have highest priority and may lock fields.
- Plugin profile packs are read-only and subject to trust-boundary restrictions.
- Managed locks may force, cap, deny, or require values for specific profile fields.
- Lower-priority profiles conflicting with locks are invalid or compiled under the lock, depending on the declared managed policy.

Diagnostics should support:

- profile authoring errors,
- source shadowing,
- read-only/write restrictions,
- high-risk declarations stripped by trust policy,
- locked-field conflicts,
- effective policy summaries,
- existing threads using stale fingerprints,
- refresh success or failure.

Audit records should exist for profile writes, removals, and explicit thread refresh attempts. Audit entries use stable machine-readable codes and structured fields.

---

## 12. Authoring And UX Process

Profile authoring tools should:

- validate Markdown before writing,
- show the effective profile source and policy summary,
- distinguish role text from enforced policy,
- warn when an edit will not affect existing threads until refresh,
- expose stale-thread hints and explicit refresh,
- avoid editing raw profile files for generated-agent workflows when a guided editor or generator is available.

Agent-profile generation tools may propose or modify Markdown profiles, but they must still use the same validation, source, policy, and refresh rules defined here.

---

## 12A. Conversational Builder

The conversational builder lets a user create or refine an Agent Profile by talking to a dedicated **profile-builder agent** instead of (or alongside) the structured editor. It is a normal Session Core thread reusing the standard turn, streaming, and tool-call machinery. The only Agent Builder-specific AppServer methods are transient working-draft synchronization methods; conversation itself still uses ordinary thread/turn APIs.

### 12A.1 Model

- **Builder thread.** A profile-builder conversation runs in an ordinary Session Core thread source with internal visibility and a builder target binding. It is excluded from ordinary thread listings by visibility/runtime metadata, not by introducing a third `ThreadSource` kind. The thread runs the built-in profile-builder agent — a hidden, non-listed agent that is not itself an editable profile in the gallery.
- **Target binding.** The thread's `ThreadConfiguration.agentBuilderTargetId` (with `agentBuilderTargetSource`) names the profile the conversation edits. A thread without this binding is not a builder thread and exposes no builder tools.
- **Working draft.** The builder edits a **thread-scoped working draft** held server-side, seeded from the target profile's Markdown (empty for a new agent). The draft is the agent's authoritative state for the session: it is injected into prompt composition (Section 7) as a cache-stable context section and synchronized with clients through `agent/profiles/builderDraft/read` and `agent/profiles/builderDraft/update`. The draft is not a persisted profile until it is created or saved (see 12A.4).
- **Conversation lifecycle.** A client that renders the conversational builder from a `thread/subscribe` stream must establish that subscription (or an equivalent event-capture path) before sending the first `turn/start` for the builder thread, matching the ordinary run/welcome flow. Before each builder message, the client must flush any pending `agent/profiles/builderDraft/update` so the server-side working draft and the turn prompt are coherent. The builder does not introduce a privileged streaming path; it uses the ordinary thread/turn notification contract.

### 12A.2 Builder tools

The profile-builder agent is given fine-grained, model-visible tools — each mutates exactly one field of the working draft. They are registered **only when the thread is a builder thread** (target binding present) and never appear on ordinary threads.

| Tool | Effect |
|------|--------|
| `SetAgentName(name)` | Set the profile name (the id on save). |
| `SetAgentDescription(description)` | Set the one-line description. |
| `SetAgentInstructions(text)` / `AppendAgentInstructions(text)` | Replace or append the Markdown role body. |
| `AddAgentTools(names[])` / `RemoveAgentTools(names[])` | Add/remove built-in tools in `tools.allow`. |
| `SetAgentToolControl(value)` | Set `tools.agentControl` (`full` / `disabled` / `allowList`). |
| `AddAgentSkills(names[])` / `RemoveAgentSkills(names[])` | Add/remove `skills.preload`. |
| `AddAgentMcpServers(names[])` / `RemoveAgentMcpServers(names[])` | Add/remove `mcp.servers`. |
| `SetAgentProviderPreference(...)` | Set the fixed `providerPreference` atomically. The input contains provider, model, reasoning enabled/effort, speed, and context-window mode; it does not expose reasoning output. |
| `ClearAgentProviderPreference()` | Remove `providerPreference` so the profile inherits model settings. |
| `SetAgentApproval(policy?, requireApprovalOutsideWorkspace?)` | Set the approval policy fields. |

Every tool validates names against any live catalogs available to the builder runtime — built-in tools via the tool catalog (AppServer protocol Section 18A), skills via the skills loader, MCP servers via the configured MCP manager. When a catalog is available, unknown values are rejected with a diagnostic the agent can correct. When a catalog is not available in the host context, the builder preserves the requested names and relies on normal profile validation/refresh diagnostics to surface unresolved references. Each successful tool call leaves the working draft valid per the normal profile validation rules (Section 3).

Tool results are **fine-grained change descriptors, not the whole document**: each returns `{ ok, field, change }`, where `field` is the changed field path (for example `name`, `instructions`, `tools.allow`, `skills.preload`, `mcp.servers`, `providerPreference`, `approval`) and `change` carries the operation (`set` / `add` / `remove` / `append`) with the scalar value or the added/removed/rejected items. `SetAgentProviderPreference` carries the complete atomic preference in `change.providerPreference`; `ClearAgentProviderPreference` carries `change.op = "remove"`. Rejections return `{ ok: false, field, error }`. The authoritative full draft is the server-side working draft (12A.1), injected into prompt composition (12A.3) rather than echoed per call. Clients read the descriptors from the normal tool-call stream to drive the per-field cursor highlight and apply the same single-field change to their local document — no additional request method or notification is introduced.

### 12A.3 Catalog and schema context

Prompt composition for a builder thread additionally injects, through the normal thread-system-prompt context path (Section 7): (a) the Agent Profile frontmatter schema and field semantics, (b) the working-draft snapshot, and (c) the built-in tool catalog — so the agent proposes only valid tool names. The builder draft is a guided-edit subset of the full profile schema and uses the same atomic `providerPreference` shape as persisted profiles. The saved profile is still parsed by the normal profile parser, which owns the complete schema and final diagnostics. Skill and MCP server names are validated against the live catalogs at tool-call time when those catalogs are available (12A.2) rather than enumerated in the prompt. This section is keyed by a constant context page (cache-stable for prompt caching): it snapshots the draft once per thread after each compaction, with later field edits carried by the conversation's own tool-call history. It carries no user-secret data.

### 12A.4 Creation, persistence, and concurrency

- **New agent.** The working draft is not persisted until the user explicitly **creates** it (the client's Create action upserts through the management API, Section 9). Before creation the conversation and the draft are transient; abandoning the builder discards them.
- **Existing agent.** A builder thread bound to an already-persisted profile flushes draft changes back through the management API (debounced auto-save). The profile file's last-write time is surfaced (`updatedAt`) for an "updated …" indicator.
- **Manual edits.** A client may also hand-edit the same draft through the structured editor; manual edits are debounced to `agent/profiles/builderDraft/update`, and clients flush pending draft sync before sending a builder message. Builder tools mutate the same server-side draft. While a builder turn is running, clients should present the document as agent-controlled (non-interactive) to avoid concurrent field conflicts; manual editing resumes when the turn completes.
- **No privileged path.** Creating or saving a profile from the builder uses the same validation, source, policy, and stale-thread refresh rules as Sections 5 and 9 — the builder is not a privileged write path.

---

## 13. Acceptance

The Agent Profiles system is complete when:

- profile-backed threads can be created from Markdown profiles,
- resolved profile provenance and policy persist with the thread,
- profile role instructions appear through the normal prompt-composition path,
- denied capabilities are hidden and rejected at invocation,
- profile CRUD and validation are available through the management API,
- refresh is explicit and updates stale profile-backed threads predictably,
- Teams mission threads can resolve member profiles while preserving Teams-owned workflow rules,
- diagnostics explain invalid profiles, shadowing, stale threads, locks, and trust restrictions.
