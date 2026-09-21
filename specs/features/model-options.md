# DotCraft Model Options Specification

| Field | Value |
|-------|-------|
| **Version** | 0.4.3 |
| **Status** | Living |
| **Date** | 2026-09-15 |
| **Parent Specs** | [Session Core](../architecture/session-core.md), [SubAgent Core](subagents.md), [AppServer Protocol](../protocols/appserver-protocol.md), [Desktop Client](../clients/desktop-client.md), [Dynamic Workflows](dynamic-workflows.md) |

Purpose: define the provider-neutral, model-aware options that control how DotCraft runs a selected
model. Reasoning, inference speed, and context-window mode share one capability, persistence, and
client lifecycle while retaining independent runtime semantics.

---

## 1. Goals and Boundaries

DotCraft exposes model options as server-authored model metadata and thread configuration, not as
client-only state. The composite model picker lets users select a model and adjust its available
options without visiting Settings.

This specification covers:

- reasoning effort and reasoning-output visibility
- Standard and Fast inference speed
- default and MAX context-window modes
- workspace presets, thread snapshots, model capability metadata, and Desktop behavior

It does not define arbitrary numeric context sizes, transcript rendering of reasoning, or raw provider
fields as client-facing configuration. AppServer wire DTO details remain authoritative in the
AppServer Protocol.

---

## 2. Common Lifecycle

### 2.1 Server-Authored Capability

`model/list` is the client-facing source of option availability. Each `ModelCatalogItem` may contain
independent `reasoning`, `speed`, and `contextWindow` metadata. Clients must not hardcode provider or
model compatibility rules.

Missing metadata means that the server has no known support for that option. Manual model entry
remains available when upstream listing fails, but the client must not invent unsupported choices.

