# DotCraft Model Options Specification

| Field | Value |
|-------|-------|
| **Version** | 0.9.1 |
| **Status** | Living |
| **Date** | 2026-10-19 |
| **Parent Specs** | [Session Core](../architecture/session-core.md) |

Purpose: define the provider-neutral, model-aware options that control how DotCraft runs a selected
model. Reasoning and inference speed are persisted selections; model capacity is resolved from
the merged model catalog for each operation.

## 1. Goals and Boundaries

DotCraft exposes model options as server-authored model metadata and thread configuration, not as
client-only state. The composite model picker lets users select a model and adjust its available
options without visiting Settings.

This specification covers:

- reasoning effort and reasoning-output visibility
- Standard and Fast inference speed
- model-catalog context capacity
- workspace presets, thread snapshots, model capability metadata, and Desktop behavior

It does not define arbitrary numeric context sizes, transcript rendering of reasoning, or raw provider
fields as client-facing configuration. AppServer wire DTO details remain authoritative in the
AppServer Protocol.

## 2. Common Lifecycle

### 2.1 Server-Authored Capability

`model/list` is the client-facing source of option availability. Each catalog item may contain
independent `reasoning`, `speed`, and `contextWindow` metadata. Clients do not hardcode provider or
model compatibility rules.

Missing metadata means that the server has no known support for that option. Manual model entry
remains available when upstream listing fails, but the client never invents unsupported choices.

