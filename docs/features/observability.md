# Observability

DotCraft Dashboard is a web-based inspection surface for sessions, traces, tool calls, automation state, merged configuration, and approval records. It exists to answer "what did the agent do?" and "why does the config behave this way?"

## Quick Start

### Enable

Enable Dashboard in workspace configuration. Field names, defaults, and JSON examples live in [Entry Points and Services](../developing/configuration.md#entry-points-and-services).

### Start

```bash
dotcraft gateway
```

### Open

Default URL `http://127.0.0.1:8080/dashboard`. After a CLI, Desktop, TUI, or other entry point sends one conversation, the Dashboard shows sessions, tool calls, errors, and configuration state.

## Main Pages

| Page | Purpose |
|---|---|
| Dashboard | Runtime summary, entry-point status, recent activity |
| Sessions | Session list and details |
| Trace Timeline | Time-ordered Agent, tool, and error events |
| Settings | Configuration schema, global config, workspace config, merged result |
| Automations | Local tasks, Cron, and activity (requires Gateway) |
| Dreams | Review, apply, discard background dreams |
| Approvals | Historical approval records |

## Three Typical Workflows

### 1. First-time confirmation that the model works

After triggering a session, open **Trace Timeline** and confirm:

- **agent_message_chunk** is producing a token stream
- **tool_call** / **tool_result** pair up successfully
- **error** is not interrupting a specific tool call

If the token stream is empty, provider credentials / endpoint usually do not match. Check the merged result in the **Settings** page under `Providers[id]`.

### 2. Diagnose a failed tool call

Open the session detail:

- Switch to the **Tools** / **Errors** filter
- Click a tool call to see full args, result, latency, and stderr
- If it is an approval failure, go to **Approvals** and check whether it was auto-rejected

### 3. Inspect why config behaves this way

The **Settings** page renders the global `~/.craft/config.json` and workspace `.craft/config.json` side by side:

- Which layer defines each field
- Which value wins after merge
- Which fields are startup-level (require restart)

> [!TIP]
> If a change does not take effect, first identify in Settings whether it is in the immediate / subsystem-restart / AppServer-restart tier. See [Settings Lifecycle](../developing/settings-lifecycle.md).

## Run Modes

| Mode | Description |
|---|---|
| Local Dashboard | Single-workspace debugging |
| Gateway Dashboard | Shared backend with Automations and external channels |

Setting `Host` to `0.0.0.0` exposes Dashboard to your network. **Note**: Dashboard may show prompts, tool arguments, and tool results. Confirm network boundary and authentication before exposing it publicly.

## Approval Audit

The Approvals page records every tool call that needed approval:

- Who / which entry point initiated the request
- Decision (approve / deny / auto-approve / auto-deny)
- Reason (user, workspace policy, Hook, API AutoApprove)
- Tool and arguments

Related: [Security & Sandbox](./security.md).

## API & Trace Events

To consume Trace events from your own dashboard, see the HTTP endpoints and event types in [Dashboard API](../developing/dashboard-api.md). The events are the same data the AppServer protocol pushes over Wire Protocol — Dashboard just renders them as UI.

## Troubleshooting

### Browser cannot open Dashboard

Confirm Dashboard is enabled in configuration and use the URL printed in the console (default `http://127.0.0.1:8080/dashboard`).

### Automations panel is empty

The Automations panel needs Gateway to load the Automations module. Local Dashboard is fine for single-workspace debugging but does not orchestrate full automation state.

### Settings change has no effect

Model fields usually only apply to new sessions. AppServer, ports, Gateway, and external channels are startup-level and need a DotCraft restart. See [Settings Lifecycle](../developing/settings-lifecycle.md).

## Related

- [Project Workspace](./workspace.md)
- [Security & Sandbox](./security.md)
- [Dashboard API](../developing/dashboard-api.md)
- [Settings Lifecycle](../developing/settings-lifecycle.md)
