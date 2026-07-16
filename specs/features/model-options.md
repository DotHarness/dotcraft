# DotCraft Model Options Specification

| Field | Value |
|-------|-------|
| **Version** | 0.1.0 |
| **Status** | Living |
| **Date** | 2026-07-12 |
| **Parent Specs** | [Session Core](../architecture/session-core.md), [AppServer Protocol](../protocols/appserver-protocol.md), [Desktop Client](../clients/desktop-client.md) |

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

It does not define visual styling, arbitrary numeric context sizes, transcript rendering of reasoning,
automatic Fast-to-Standard fallback, or raw provider fields as client-facing configuration. AppServer
wire DTO details remain authoritative in the AppServer Protocol.

---

## 2. Common Lifecycle

### 2.1 Server-Authored Capability

`model/list` is the client-facing source of option availability. Each `ModelCatalogItem` may contain
independent `reasoning`, `speed`, and `contextWindow` metadata. Clients must not hardcode provider or
model compatibility rules.

Missing metadata means that the server has no known support for that option. Manual model entry
remains available when upstream listing fails, but the client must not invent unsupported choices.

The server's built-in model metadata is stored in `models.json`. Optional global and workspace
`.craft/models.json` files override it in that order. Model entries may independently declare
`contextWindow` and Fast routing selectors, and matching uses the most specific model prefix or
namespaced suffix that declares the requested capability. Fast selectors constrain normalized
protocols; `fast: null` explicitly disables an inherited Fast declaration. Invalid declarations are
ignored. Provider request adapters and `model/list` must resolve capabilities through the same
merged catalog.

### 2.2 Presets and Thread Snapshots

Model options follow this lifecycle:

1. Workspace configuration supplies the preset used by the Welcome composer and future threads.
2. `thread/start` captures effective provider, model, reasoning, and speed into `ThreadConfiguration`
   unless the request supplies explicit values. It captures workspace MAX only when the resolved model
   supports MAX.
3. A Welcome picker change atomically updates the workspace provider/model preset; an active-thread picker change updates only that thread's complete provider/model snapshot.
4. A change affects future and queued turns; it never changes a running provider request.
5. Forks copy the source thread configuration unless the fork request supplies an override.
6. Existing threads keep their captured values when workspace defaults change.

`thread/config/update` replaces the full `ThreadConfiguration`; clients must preserve unrelated fields.

### 2.3 Reconnect and External Changes

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

The provider-neutral object is:

```json
{
  "enabled": true,
  "effort": "high",
  "output": "full"
}
```

- `enabled=false` represents Off; quick selectors must not encode Off as `effort=none`.
- `effort` supports `none`, `low`, `medium`, `high`, and `extraHigh` on the wire.
- `output` supports `none`, `summary`, and `full`. The quick picker changes effort only.
- Workspace persistence uses `Reasoning.Enabled`, `Reasoning.Effort`, and `Reasoning.Output`.
- Effective reasoning resolves thread, workspace, global, then the built-in disabled default.
- A missing reasoning field on an old thread uses the effective AppConfig for compatibility.

### 3.2 Capability Metadata

`model/list.reasoning` supplies `supportsDisable`, ordered `supportedEfforts`, `defaultEffort`,
`supportedOutputs`, and `defaultOutput`. `defaultEffort` must be one of `supportedEfforts`.

When `supportsDisable=false`, clients must not offer Off as an enabled action. If an explicit effort is
unsupported for a known model, the server rejects it; selecting Default may resolve to the model's
default effort.

The server derives reasoning capability and request shaping from protocol, endpoint, model id, and
`model-thinking-adapters.json`. Matching supports model prefixes and namespaced suffixes.

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
4. For `max`, require an explicit match with `catalogWindow > configuredWindow`, then use
   `catalogWindow` directly.

MAX intentionally bypasses `Compaction.MaxContextWindow`; that cap remains the default-mode guardrail.
The server returns JSON-RPC `InvalidParams` for MAX when the model has no explicit match, only a
fallback match, or no larger catalog window.

### 5.3 Persistence and Metadata

Thread configuration uses `{ "contextWindow": { "mode": "max" } }`. Workspace persistence uses
`Compaction.ContextWindowMode`; `default` or null removes the explicit workspace override. New threads
capture MAX only when their resolved model supports it.

`model/list.contextWindow` supplies `catalogWindow`, `configuredWindow`, `supportsMax`, and
`maxWindow`. `supportsMax` is true only for an explicit match whose catalog window is larger.
`ContextUsageSnapshot.contextWindow` remains the effective denominator after reserve and buffer logic.

---

## 6. Desktop UX

The composer Model picker is the shared entry point:

- Provider opens a submenu of configured providers. Welcome uses the workspace provider and remembered `ProviderModels` entry; an existing thread uses its captured provider.
- Model opens the model submenu and preserves manual fallback behavior.
- Effort exposes only the active model's reasoning choices and keeps the trigger label compact.
- Speed appears only for Fast-capable models and offers Standard and Fast.
- When a Fast-capable active model uses Fast, the picker trigger shows a compact speed indicator and
  the composer mascot adds a quiet looping afterimage independent of Effort and MAX. Standard and
  unsupported models show neither treatment; reduced-motion mode keeps only a static, low-contrast
  afterimage.
- MAX is an advanced switch enabled only when `supportsMax=true`; a captured MAX that later becomes
  unsupported is shown as degraded until changed.

Provider/model changes are one `thread/config/update`. If the target model invalidates reasoning,
Desktop selects its default effort; if it does not support MAX, Desktop clears MAX. Speed preference is
preserved and unsupported Fast continues to run as Standard.

The picker remains available while a turn is running; changes update the thread snapshot for queued and
future turns without changing the active provider request. It is disabled while waiting for approval or
user input, during blocking maintenance, or while a configuration update is being applied. The Welcome
atomically updates `providerId` and `providerModels` and passes the resulting pair to thread creation.
Existing threads never show Default and update only their own snapshot. They load `model/list` for their
captured provider and do not follow later workspace provider changes. Without a remembered model Desktop
chooses the first listed model; if no list is available, it leaves state unchanged and directs the user
to Model Providers settings.

---

## 7. Compatibility and Errors

- Config-file enum values are read case-insensitively; wire DTOs use camelCase strings.
- Invalid explicit AppServer values return protocol validation errors.
- Provider rejection of an advertised option fails through the normal turn error contract.
- Unknown models never receive Fast fields or MAX capability without a catalog match.
- Existing reasoning configurations retain their current `Reasoning` shape.
- Existing threads without Speed use Standard; existing threads without Context Window use default.
- The obsolete workspace root `Model` key is ignored and is neither migrated nor used as a fallback.

---

## 8. Acceptance Criteria

- `model/list` is sufficient for clients to render Reasoning, Speed, and MAX without model hardcoding.
- Workspace presets and active-thread snapshots round-trip through AppServer.
- New threads capture effective reasoning and speed plus supported MAX, and existing threads remain stable after preset changes.
- Provider-specific reasoning and Fast request shapes remain server-owned.
- MAX resolution and validation use explicit server catalog evidence.
- Desktop exposes the three options through one model picker and respects busy state.
- Legacy configurations and threads retain their documented defaults.
