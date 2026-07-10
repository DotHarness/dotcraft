# DotCraft Reasoning Settings Specification

| Field | Value |
|-------|-------|
| **Version** | 0.1.0 |
| **Status** | Living |
| **Date** | 2026-05-18 |
| **Parent Specs** | [AppServer Protocol](../protocols/appserver-protocol.md), [Desktop Client](../clients/desktop-client.md) |

Purpose: Define a provider-neutral reasoning settings contract for DotCraft clients and runtime adapters. The goal is to make reasoning depth easy to select alongside model selection while preserving provider-specific request shaping for OpenAI-compatible and Anthropic protocols.

---

## 1. Reasoning Model

DotCraft exposes reasoning as model metadata and session configuration rather than as UI-only state:

- `model/list` returns each model's supported reasoning efforts and default reasoning effort.
- Client turn/session configuration carries the selected reasoning effort separately from the model id.
- The model selector UI combines model selection and "Intelligence" selection so users can change both without visiting settings.

Provider-specific request details stay behind server-side adapters and `model-thinking-adapters.json`.

---

## 2. Goals and Non-Goals

### Goals

1. Provide one DotCraft reasoning control that works for `openai-chat-completions`, `openai-responses`, and `anthropic` provider protocols.
2. Let Desktop render model-aware reasoning choices without hardcoding model ids.
3. Preserve the existing persisted config shape: `Reasoning.Enabled`, `Reasoning.Effort`, and `Reasoning.Output`.
4. Support workspace defaults, per-thread overrides, and new-thread pending selections.
5. Keep provider request shaping catalog-driven through `model-thinking-adapters.json`.

### Non-Goals

- This spec does not define exact colors, spacing, animation, or menu geometry.
- This spec does not add a service-tier or speed-mode selector. A future speed selector may share the same picker shell.
- This spec does not expose raw provider-specific fields such as Anthropic `thinking.type` or OpenAI request body patches to clients.
- This spec does not change how reasoning content is rendered in the transcript.

---

## 3. User Model

DotCraft presents reasoning as a quick "Thinking" or "Intelligence" level:

| UI value | Runtime meaning |
|----------|-----------------|
| `Default` | Remove the local override and inherit the next lower scope. |
| `Off` | Do not request provider reasoning when the model/protocol can disable it. |
| `Low` | Request low reasoning effort. |
| `Medium` | Request medium reasoning effort. |
| `High` | Request high reasoning effort. |
| `Extra High` | Request the highest model-supported reasoning effort. |

The quick selector controls effort only. Reasoning output visibility remains a separate advanced setting and defaults to visible summarized/full reasoning according to provider capability.

If a model cannot disable reasoning, clients must not present `Off` as an ordinary selectable option. They may show it disabled with a short explanation.

---

## 4. Configuration Model

### 4.1 Canonical Wire DTO

AppServer exposes reasoning settings with this provider-neutral object:

```json
{
  "enabled": true,
  "effort": "high",
  "output": "full"
}
```

Fields:

| Field | Type | Meaning |
|-------|------|---------|
| `enabled` | boolean? | `false` means Off. `true` means request reasoning support. Omitted means preserve the existing value in patch-style APIs. |
| `effort` | `"none" \| "low" \| "medium" \| "high" \| "extraHigh"`? | Provider-neutral reasoning effort. Quick selectors must use `enabled=false` for Off instead of `effort=none`. |
| `output` | `"none" \| "summary" \| "full"`? | Whether provider-returned reasoning text should be requested. Quick selectors should leave this unchanged unless creating a first reasoning config. |

### 4.2 Persistence

The server maps the wire DTO onto the existing JSON config section:

```json
{
  "Reasoning": {
    "Enabled": true,
    "Effort": "High",
    "Output": "Full"
  }
}
```

No new top-level persisted config field is introduced for this iteration.

### 4.3 Effective Resolution

Effective reasoning is resolved in this order:

1. Thread configuration override.
2. Workspace `.craft/config.json`.
3. User/global config.
4. Built-in default: reasoning disabled.

When a new thread is created, Session Core must capture the effective provider id, model, and reasoning settings into `ThreadConfiguration` unless the client supplies explicit values in the start request. This keeps old threads stable when workspace defaults change later.

---

## 5. AppServer Protocol Changes

### 5.1 `ThreadConfiguration`

Add an optional `reasoning` object to `ThreadConfiguration`.

Example:

