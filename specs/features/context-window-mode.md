# DotCraft Context Window Mode Specification

| Field | Value |
|-------|-------|
| **Version** | 0.1.0 |
| **Status** | Living |
| **Date** | 2026-07-09 |
| **Parent Specs** | [Session Core](../architecture/session-core.md), [AppServer Protocol](../protocols/appserver-protocol.md) |

Purpose: define server-authoritative context-window modes so clients can offer a MAX option without owning model-window rules or local-only state.

---

## 1. Model

DotCraft has two context-window modes:

| Mode | Meaning |
|------|---------|
| `default` | Current behavior. `Compaction.ContextWindow` is used when explicitly configured; otherwise the model catalog window is capped by `Compaction.MaxContextWindow`. |
| `max` | Thread uses the raw model-catalog context window directly when the catalog has an explicit match larger than the configured window. |

Omitted or null context-window configuration is equivalent to `default`.

V1 is intentionally binary. DotCraft does not expose arbitrary numeric per-thread context-window sizes.

---

## 2. Resolution

Resolution is server-owned and uses the thread's effective provider/model:

1. Resolve the model runtime from `ThreadConfiguration.providerId` and `ThreadConfiguration.model`, falling back only where the runtime resolver already does so.
2. Resolve the raw model catalog entry, including whether the catalog window came from an explicit model, prefix, or namespaced-suffix match.
3. Resolve the configured default compaction window:
   - if `Compaction.ContextWindow` is explicit, keep it
   - otherwise infer from the catalog and apply `Compaction.MaxContextWindow`
4. Apply mode:
   - `default`: keep the configured default compaction window
   - `max`: require an explicit catalog match and `catalogWindow > configuredWindow`, then set `Compaction.ContextWindow = catalogWindow`

The `max` mode intentionally bypasses `Compaction.MaxContextWindow`. That cap remains the soft guardrail for default inferred context only.

---

## 3. Validation

Servers reject explicit `max` requests with JSON-RPC `InvalidParams` when:

- the effective model has no explicit catalog match
- the catalog match is only the catalog default/fallback
- `catalogWindow <= configuredWindow`

Manual or unknown model ids therefore do not support MAX unless a model-context catalog entry explicitly matches them.

---

## 4. Persistence

Thread-level configuration uses:

```json
{
  "contextWindow": {
    "mode": "max"
  }
}
```

Workspace default configuration is persisted as `Compaction.ContextWindowMode`:

- `max` persists the workspace default preference for newly created threads.
- `default` or `null` removes the explicit workspace default.
- New threads capture the workspace default context-window mode only when their resolved model supports it.
- Existing threads keep their captured thread configuration until explicitly updated.
- Forks copy the source thread's context-window configuration unless the fork request supplies an override.

---

## 5. AppServer Metadata

`model/list` returns server-authored context-window metadata for each model:

```json
{
  "contextWindow": {
    "catalogWindow": 1000000,
    "configuredWindow": 256000,
    "supportsMax": true,
    "maxWindow": 1000000
  }
}
```

`catalogWindow` is the raw catalog/fallback resolution value, `configuredWindow` is the normal default-mode compaction window after cap rules, and `maxWindow` is the MAX-mode window when supported or the configured window otherwise. `supportsMax` is true only when the model has an explicit catalog match and `catalogWindow > configuredWindow`. Clients must use this metadata instead of hardcoding model ids.

`ContextUsageSnapshot.contextWindow` remains the effective denominator after reserve and buffer logic, not the raw catalog window.
