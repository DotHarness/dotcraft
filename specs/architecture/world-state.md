# DotCraft World State

| Field | Value |
|-------|-------|
| **Version** | 0.1.1 |
| **Status** | Draft |
| **Date** | 2026-09-19 |
| **Parent Specs** | [Prompt Composition](prompt-composition.md) |
| **Related Specs** | [Prompt Cache](prompt-cache.md), [Session Core](session-core.md), [.NET Plugins](dotnet-plugins.md) |

Purpose: define the model-visible state DotCraft re-sends only when it changes — what counts as a
section, what a section may compare on, how a change reaches the model, how the baseline survives
resume and compaction, and what a diagnosis of it must show.

---

## 1. Model

World state is the part of the model-visible context that persists across turns. It is delivered as
[thread context items](prompt-composition.md#4b-thread-context-items) and follows that layer's
rules: carrier by protocol, placement before the turn's user message, append on change, identity in
message metadata.

A **section** owns one coherent piece of that state. A section has:

- a **stable id**, persisted in rollouts, which must not change once shipped;
- a **snapshot**, the comparison data that decides whether the model must be told again;
- a **render**, which given what the model was last shown returns the text it is owed now, or
  nothing. It is called only when the snapshot differs from what the model was last shown.

### 1.1 What is a section

State qualifies as a section when it persists across turns and the model must be told when it moves:
the environment the agent runs in, the active mode and the actions that mode allows, an active goal's
identity and status, the availability of a capability such as a remote tool host, and equivalent
state contributed by a plugin.

The following are not sections and stay in the turn's own reminder:

- **Per-message facts.** Who sent this message, which conversation it arrived in, and the context the
  sending client bound to it belong to the message, not to the thread.
- **Counters that move on their own.** Consumed tokens, remaining budget, and elapsed time change
  without anything happening, so a snapshot of them would report a change every turn. They are
  rendered fresh each turn instead.

### 1.2 Snapshot contents

A snapshot carries identity and status, never moving values. Long text is carried as a fingerprint
rather than in full, so the persisted baseline stays small.

A snapshot must not serialize to null, because null marks removal in a merge patch. Null members of
a snapshot object are stripped before comparison, so a snapshot compares equal to the value restored
from a patch. Normalization is the runtime's job, not a section's.

## 2. Previous state

A section renders against one of three answers about what the model was last shown:

| State | Meaning |
|-------|---------|
| `Absent` | No baseline entry, and retained history holds no item for this section. |
| `Unknown` | Retained history holds an item for this section, but its snapshot is unavailable. |
| `Known` | The exact previous snapshot is available. |

`Unknown` is what keeps resume and inheritance correct. A thread whose history visibly carries a
section, but whose baseline cannot say with what value, must neither stay silent nor speak as if for
the first time.

### 2.1 Capability sections

A section describing a capability that can be switched off has three cases:

- never on: say nothing;
- on: render the current state;
- on, then off: render one notice retiring what was said, then say nothing.

A section that carries instructions must mark a re-render as replacing what it said before, so two
copies in history cannot be read as cumulative.

## 3. Baseline

### 3.1 Record

After the turn's fragments are in persisted model history, the runtime records the snapshot the
model was brought to. The first record after a reset is a full snapshot; later records are RFC 7386
merge patches against the previous record. No change records nothing.

The record carries the turn it belongs to. Rollback is append-only, so the turn id is the only way
replay can tell that a record describes context a rolled-back turn removed.

### 3.2 Replay

Replay applies records oldest first: a full snapshot resets the baseline, a patch advances it, and a
patch with no full snapshot to stand on is ignored. A record missing its state or its identifiers is
rejected and replay continues; a malformed record must never make a thread unloadable. Replay stops at the newest surviving compaction
checkpoint, which is what makes compaction a reset.

A record is skipped when its turn did not survive rollback, and when its turn was rebuilt from its
projected items rather than its exact history — such a turn no longer carries the context items the
record claims.

### 3.3 Reset

The baseline is dropped whenever history is replaced, because the items it claims the model is
carrying are gone: compaction of any kind, rollback, a reconciliation that reloads history from
disk, and a replacement that establishes a new checkpoint. The next step then restates every
section.

### 3.4 Inheritance

A fork inherits only the part of its parent's baseline that its own inherited history carries. A
fork that does not carry a section's item must restate that section.

## 4. Cadence

World state is evaluated before every sampling request, not once per turn, so state a tool changed
reaches the model within the same turn.

## 5. Failure behaviour

| Condition | Behaviour |
|-----------|-----------|
| A section fails to snapshot | It contributes no baseline entry and the turn continues. |
| A section fails to render | It contributes no text, the baseline keeps what the model was last told so the next step retries, the turn continues, and the diagnosis names it. |
| A section reports an unusable or duplicate id | It is skipped and the turn continues. |
| Recording the baseline fails | The turn continues; the next record is computed from the last one that succeeded. |

No world-state failure may fail a turn.

## 6. Diagnosis

Every sampling step records one diagnostic, including a step that sent nothing — otherwise "the
model was told nothing" and "the section never ran" cannot be told apart afterwards. The diagnostic
reports which sections were sent, which were unchanged, which were suppressed and why, and where the
baseline came from. It records section ids, never rendered text.

## 7. Conformance

- A first step with no baseline renders every section; the immediately following step with unchanged
  state renders none.
- A section whose snapshot is unchanged renders nothing, including when values it prints but does not
  snapshot have moved.
- A resumed thread restores its baseline from the rollout and does not restate sections its retained
  history still carries.
- Compaction, rollback, and history replacement each cause the next step to restate every section.
- A plugin-contributed section behaves identically to a built-in one and cannot fail the turn.