The server's built-in model metadata is stored in `models.json`. Its synchronized context-window
entries use the provider-agnostic [models.dev](https://models.dev/) catalog and include models whose
metadata declares tool calling, text output, and a context window of at least 1,000 tokens. The final
segment of each canonical model id becomes a lowercase matching key, and duplicate keys are rejected. Provider-specific limits remain outside this
provider-agnostic catalog.

Optional global and workspace `.craft/models.json` files override the built-in catalog in that order.
Model entries may independently declare `contextWindow` and Fast routing selectors, and matching uses
the most specific model prefix or namespaced suffix that declares the requested capability. More
specific model keys therefore override family prefixes without changing the matching schema. Fast
selectors constrain normalized protocols; `fast: null` explicitly disables an inherited Fast
declaration. Invalid declarations are ignored. Provider request adapters and
`model/list` resolve capabilities through the same merged catalog.

Catalog freshness is server-owned. AppServer serves `model/list` from a provider-identity cache and
keeps returning the last catalog fetched with the same credentials when a refresh fails, so a client
may request a catalog whenever it needs one without re-querying the upstream endpoint.

Desktop caches successful model catalogs by provider for the current connection and workspace, and
option changes reuse them. Manual refresh reloads the selected provider. Provider registry or
authentication changes invalidate catalogs; connection and workspace changes discard them, and
responses from a previous connection or workspace never repopulate the cache.

A thread composer resolves its options from thread configuration values. Receiving an unchanged thread
snapshot does not reload the catalog or re-read workspace configuration.

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
  "speed": "fast"
}
```

MainAgent preferences are stored under `ProviderPreferences[providerId]`. Native SubAgent preferences
are stored under `SubAgent.ProviderPreferences[providerId]`. Provider keys are case-insensitive and
normalized when written. A workspace preference replaces the personal preference for the same provider
as one atomic record; fields within a preference are never merged across scopes. Preferences for other
providers remain inherited.

Missing native SubAgent preferences inherit the parent thread's complete MainAgent preference.
Consumer-specific role and invocation precedence normalize the resulting complete preference
against the final model. External CLI runtimes do not consume native preferences.

### 2.3 Defaults and Normalization

Creating a preference uses model-catalog capability defaults:

- models that can disable reasoning start at Off
- models that cannot disable reasoning use the catalog default effort and output
- speed uses the catalog default, normally Standard
- manual models use Off / Full / Standard

Changing models replaces unsupported reasoning selections with the model defaults. Fast remains stored as a user preference, but unsupported models execute as Standard.

### 2.4 Presets and Thread Snapshots

Model options follow this lifecycle:

1. The effective `ProviderPreferences` record supplies the preset used by the Welcome composer and
   future threads.
2. `thread/start` captures effective provider, model, reasoning and speed into
   `ThreadConfiguration` unless the request supplies explicit values. A configured Provider without a
   saved preference remains usable when the request supplies an explicit model; missing model options
   use capability-safe defaults and explicit options win.
3. A Welcome picker change atomically updates the complete workspace provider preference; an
   active-thread picker change updates only that thread's complete provider/model snapshot.
4. A change affects future and queued turns; it never changes a running provider request.
5. Forks copy the source thread configuration unless the fork request supplies an override.
6. Existing threads keep their captured values when workspace defaults change.

`thread/config/update` replaces the full `ThreadConfiguration`; clients preserve unrelated fields.

### 2.5 Agent Profile Model Policy

Profile consumers materialize a complete `ModelPreference` before starting a thread. Profile authoring, inheritance and refresh policy belong to the profile owner; they cannot bypass catalog normalization or mutate a running request.

### 2.6 Reconnect and External Changes

Clients recompute effective model options when:

- `thread/read` or `thread/resume` returns a configuration
- `config/changed` lists a `ProviderId` or `ProviderPreferences` key path, or the `providers` domain tag
- model catalog data reloads for another provider

## 3. Reasoning

### 3.1 User Model and Configuration

| UI value | Runtime meaning |
|----------|-----------------|
| `Default` | Remove the local override and inherit the next lower scope. |
| `Off` | Do not request provider reasoning when the model can disable it. |
| `Low` | Request low reasoning effort. |
| `Medium` | Request medium reasoning effort. |
| `High` | Request high reasoning effort. |
| `Extra High` | Request xhigh reasoning effort. |
| `Max` | Request max reasoning effort. |
| `Ultra` | Request Max provider reasoning and enable proactive Dynamic Workflow orchestration for substantive tasks. |

The provider-neutral object is:

```json
{
  "enabled": true,
  "effort": "high",
  "output": "full"
}
```

- `enabled=false` represents Off; Off is never encoded as `effort=none`.
- `effort` supports `low`, `medium`, `high`, `extraHigh`, `max`, and `ultra` on the wire. `ultra` is a
  DotCraft-owned thread tier; provider adapters map it to the same effective provider effort as
  `max`.
- `output` supports `none`, `summary`, and `full`. The quick picker changes effort only.
- Preference persistence uses `ModelPreference.reasoning`.
- A new thread always captures the effective preference reasoning.

### 3.2 Capability Metadata

`model/list.reasoning` supplies `supportsDisable`, ordered `supportedEfforts`, `defaultEffort`,
`supportedOutputs`, and `defaultOutput`. `defaultEffort` must be one of `supportedEfforts`.

The server adds `ultra` to `supportedEfforts` only when the model supports `max` and the Dynamic
Workflow runtime is available. Clients derive availability from this metadata and do not infer it from
the model id. `ultra` is persisted in the thread reasoning configuration and does not create an
agent mode.

When `supportsDisable=false`, clients do not offer Off as an enabled action. If an explicit effort is
unsupported for a known model, normalization repairs it to the model's default effort.

The server derives reasoning capability and request shaping from protocol, endpoint, model id, and
`model-thinking-adapters.json`. Matching supports model prefixes and namespaced suffixes.

Ultra remains the persisted value. Provider validation and request construction use its effective
provider value `max` without overwriting the thread snapshot, which would remove orchestration
behavior from later turns.

### 3.3 Provider Semantics

For OpenAI Chat Completions and Responses:

- disabled reasoning omits `ChatOptions.Reasoning` and provider-specific thinking patches
- ordinary enabled efforts and output use `ChatOptions.Reasoning`
- Max is mapped to native `max` by the provider adapter; Ultra resolves to Max before request construction
- internal reasoning metadata is consumed before sending the native request
- non-standard compatible models use catalog-driven deep-thinking adapters

For Anthropic:

- disabled reasoning omits `thinking` when the model supports disabling it
- enabled reasoning uses the most-specific Anthropic thinking adapter
- adaptive models map to `thinking.type="adaptive"`, output display, and `output_config.effort`
- `extraHigh` maps to `xhigh`; `max` and the effective Ultra effort map to `max`
- models that always reason advertise `supportsDisable=false`

Anthropic-compatible reasoning-history adapters may map historical assistant reasoning to supported
`thinking` block shapes; they are not generic unsupported-block filters.

## 4. Inference Speed

### 4.1 Model and Persistence

Inference speed is `standard` or `fast`:

- `standard` is the default for configurations and threads without a speed value
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
Capacity, access, and rate-limit failures remain Fast through retries and fail normally;
DotCraft does not silently retry as Standard.

## 5. Context Window

The effective model's merged `models.json` catalog is the sole source of context capacity.
Built-in entries are overridden by global and then workspace catalogs using the model matching
rules of §2.1. Unknown models use the catalog default, falling back to 256,000 tokens.

`Compaction.MaxContextWindow` sets a client-side token budget in global or workspace config.
Its default is `-1`: omitted or `-1` uses the catalog window; a positive value limits the runtime
window to the smaller of that value and the catalog window. Workspace values override global values,
including an explicit `-1` that restores the catalog window. This setting is independent of reasoning.

Compaction receives the resulting window as runtime data and applies summary reserves, safety buffers,
and compaction thresholds. Model changes affect subsequent requests and maintenance work;
running operations keep their captured configuration.

`model/list.contextWindow` is the resolved numeric capacity. The context usage snapshot's
`contextWindow` is the effective denominator after the client budget, reserve, and buffer rules. Model catalog
metadata always reports raw capacity independently of this budget.

## 6. Desktop UX

The composer Model picker is one shared panel. Its trigger names the model and its reasoning level,
with a speed indicator when a Fast-capable model uses Fast. The panel reads as one decision:

- The headline is the active model's reasoning level, with the model name beneath it leading one
  level in. A model without reasoning metadata has no level: its name takes the headline's place and
  the panel has no scale.
- The scale is a slider with one stop per effort the active model advertises, in catalog order, with
  an `Off` stop at the near end only when the model supports disabling reasoning. Moving the thumb
  applies the level at once and leaves the panel open. The top stop and Fast have distinct visual
  treatments; reduced-motion mode shows them still.
- A Fast toggle appears only for Fast-capable models and is pressed while Fast is active.
- Reset appears only while the level or speed differs from the active model's catalog defaults and
  returns each differing setting to its default.
- One level in, a menu offers Provider (configured providers; Welcome uses the workspace provider and
  its remembered `ProviderPreferences` entry, an existing thread its captured provider) and Model
  (the model submenu, with manual fallback), and returns to the panel. Escape closes an open
  submenu, then returns to the panel, then closes the picker.

Provider/model changes are one `thread/config/update`. If the target model invalidates reasoning,
Desktop selects its default effort; the speed preference is preserved and unsupported Fast runs as
Standard. Changes made in quick succession apply one after another, each on the configuration the
previous one wrote.

The picker remains available while a turn is running or waiting for approval or user input; changes
update the thread snapshot for queued and future turns without changing the active provider request.
The trigger is disabled only while model controls are unavailable or a configuration update is being
applied; an open panel stays open through that. The Welcome composer atomically updates `providerId`
and `providerPreferences` and passes the resulting preference to thread creation. Existing threads
never show Default, update only their own snapshot, load `model/list` for their captured provider,
and do not follow later workspace provider changes. Without a remembered model Desktop chooses the
first listed model; if no list is available, it leaves state unchanged and directs the user to the
model provider settings.

Settings and Setup reuse the same panel and capability handling, without the Provider row because
those screens already establish the provider. In Settings, MainAgent and SubAgent each have a model
field; the SubAgent field offers `Inherit`, which removes the provider's SubAgent record, and
`Custom`, which clones the current MainAgent preference before editing. Setup configures MainAgent
only and leaves SubAgent inheritance untouched.

Each provider in the provider list shows its name, whether it can create images, any sign-in
problem such as a missing API key or a subscription that is not signed in, and its remembered
preferences: the Main model, reasoning effort, and speed, and a Subagent clause only when the provider
has a SubAgent record.

## 7. Errors

- Config-file enum values are read case-insensitively; wire values use camelCase strings.
- Invalid explicit AppServer values return protocol validation errors.
- Provider rejection of an advertised option fails through the normal turn error contract.
- Unknown models never receive Fast fields without a catalog match.
