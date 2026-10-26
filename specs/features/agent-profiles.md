# DotCraft Agent Profiles Specification

| Field | Value |
|-------|-------|
| **Version** | 0.9.1 |
| **Status** | Draft |
| **Date** | 2026-10-19 |
| **Related Specs** | [Prompt Composition](../architecture/prompt-composition.md), [Session Core](../architecture/session-core.md), [Memory](memory.md) |

Purpose: define Agent Profiles as reusable agent configuration templates. A profile gives a thread a role, default runtime preferences, and enforceable capability policy without replacing DotCraft's generated base instructions.

## 1. Scope

Agent Profiles define:

- Markdown profile documents with YAML frontmatter and a Markdown role body.
- Deterministic source resolution across built-in, plugin, user, workspace, and managed profiles.
- Compilation into a persisted thread configuration snapshot.
- Profile-backed ordinary threads and unattended tasks.
- Management APIs for profile authoring, validation, readback, and explicit thread refresh.
- Policy rules for tools, MCP, plugins/apps, skills, and approvals.

Out of scope:

- Full-prompt replacement for ordinary profile-backed threads.
- Automatic mutation of existing threads when profile files change.
- Profile inheritance or field-by-field merging between same-id profiles.

## 2. Principles

- The thread configuration snapshot is the runtime contract. Agents, tool filtering, approvals, and prompt rendering read the resolved thread configuration, not the profile document.
- Profile prompt text is not security. Security-sensitive behavior must be enforced by runtime policy.
- Profile resolution is deterministic. Higher-priority sources shadow lower-priority profiles as whole documents.
- Existing threads are stable. A profile file change affects new threads only, unless a client explicitly refreshes an existing profile-backed thread.
- Profiles specialize the generated DotCraft prompt through role instructions. They must not replace the base prompt.
- Model policy is atomic. A profile either inherits the complete effective provider preference or pins one complete model preset; it never merges individual model-option fields with workspace defaults.
- Overlays are narrow. Profile-backed thread creation may override ordinary runtime model choices, but must not use request-time overlays to broaden capabilities.
- Runtime-owned capabilities remain outside profile policy. Profiles specialize a thread without taking ownership of scheduling or runtime lifecycle.
- Visual identity is client-derived from the profile name. It is not authored in profile frontmatter, stored by the profile service, or exposed by the AppServer profile contract.

## 3. Profile Document

A profile is a Markdown file with YAML frontmatter. The frontmatter declares structured configuration. The Markdown body declares role instructions.

Minimal shape:

```markdown
---
name: reviewer
description: Read-only reviewer focused on correctness, risks, and tests.
tools:
  allow: [ReadFile, FindFiles, GrepFiles, LSP, WebSearch, WebFetch]
  agentControl: disabled
permissions:
  approvalPolicy: prompt
---

You are the Reviewer. Focus on correctness, risk, and missing tests.
Do not edit files unless the mission explicitly reassigns you.
```

Required fields:

| Field | Meaning |
|-------|---------|
| `name` | Canonical Unicode name used for display, lookup, and avatars. |
| `description` | Human-readable purpose and selection hint. |

Supported frontmatter groups:

| Field | Meaning |
|-------|---------|
| `providerPreference` | Optional fixed model preset for new profile-backed threads: `providerId`, `model`, reasoning enabled/effort, speed. Reasoning output visibility comes from the model catalog at runtime. |
| `mode` | Other runtime defaults for new profile-backed threads. |
| `tools` | Built-in, dynamic, deferred, and agent-control tool policy. |
| `mcp` | MCP server and MCP tool policy. |
| `plugins` | Plugin/app capability policy. |
| `skills` | Which Skills the agent may use (`allow`, `deny`) and skill-management policy (`allowManage`). |
| `permissions` | Approval and workspace-boundary policy. |
| `memory` | `shared` (default) uses the workspace memory store; `own` gives the profile a store of its own. See [Memory](memory.md#4-memory-scope). |
| `locked` | Managed-source governance constraints only. |

Validation rules:

- `name` is trimmed and NFC-normalized, preserves case and internal spaces, contains 1–240 Unicode scalar values, and contains no control characters. Equality is ordinal after normalization.
- The document name, not its filename, identifies a profile. New writable files use lowercase SHA-256 of the UTF-8 canonical name plus `.md`; hand-written filenames remain valid and are preserved on updates.
- Duplicate names within one source are invalid and block mutation. Cross-source shadowing remains whole-document priority resolution.
- `id` and `agentProfileId` reference this same canonical name; there is no independent display-name or slug field.
- Upsert accepts optional `previousName` for a same-source rename. It validates the new document and target-name availability before replacing the old document. Failure preserves the original. Existing thread snapshots retain their original provenance; renaming does not rewrite historical threads.
- Unknown fields are rejected unless explicitly marked experimental.
- The Markdown body maps to role instructions, not a base-prompt replacement.
- An omitted `providerPreference` captures the complete effective workspace/global provider preference when a new thread is created. A present one requires non-empty `providerId`, `model`, `reasoning` (only `enabled` and `effort`) and `speed`; an empty or partial preference is invalid.
- A profile with a fixed provider preference may remain structurally valid when its provider or model is unavailable in the current workspace. Management APIs report that runtime diagnostic, and thread creation fails without changing thread state until the provider becomes runnable.
- Non-managed profiles must not declare managed-only locks.
- `tools.allow` and `tools.deny` are mutually exclusive in an Agent Profile.
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
```

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

Managed policy may constrain lower-priority profiles through explicit locks. Without such a lock, priority is whole-document shadowing.

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

- refreshing from a profile without `providerPreference` preserves the existing thread's complete provider/model/reasoning/speed snapshot;
- refreshing from a profile with `providerPreference` replaces that complete snapshot atomically;
- changing workspace/global provider preferences never mutates an existing thread;
- returning an existing thread to current workspace model defaults is a separate explicit model-reset operation, not a side effect of profile refresh.

If the profile is missing, invalid, blocked by source restrictions, or incompatible with requested overlays, thread creation or refresh fails before changing thread state.

## 6. Effective Thread Contract

A profile-backed thread stores:

| Field | Meaning |
|-------|---------|
| `agentProfileId` | Requested profile id. |
| `agentProfileSource` | Source selected during resolution. |
| `agentProfileFingerprint` | Stable fingerprint for stale-thread detection. |
| `roleInstructions` | Profile body, optionally followed by first-party runtime role text. |
| Model snapshot | Complete provider, model, reasoning, and speed values resolved at thread creation. |
| Runtime defaults | Mode and prompt profile when set by the profile. |
| Capability policies | Tool, MCP, plugin/app, skills, approval, and workspace-boundary policy. |
| Memory scope | The profile's own scope when it declares `memory: own` and the thread creator sets none. |

Policy semantics:

- Omitted policy means the existing runtime default applies.
- Omitting both `tools.allow` and `tools.deny` allows all ordinary profile-managed tools.
- An explicit empty `tools.allow` allows no ordinary tools; a non-empty list allows only the named tools.
- A non-empty `tools.deny` allows ordinary tools except the named tools.
- `tools.allow` and `tools.deny` name ordinary profile-managed tools and never reach MCP tools, so a host-attached MCP server on one thread is not hidden by a tool name list. MCP tools answer to `mcp.servers` and `mcp.tools`, which select by server and namespaced tool selector and accept wildcards.
- Outside the tool policy, omitted `allow` means no allow-list is applied and empty `allow` allows no profile-managed capability for that policy dimension.
- Empty or omitted `deny` means no deny-list is applied.
- Deny wins over allow.
- Exact-name tool filters from other configuration compose with structured tool policy. The effective surface is the intersection of all allows after all denies are applied.
- `permissions.approvalPolicy` is `default`, `prompt`, `autoApprove`, or `deny`. `default` follows the workspace default policy, `prompt` always asks, `autoApprove` accepts, and `deny` never asks: the operation is rejected and that rejection is returned to the calling tool, so the turn continues.
- A profile written by the conversational builder carries `prompt` or `autoApprove`. `default` and `deny` stay legal on read for hand-written and automation-owned profiles.
- `permissions.requireApprovalOutsideWorkspace` stays a legal profile key for hand-written and automation-owned profiles. The conversational builder neither presents nor writes it and preserves an authored value.
- `tools.agentControl` is `full`, `disabled`, or `allowList`, and `allowList` draws its members from `tools.allowedAgentControlTools`. Both remain enforced as written; the conversational builder presents only the choice between `full` and `disabled` and preserves an authored `allowList` until that choice changes.

## 7. Prompt Composition

The profile body supplies the role-instruction layer of [Prompt Composition](../architecture/prompt-composition.md). It must not replace the generated base instructions or raise the authority of application context. Runtime and builder context use that specification's stable and turn-scoped boundaries.

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

Runtime-managed capabilities are contributed by the runtime that owns the thread and do not participate in Agent Profile tool allow/deny policy. Profiles cannot enable, disable, or impersonate those registrations.

A capability the policy keeps out of the model's tool list is recorded on the thread's trace with the tool's model-visible name, its namespace, its usage source, and the refusal that hid it. A withheld tool produces no call and no result, so the trace is the only place that decision is written down.

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
| `agent/profiles/export/plan` | List what an export of a user or workspace profile can carry. |
| `agent/profiles/export/read` | Write the Agent package for the chosen skills and plugins and read it in chunks. |
| `agent/profiles/import/upload` | Upload an Agent package or Profile document in chunks and return its preview. |
| `agent/profiles/import/commit` | Install the chosen packages and create the profile from an uploaded import. |
| `agent/profiles/import/discard` | Discard an uploaded import. |

The export and import methods are defined by the Agent package feature.

Rules:

- Writes validate before changing storage.
- Upsert requires the requested id to match the frontmatter `name`.
- Read-only sources return clear errors for write attempts.
- List/read responses include diagnostics instead of forcing clients to parse Markdown.
- Error codes are stable; user-facing localization belongs to clients.

## 10. Governance And Diagnostics

Governance features:

- Managed profiles have highest priority and may lock fields.
- Plugin profile packs are read-only and subject to trust-boundary restrictions.
- Managed locks may force, cap, deny, or require values for specific profile fields.
- Lower-priority profiles conflicting with locks are invalid or compiled under the lock, depending on the declared managed policy.

Diagnostics cover:

- profile authoring errors,
- source shadowing,
- read-only/write restrictions,
- high-risk declarations stripped by trust policy,
- locked-field conflicts,
- effective policy summaries,
- existing threads using stale fingerprints,
- refresh success or failure.

Audit records exist for profile writes, removals, and explicit thread refresh attempts. Audit entries use stable machine-readable codes and structured fields.

## 11. Authoring And UX Process

Profile authoring tools validate Markdown before writing, show the effective source and policy summary, distinguish role text from enforced policy, warn that edits affect existing threads only after refresh, and expose stale-thread hints and explicit refresh. Profile generation tools use the same validation, source, policy, and refresh rules.

## 12. Conversational Builder

The conversational builder lets a user create or refine an Agent Profile by talking to a dedicated **profile-builder agent** alongside the structured editor. It is a normal Session Core thread using the standard turn, streaming, and tool-call machinery. Its only dedicated AppServer methods are the working-draft synchronization methods.

### 12.1 Model

- **Builder thread.** A profile-builder conversation runs in an ordinary thread with internal visibility and a builder target binding, excluded from ordinary thread listings by that visibility. It runs the built-in, hidden profile-builder agent, which is not an editable profile.
- **Target binding.** The thread configuration's `agentBuilderTargetId` (with `agentBuilderTargetSource`) names the profile the conversation edits. A thread without this binding is not a builder thread and exposes no builder tools.
- **Working draft.** The builder edits a **thread-scoped working draft** held server-side, seeded from the target profile's Markdown (empty for a new agent). The draft is the agent's authoritative state for the session: it is injected into prompt composition (Section 7) as a cache-stable context section and synchronized with clients through `agent/profiles/builderDraft/read` and `agent/profiles/builderDraft/update`. The draft is not a persisted profile until it is created or saved (§12.4).
- **Conversation lifecycle.** A client rendering the builder from `thread/subscribe` subscribes before the first `turn/start`, and flushes any pending `agent/profiles/builderDraft/update` before each builder message so the working draft and turn prompt are coherent. The builder uses the ordinary thread/turn notification contract.

### 12.2 Builder tools

The profile-builder agent is given fine-grained, model-visible tools — each mutates exactly one field of the working draft. They are registered **only when the thread is a builder thread** (target binding present) and never appear on ordinary threads.

| Tool | Effect |
|------|--------|
| `SetAgentName(name)` | Set the profile name (the id on save). |
| `SetAgentDescription(description)` | Set the one-line description. |
| `SetAgentInstructions(text)` / `AppendAgentInstructions(text)` | Replace or append the Markdown role body. |
| `SetAgentToolPolicy(mode, names[])` | Atomically set the built-in tool policy. `mode` is `all`, `allowList`, or `denyList`; `names` is empty for `all` and is the complete active list otherwise. |
| `SetAgentToolControl(value)` | Set `tools.agentControl` (`full` / `disabled` / `allowList`). |
| `AddAgentSkills(names[])` / `RemoveAgentSkills(names[])` | Add/remove names in `skills.allow`, the Skills the agent may use. Removing the last name removes the allow-list, so every enabled Skill is available again. |
| `AddAgentMcpServers(names[])` / `RemoveAgentMcpServers(names[])` | Add/remove `mcp.servers`. |
| `SetAgentProviderPreference(...)` | Set the fixed `providerPreference` atomically. The input contains provider, model, reasoning enabled/effort, speed; it does not expose reasoning output. |
| `ClearAgentProviderPreference()` | Remove `providerPreference` so the profile inherits model settings. |
| `SetAgentApproval(policy)` | Set `permissions.approvalPolicy` to `prompt` or `autoApprove`. The builder does not author the unattended values or the workspace-boundary permission. |

Every tool validates names against the live built-in tool, skill, and MCP server catalogs when available, rejecting unknown values with a diagnostic the agent can correct; an unknown built-in tool diagnostic also lists the available built-in tool names. Without a catalog, requested names are preserved and normal profile validation and refresh diagnostics surface unresolved references. Each successful tool call leaves the working draft valid per the normal profile validation rules (Section 3).

Tool results are **fine-grained change descriptors, not the whole document**: each returns `{ ok, field, change }`, where `field` is the changed field path (for example `name`, `instructions`, `tools.policy`, `skills.allow`, `mcp.servers`, `providerPreference`, `approval`) and `change` carries the operation (`set` / `add` / `remove` / `append`) with the scalar value or the added/removed/rejected items. `SetAgentToolPolicy` returns the complete derived mode and active list. `SetAgentProviderPreference` carries the complete atomic preference in `change.providerPreference`; `ClearAgentProviderPreference` carries `change.op = "remove"`. Rejections return `{ ok: false, field, error }`. The authoritative full draft is the server-side working draft (§12.1), injected into prompt composition (§12.3) rather than echoed per call. Clients apply the descriptors from the normal tool-call stream to their local document.

Every builder tool carries the trusted presentation id `core.agent-builder` with `options.field` set to the field path it edits. Desktop renders these calls as profile-change rows; other clients keep the text descriptor.

### 12.3 Catalog and schema context

Prompt composition for a builder thread additionally injects, through the normal thread-system-prompt context path (Section 7): (a) the Agent Profile frontmatter schema and field semantics, including the three mutually exclusive built-in tool modes and explicit-empty allow-list behavior, (b) the working-draft snapshot, and (c) the built-in tool catalog — so the agent proposes only valid tool names. The builder draft is a guided-edit subset of the full profile schema and uses the same atomic `providerPreference` shape as persisted profiles. The saved profile is still parsed by the normal profile parser, which owns the complete schema and final diagnostics. Skill and MCP server names are validated at tool-call time (§12.2) rather than enumerated in the prompt. This section is a cache-stable context page that snapshots the draft once per thread and after each compaction; later edits are carried by the conversation's tool-call history. It carries no user-secret data.

### 12.4 Creation, persistence, and concurrency

- **New agent.** The working draft is not persisted until the user explicitly **creates** it (the client's Create action upserts through the management API, Section 9). Before creation the conversation and the draft are transient; abandoning the builder discards them.
- **Existing agent.** A builder thread bound to a persisted profile auto-saves draft changes through the management API. The profile's last-write time is surfaced as `updatedAt`.
- **Manual edits.** The structured editor edits the same server-side draft through `agent/profiles/builderDraft/update`. While a builder turn runs, clients present the document as agent-controlled to avoid concurrent field conflicts.
- **No privileged path.** Creating or saving a profile from the builder uses the same validation, source, policy, and stale-thread refresh rules as Sections 5 and 9 — the builder is not a privileged write path.

### 12.5 Desktop creation

New agent opens the profile editor alongside the builder conversation. Empty conversations offer creation prompts. Unnamed drafts use the default avatar; named drafts use the name-derived avatar.