```json
{
  "providerId": "anthropic",
  "model": "claude-opus-4-7",
  "reasoning": {
    "enabled": true,
    "effort": "high",
    "output": "full"
  }
}
```

Rules:

- `thread/start.config.reasoning` sets the new thread's reasoning override.
- `thread/read`, `thread/start`, and `thread/resume` return `thread.configuration.reasoning` when the thread has captured or explicit reasoning.
- `thread/config/update` replaces the full `ThreadConfiguration`, so clients must preserve unrelated fields when changing reasoning.
- A missing `reasoning` field on an existing thread means "use the current effective AppConfig" for backward compatibility.

### 5.2 `workspace/config/update`

Add `reasoning` to `workspace/config/update`.

Params:

```json
{
  "reasoning": {
    "enabled": true,
    "effort": "medium",
    "output": "full"
  }
}
```

Semantics:

- Omitted `reasoning` means no change.
- `reasoning: null` removes the workspace `Reasoning` section so global/default config applies.
- `reasoning.enabled=false` writes an explicit workspace Off override.
- If `enabled=true` and `effort` is omitted, the server preserves the current workspace/global effort or uses `medium`.
- If `enabled=true` and `output` is omitted, the server preserves the current workspace/global output or uses `full`.
- Success responses include `reasoning`.
- `workspace/configChanged` emits the new region `workspace.reasoning`.

### 5.3 `model/list`

Extend each `ModelCatalogItem` with optional reasoning capability metadata:

```json
{
  "id": "claude-opus-4-7",
  "ownedBy": "anthropic",
  "createdAt": "2026-05-15T00:00:00Z",
  "reasoning": {
    "supportsDisable": true,
    "supportedEfforts": [
      { "effort": "low", "label": "Low", "description": "Faster, lighter reasoning." },
      { "effort": "medium", "label": "Medium", "description": "Balanced reasoning." },
      { "effort": "high", "label": "High", "description": "Deeper reasoning." },
      { "effort": "extraHigh", "label": "Extra High", "description": "Maximum depth for supported models." }
    ],
    "defaultEffort": "high",
    "supportedOutputs": ["none", "summary", "full"],
    "defaultOutput": "full"
  }
}
```

Rules:

- `reasoning: null` or missing means the server has no known reasoning support for the model.
- `supportsDisable=false` means the client must not offer an ordinary Off action for this model.
- `supportedEfforts` is ordered by the server and should be rendered in that order.
- `defaultEffort` must be one of `supportedEfforts`.
- The server derives this metadata from provider protocol, model id, endpoint, and `model-thinking-adapters.json`; clients must not hardcode provider/model rules.
- If upstream model listing fails, clients may still allow manual model entry, but reasoning choices are limited to `Default` and the currently effective setting.

---

## 6. Provider Adapter Semantics

### 6.1 Catalog Source

`model-thinking-adapters.json` is the source of provider/model-specific behavior. It should describe both:

- request shaping, such as Anthropic adaptive thinking or OpenAI-compatible deep-thinking body patches
- UI capability metadata, such as supported efforts, default effort, output support, and whether Off is enforceable

Catalog entries must support model prefix matching and namespaced suffix matching, so a namespaced model ID ending in `claude-opus-4-7` matches `claude-opus-4-7`. Protocol-level reasoning capability entries can expose the full reasoning control surface for unlisted Anthropic-protocol models, but Anthropic `thinking.type="adaptive"` request shaping is applied only by explicit `anthropicThinking` model or endpoint adapters.

Anthropic-compatible providers may also declare provider-visible reasoning-history compatibility in `anthropicMessageContent.adapters`. These entries use the same model and endpoint matching rules and can set `reasoningHistory.blockType` for historical assistant `TextReasoningContent`. The built-in DeepSeek Anthropic adapter maps DotCraft reasoning history to Anthropic-compatible `thinking` blocks before sending historical assistant messages to the provider; it is not a generic unsupported-block filter.

### 6.2 OpenAI Protocols

For `openai-chat-completions` and `openai-responses` protocol runtimes:

- `enabled=false`: omit `ChatOptions.Reasoning` and provider-specific thinking patches.
- `enabled=true`: set `ChatOptions.Reasoning.Effort` and `ChatOptions.Reasoning.Output`.
- OpenAI-compatible models that need non-standard request fields continue to use catalog-driven adapters such as the existing deep-thinking adapter.
- Unknown OpenAI-compatible models may accept the config but should not be shown as reasoning-capable in the quick selector unless the catalog marks them as supported.