The server's built-in model metadata is stored in `models.json`. Its synchronized context-window
entries use the provider-agnostic [models.dev](https://models.dev/) catalog and include models whose
metadata declares tool calling, text output, and a context window of at least 1,000 tokens. The final
segment of each canonical model id becomes a lowercase matching key, and a catalog update must reject
duplicate keys before changing the built-in file. Provider-specific limits remain outside this
provider-agnostic catalog.

Optional global and workspace `.craft/models.json` files override the built-in catalog in that order.
Model entries may independently declare `contextWindow` and Fast routing selectors, and matching uses
the most specific model prefix or namespaced suffix that declares the requested capability. More
specific model keys therefore override family prefixes without changing the matching schema. Fast
selectors constrain normalized protocols; `fast: null` explicitly disables an inherited Fast
declaration. Invalid declarations are ignored. Legacy conservative context entries may remain as
compatibility tombstones for models outside the synchronized set. Provider request adapters and
`model/list` must resolve capabilities through the same merged catalog.

Catalog freshness is server-owned. AppServer serves `model/list` from a provider-identity cache and
keeps returning the last catalog fetched with the same credentials when a refresh fails, so a client
may request a catalog whenever it needs one without re-querying the upstream endpoint.

Desktop caches successful model catalogs by provider for the current connection and workspace.
Reasoning, speed, context-mode, and model preference changes reuse those catalogs. Concurrent loads
for the same provider share a request; switching providers reuses their cached results. Manual refresh
reloads the selected provider. Provider registry or authentication changes invalidate catalogs;
connection and workspace changes discard them and prevent old responses from repopulating the cache.
Changing the workspace-selected provider invalidates only the default-provider alias.

A thread composer resolves its options from thread configuration values. Receiving an unchanged thread
snapshot must not reload the catalog or re-read workspace configuration.

### 2.2 Provider Preferences

The public provider-neutral preset is `ModelPreference`:

```json
{
  "model": "gpt-5.6",
  "reasoning": {
    "enabled": true,
    "effort": "high",
    "output": "full"
  },
  "speed": "fast",
  "contextWindow": {
    "mode": "max"
  }
}
```

MainAgent preferences are stored under `ProviderPreferences[providerId]`. Native SubAgent preferences
are stored under `SubAgent.ProviderPreferences[providerId]`. Provider keys are case-insensitive and
normalized when written. A workspace preference replaces the personal preference for the same provider
as one atomic record; fields within a preference are never merged across scopes. Preferences for other
providers remain inherited.

Missing native SubAgent preferences inherit the parent thread's complete MainAgent preference. For a
fresh or bounded native child, an explicit role model takes precedence over that default and an
authorized invocation-specific model or effort override takes precedence over the role default. The
complete preference is then revalidated against the final model. A native full-history child ignores
these overrides and inherits the parent's complete captured preference. External CLI SubAgents do not
consume native preferences. The complete precedence contract is defined by [SubAgent Core](subagents.md#6-native-model-resolution).

### 2.3 Defaults and Normalization

Creating a preference uses model-catalog capability defaults:

- models that can disable reasoning start at Off
- models that cannot disable reasoning use the catalog default effort and output
- speed uses the catalog default, normally Standard
- context uses Default
- manual models use Off / Full / Standard / Default

Changing models replaces unsupported reasoning selections with the model defaults and clears MAX when
unsupported. Fast remains stored as a user preference, but unsupported models execute as Standard.

### 2.4 Presets and Thread Snapshots

Model options follow this lifecycle:

1. The effective `ProviderPreferences` record supplies the preset used by the Welcome composer and
   future threads.
2. `thread/start` captures effective provider, model, reasoning, speed, and context-window mode into
   `ThreadConfiguration` unless the request supplies explicit values. A configured Provider without a
   saved preference remains usable when the request supplies an explicit model; missing model options
   use capability-safe defaults and explicit options win. Unsupported MAX is normalized to Default
   before capture.
3. A Welcome picker change atomically updates the complete workspace provider preference; an
   active-thread picker change updates only that thread's complete provider/model snapshot.
4. A change affects future and queued turns; it never changes a running provider request.
5. Forks copy the source thread configuration unless the fork request supplies an override.
6. Existing threads keep their captured values when workspace defaults change.

`thread/config/update` replaces the full `ThreadConfiguration`; clients must preserve unrelated fields.

### 2.5 Agent Profile Model Policy

Agent Profiles expose a reduced model-preset contract while runtime threads continue to capture the
complete `ModelPreference`:

- an omitted `providerPreference` captures the complete effective workspace/global provider preference
  when a new thread is created;
- a present `providerPreference` stores provider id, model, reasoning enabled/effort, speed, and
  context-window mode;
- reasoning output visibility is not authorable in a Profile; runtime materialization derives it from
  the selected model's catalog `defaultOutput`;
- an empty or partial `providerPreference` is invalid;
- canonical profiles never merge individual model, reasoning, speed, or context-window fields with a
  workspace preference;
- profile-backed thread creation always persists a normalized complete provider/model/reasoning/speed/
  context-window snapshot;
- explicit thread-level reasoning overlays may still set output visibility and take precedence over
  the catalog default;
- refreshing an existing thread from a profile without `providerPreference` preserves its current
  complete model snapshot, while a present `providerPreference` replaces the complete snapshot.

### 2.6 Reconnect and External Changes

Clients recompute effective model options when:

- `thread/read` or `thread/resume` returns a configuration
- `workspace/configChanged` reports the corresponding workspace option, model, or provider region
- model catalog data reloads for another provider

---

## 3. Reasoning

### 3.1 User Model and Configuration

| UI value | Runtime meaning |
|----------|-----------------|
| `Default` | Remove the local override and inherit the next lower scope. |
| `Off` | Do not request provider reasoning when the model can disable it. |
| `Low` | Request low reasoning effort. |
| `Medium` | Request medium reasoning effort. |
| `High` | Request high reasoning effort. |
| `Extra High` | Request the highest model-supported effort. |
| `Ultra` | Request Extra High provider reasoning and enable proactive Dynamic Workflow orchestration for substantive tasks. |

The provider-neutral object is:

```json
{
  "enabled": true,
  "effort": "high",
  "output": "full"
}
```

- `enabled=false` represents Off; quick selectors must not encode Off as `effort=none`.
- `effort` supports `low`, `medium`, `high`, `extraHigh`, and `ultra` on the wire. `ultra` is a
  DotCraft-owned thread tier; provider adapters map it to the same effective provider effort as
  `extraHigh`.
- `output` supports `none`, `summary`, and `full`. The quick picker changes effort only.
- Preference persistence uses `ModelPreference.reasoning`.
- A new thread always captures the effective preference reasoning.

### 3.2 Capability Metadata

`model/list.reasoning` supplies `supportsDisable`, ordered `supportedEfforts`, `defaultEffort`,
`supportedOutputs`, and `defaultOutput`. `defaultEffort` must be one of `supportedEfforts`.

The server adds `ultra` to `supportedEfforts` only when the model supports `extraHigh` and the Dynamic
Workflow runtime is available. Clients derive availability from this metadata and do not infer it from
the model id. `ultra` is persisted in the existing thread reasoning configuration and does not create
an `AgentMode`.

When `supportsDisable=false`, clients must not offer Off as an enabled action. If an explicit effort is
unsupported for a known model, normalization repairs it to the model's default effort.

The server derives reasoning capability and request shaping from protocol, endpoint, model id, and
`model-thinking-adapters.json`. Matching supports model prefixes and namespaced suffixes.

Ultra remains the DotCraft-owned persisted value. Provider validation and request construction compare
its effective provider value as `extraHigh`; they must not overwrite the thread snapshot with
`extraHigh`, because that would remove orchestration behavior from later turns.

### 3.3 Provider Semantics

For OpenAI Chat Completions and Responses:

- disabled reasoning omits `ChatOptions.Reasoning` and provider-specific thinking patches
- enabled reasoning maps effort and output through `ChatOptions.Reasoning`
- non-standard compatible models use catalog-driven deep-thinking adapters

For Anthropic:

- disabled reasoning omits `thinking` when the model supports disabling it
- enabled reasoning uses the most-specific Anthropic thinking adapter
- adaptive models map to `thinking.type="adaptive"`, output display, and `output_config.effort`
- catalog mappings own provider differences such as `extraHigh` becoming `xhigh` or `max`
- `ultra` first normalizes to `extraHigh`, then uses the same catalog mapping
- models that always reason advertise `supportsDisable=false`

Anthropic-compatible reasoning-history adapters may map historical assistant reasoning to supported
`thinking` block shapes. They are compatibility adapters, not generic unsupported-block filters.

---

## 4. Inference Speed

### 4.1 Model and Persistence

Inference speed is `standard` or `fast`:

- `standard` is the default for configurations and old threads without a speed value
- the Welcome composer writes the workspace preset without creating a thread
- new threads capture that preset; active threads store their own speed snapshot
- a stored Fast preference is preserved on an unsupported model, but request shaping behaves as
  Standard until a supported model becomes active again

### 4.2 Capability and Provider Mapping

`model/list.speed` contains ordered `supportedModes` and `defaultMode`. Desktop shows Speed only when
the active model advertises Fast.

| Protocol | Standard | Fast |
|----------|----------|------|
| `openai-responses` | Omit `service_tier` | Send `service_tier: "priority"` |
| `anthropic` | Omit Fast fields | Send `speed: "fast"` and beta `fast-mode-2026-02-01` |
| `openai-chat-completions` | No change | Unsupported; never send Fast fields |

Catalog-matched Anthropic-protocol models use the Anthropic Fast request shape regardless of endpoint.
Capacity, access, and rate-limit failures remain Fast through existing retries and fail normally;
DotCraft does not silently retry as Standard.

---

## 5. Context Window

### 5.1 Modes

| Mode | Meaning |
|------|---------|
| `default` | Use explicit `Compaction.ContextWindow`, or infer the catalog window and apply `Compaction.MaxContextWindow`. |
| `max` | Use the raw explicit model-catalog context window when it is larger than the configured window. |

Omitted or null thread configuration is `default`. V1 does not expose arbitrary numeric per-thread
context sizes.

### 5.2 Resolution and Validation

Resolution is server-owned:

1. Resolve the thread's effective provider and model.
2. Resolve the raw context catalog entry and whether the match is explicit, prefix, suffix, or fallback.
3. Keep an explicit `Compaction.ContextWindow`; otherwise infer it and apply `Compaction.MaxContextWindow`.
4. Offer `max` only when the catalog produced a model-rule match and
   `catalogWindow > configuredWindow`, then use `catalogWindow` directly. Prefix and namespaced-suffix
   matches count as model-rule matches; the default fallback does not.

MAX intentionally bypasses `Compaction.MaxContextWindow`; that cap remains the default-mode guardrail.
This preserves the existing summary reserve and safety buffer while changing their input window. The
server returns JSON-RPC `InvalidParams` for MAX when the model only has the default fallback or when
the catalog window is not larger than the configured Default window. Preference normalization repairs
unsupported MAX to Default.

### 5.3 Persistence and Metadata

Thread configuration and `ModelPreference` use `{ "contextWindow": { "mode": "max" } }`. New threads
capture the normalized preference mode; unsupported MAX becomes Default.

Desktop persists an explicit `{ "contextWindow": { "mode": "default" } }` when MAX is turned off,
including in new-thread overlays and when switching to a model that does not support MAX.
Existing threads read their captured context-window mode; omitted or null means Default and must not
fall back to workspace preferences. Only untouched new-thread drafts inherit the workspace preference.
Changing reasoning effort, refreshing thread state, or navigating between threads preserves this choice.

`model/list.contextWindow` supplies `catalogWindow`, `configuredWindow`, `supportsMax`, and
`maxWindow`. `supportsMax` is true only for an explicit match whose catalog window is larger.
`ContextUsageSnapshot.contextWindow` remains the effective denominator after reserve and buffer logic.

---

## 6. Desktop UX

The composer Model picker supplies one shared panel implementation. The trigger names the model and
its reasoning level, with a compact speed indicator when a Fast-capable model uses Fast and a `MAX`
tag when the context window is maximised. The panel opens above the trigger and reads as one decision:

- The headline is the level the active model thinks at, with the `MAX` tag beside it when the context
  window is maximised (warning-coloured while degraded). Beneath the headline the model name is a
  button that leads one level in. A model without reasoning metadata has no level in the panel: its
  name takes the headline's place and the panel has no scale.
- The scale under the headline is a slider with one stop per reasoning effort the active model
  advertises, in catalog order, with an `Off` stop at the near end only when the model supports
  disabling reasoning. Moving the thumb applies the level at once and leaves the panel open. Reaching
  the top stop plays a short arrival, and while the thumb stays there the fill takes on the hue the
  mascot turns at its highest effort, blooming from the thumb's end and blending back into the
  accent toward the far end; the fill never darkens. Reduced-motion mode shows one still frame.
- Fast is a bolt at the panel's left, shown only for Fast-capable models and pressed while Fast is
  active. While Fast is on, the stop dots the fill covers become particles streaming back from the
  thumb along the bar. Fast and the top of the scale arrive and leave over a short fade; no change
  of state switches abruptly. When a Fast-capable active model uses Fast, the composer mascot adds a quiet looping
  afterimage independent of Effort and MAX. Standard and unsupported models show neither treatment;
  reduced-motion mode keeps only a static, low-contrast afterimage.
- Reset is an arrow at the panel's right, shown only while the level, the speed or MAX differs from
  the active model's catalog defaults (its default effort, its default speed mode and the Default
  window). It returns each differing setting to that default.
- One level in, reached from the model name, a menu keeps the earlier rows. Provider opens a submenu
  of configured providers; Welcome uses the workspace provider and remembered `ProviderPreferences`
  entry, and an existing thread uses its captured provider. Model opens the model submenu and
  preserves manual fallback behavior. MAX is a switch enabled only when `supportsMax=true`; a captured
  MAX that later becomes unsupported is shown as degraded until changed. A Back row returns to the
  panel. Escape closes an open submenu, then returns from the menu to the panel, then closes the
  picker.

Provider/model changes are one `thread/config/update`. If the target model invalidates reasoning,
Desktop selects its default effort; if it does not support MAX, Desktop clears MAX. Speed preference is
preserved and unsupported Fast continues to run as Standard. Changes made in quick succession apply one
after another, each on the configuration the previous one wrote.

The picker remains available while a turn is running and while the turn waits for approval or user
input; changes update the thread snapshot for queued and future turns without changing the active
provider request. Whenever the scale is visible it can be moved. The trigger is disabled only while
model controls are unavailable and while a configuration update is being applied; a panel that is already open
stays open through that. The Welcome atomically updates `providerId` and `providerPreferences` and
passes the resulting preference to thread creation.
Existing threads never show Default and update only their own snapshot. They load `model/list` for their
captured provider and do not follow later workspace provider changes. Without a remembered model Desktop
chooses the first listed model; if no list is available, it leaves state unchanged and directs the user
to Model providers settings.

Settings and Setup reuse the same panel, keyboard navigation, portal placement, submenu aim, and
capability handling. Their full-width field wrapper hides the Provider row because those screens
already establish the provider. It does not add alternate rows, icons, dividers, or explanatory
tooltips.

The Settings `Workspace preferences` header owns the refresh action. MainAgent uses one full-width
picker under `Main model`. SubAgent uses the same field under `Subagent model`, with a shared
segmented control in the row offering `Inherit` and `Custom`. Inherit removes the provider's SubAgent
record and disables the field; Custom clones the current MainAgent preference before editing. Setup is
one centered wizard screen, configures MainAgent only, and leaves SubAgent inheritance untouched.

Each provider row in `Provider list` summarises its remembered preferences as two clauses, `Main` and
`Subagent`, each naming the model and reasoning effort with `MAX` appended when the context window is
maximised. Fast inference reads as a bolt before the model name; Standard adds nothing. A provider
without a SubAgent record shows `Inherits main model` as its `Subagent` value.

---

## 7. Compatibility and Errors

- Config-file enum values are read case-insensitively; wire DTOs use camelCase strings.
- Invalid explicit AppServer values return protocol validation errors.
- Provider rejection of an advertised option fails through the normal turn error contract.
- Unknown models never receive Fast fields or MAX capability without a catalog match.
- Existing threads without Speed use Standard; existing threads without Context Window use default.
- Obsolete preference keys are ignored and are neither migrated nor used as fallback.

---

## 8. Acceptance Criteria

- `model/list` is sufficient for clients to render Reasoning, Speed, and MAX without model hardcoding.
- Workspace presets and active-thread snapshots round-trip through AppServer.
- Workspace-over-personal resolution replaces one provider preference atomically.
- Native SubAgent inheritance and explicit overrides preserve the complete preference, while role-model
  and external-runtime precedence remain deterministic.
- New threads capture effective reasoning and speed plus supported MAX, and existing threads remain stable after preset changes.
- Provider-specific reasoning and Fast request shapes remain server-owned.
- MAX resolution and validation use explicit server catalog evidence.
- Desktop exposes the three options through one model picker and respects busy state.
- Threads without Speed or Context Window retain the documented Standard and Default runtime behavior.
