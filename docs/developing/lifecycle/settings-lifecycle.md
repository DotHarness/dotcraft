# DotCraft Settings Lifecycle Guide

This page targets integrators and contributors. It explains the three-tier settings lifecycle in Desktop and how to tell whether a change is already applied or still pending.

## 1. Three-Tier Settings Model

Desktop groups settings by how changes become effective:

1. **Live Apply (Tier A)**
   - Effective immediately after apply.
   - Typical examples: `Skills.DisabledSkills`, MCP configuration entries.
2. **Subsystem Restart (Tier B)**
   - Persisted, but requires restarting a related subsystem to take effect.
   - Typical examples: settings tied to external-channel subsystem lifecycle.
3. **Process Restart (Tier C)**
   - In Local mode, persisted but requires restarting the Hub-managed local AppServer process.
   - Typical examples: startup-level Core settings, local AppServer binary path, and some entry-point settings.

You can identify the tier from the group action pattern: immediate apply, restart, or apply-and-restart.

## 2. Representative Fields by Tier

| Area | Representative fields | Effect timing |
|---|---|---|
| Skills / MCP | `Skills.DisabledSkills`, MCP server definitions | Live Apply |
| External Channel | External channel related configuration | Subsystem Restart |
| Connection / Local AppServer | `connectionMode = local`, local AppServer binary path, local WebSocket listener config | Hub-managed local AppServer can Apply & Restart |
| Connection / Remote AppServer | `connectionMode = remote`, remote WebSocket URL, token | Probe the draft URL/token through WebSocket initialize first, then save and switch only after success; no remote AppServer restart |
| Model Providers | `Providers[id]`, `ProviderId`, `Model` | Desktop / AppServer refresh new-session defaults through provider management APIs |

Notes:

- The Desktop model page manages the provider registry; credentials and endpoints belong only to `Providers[id]`.
- Changing `Model` updates new-session defaults through provider management APIs; existing threads keep the model captured at creation time unless the client explicitly changes that thread's model.
- Workspaces save only the current `ProviderId` and `Model` overrides, not root-level `ApiKey` / `EndPoint`.
- A Remote AppServer is owned by the user or remote environment. Desktop tests and switches the connection, but does not restart remote processes.
- If Desktop was launched with `--remote`, the current session is controlled by that launch argument and persistent connection switching is unavailable in Settings.

## 3. Applied vs Pending Changes

Use these signals to interpret state:

- **Applied**: the config is persisted and the required tier action is complete (live apply succeeded, or restart completed).
- **Pending**: restart-required hints are shown, which means config changed on disk but runtime has not switched yet.
- **Pending Remote connection**: URL/token edits are not yet the default connection. Apply & Connect probes the draft connection first and saves only after success, so a failed probe does not trap the next launch behind a bad endpoint.
- **Per-group dirty state**: if only one group changed, only that group's action is required; a global save is not needed.
- **Existing bad Remote config**: if a saved Remote connection is invalid at startup, the error screen offers **Open connection settings**, which takes you to Settings > Connection and clears the blocking overlay so you can fix the URL/token or switch back to Local.

## Related

- [AppServer Protocol](../protocols/appserver-protocol) — the `workspace/configChanged` client event
- [Configuration Reference](../configuration) — every field these tiers cover
- [AppServer Mode](./appserver) — remote / multi-client connections