### 6.3 Anthropic Protocol

For `anthropic` protocol runtimes:

- `enabled=false`: omit the Anthropic `thinking` field when the model supports disabling.
- `enabled=true`: use the most-specific catalog adapter to produce Anthropic SDK `MessageCreateParams` when one matches; unlisted models still carry provider-neutral `ChatOptions.Reasoning` but do not receive Anthropic `adaptive` request shaping.
- Adaptive models use `thinking.type="adaptive"`, `thinking.display` from `Reasoning.Output`, and `output_config.effort` from `Reasoning.Effort`.
- `Extra High` maps through the catalog, because Anthropic model families differ: Opus 4.7 supports `xhigh`, while Opus 4.6, Sonnet 4.6, and Mythos support `max`.
- Models whose default behavior always reasons, such as Mythos Preview, must be represented with `supportsDisable=false`.

---

## 7. Client UX Contract

### 7.1 Composite Model Picker

Desktop should evolve the current model picker into a composite model/reasoning picker.

Required behavior:

- The trigger displays the effective model and effective reasoning quick label.
- Opening the picker exposes a Thinking section and a Model section.
- Thinking choices are filtered by the selected/effective model's `model/list` reasoning metadata.
- Model choices continue to allow manual fallback when provider model listing is unavailable.
- Selecting a model may change available reasoning choices; if the current reasoning effort is unsupported, the client selects the model's `defaultEffort` and tells the user in the normal non-blocking status/toast channel.

Recommended Desktop shape:

- Top-level menu contains Thinking choices first: Off/Low/Medium/High/Extra High.
- A separate Model row opens the model submenu while keeping the top-level Thinking choices compact.
- The trigger text should remain compact, for example `claude-opus-4-7 High`.

### 7.2 Save Scope

The quick picker mirrors current DotCraft model picker behavior:

- If an active thread exists, selection updates the workspace default and the active thread configuration.
- If no active thread exists, selection updates the workspace default and the pending new-thread configuration.
- Settings pages may expose workspace-only edits, but the composer picker is an "use this now and by default next time" control.

### 7.3 Busy State

Clients must not mutate reasoning settings while a turn is running, waiting for approval, or waiting for user input. The picker may remain visible but disabled with a short reason.

### 7.4 Reconnect and External Changes

Clients must recompute effective model and reasoning when:

- `thread/read` or `thread/resume` returns a new configuration
- `workspace/configChanged` includes `workspace.model`, `workspace.provider`, or `workspace.reasoning`
- model catalog data is reloaded for a different provider

---

## 8. Validation and Error Handling

- The server validates enum strings case-insensitively on config-file input and camelCase on wire DTOs.
- Invalid reasoning values return a protocol validation error for AppServer requests.
- If a client requests unsupported reasoning for a known model, the server coerces to the model default only when the client used `Default`; explicit unsupported values should be rejected with a clear error.
- If the provider rejects a reasoning request despite catalog support, the turn fails normally and trace diagnostics should include the effective reasoning settings.
- Clients should surface model-list unsupported endpoints as they do today and should not block manual model entry.

---

## 9. Implementation Plan

1. Extend `ThreadConfiguration`, clone/capture logic, and agent construction so thread-level reasoning overrides feed `AgentFactory` and SubAgent creation.
2. Extend AppServer DTOs and spec sections for `thread/config/update`, `workspace/config/update`, `workspace/configChanged`, and `model/list`.
3. Extend `model-thinking-adapters.json` and its catalog loader to expose reasoning capabilities for OpenAI-compatible and Anthropic models.
4. Update Desktop model catalog store and composite `ModelPicker` to carry reasoning metadata and save reasoning updates.
5. Add protocol, server, and Desktop tests for capability metadata, persistence, active-thread updates, and unsupported model fallback.

---

## 10. Acceptance Criteria

- Desktop can show and change reasoning depth from the model picker.
- The same UI works for OpenAI and Anthropic provider protocols without client-side model hardcoding.
- Workspace config persists to the existing `Reasoning` section.
- Active thread changes take effect without restarting AppServer.
- Existing config files without `Reasoning` continue to behave as reasoning disabled.
- `dotnet test` passes for AppServer/Core changes.
- Desktop tests cover model catalog parsing, picker behavior, and config update calls.
