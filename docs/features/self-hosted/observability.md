# Observability

The DotCraft Dashboard is a web page for seeing what's going on — sessions, traces, tool calls, automation state, the merged config, and approval records. It's where you go to answer "what did the agent actually do?" and "why is the config behaving this way?"

## Quick Start

### Enable

Enable Dashboard in workspace configuration. Field names, defaults, and JSON examples live in [Entry Points and Services](../../developing/configuration#entry-points-and-services).

### Start

```bash
dotcraft dashboard
```

### Open

Default URL `http://127.0.0.1:8080/dashboard`. After a CLI, Desktop, or other entry point sends one conversation, the Dashboard shows sessions, tool calls, errors, and configuration state.

## Main Pages

| Page | Purpose |
|---|---|
| Dashboard | Runtime summary, entry-point status, recent activity |
| Sessions | Session list and details |
| Trace Timeline | Time-ordered Agent, tool, and error events |
| Settings | Configuration schema, global config, workspace config, merged result |
| Automations | Local tasks, Cron, and activity when hosted by AppServer |
| Dreams | Review, apply, discard background dreams |
| Approvals | Historical approval records |

## Three Typical Workflows

### 1. First-time confirmation that the model works

After triggering a session, open **Trace Timeline** and confirm:

- **agent_message_chunk** is producing a token stream
- **tool_call** / **tool_result** pair up successfully
- **error** is not interrupting a specific tool call

If the token stream is empty, provider credentials / endpoint usually do not match. Check the merged result in the **Settings** page under `Providers[id]`.

Terminal and provider diagnostics are recorded separately from visible response text. Use the **Responses** filter to inspect `ResponseTerminal` events for empty or usage-only streams. Use the **Provider** filter to inspect `ProviderError` and `ProviderResponseDiagnostic` events.

Use the **Provider** filter to inspect retry behavior. A `stream_attempt` diagnostic shows the
attempt number, outcome, retry decision, duration, and whether visible output prevented a retry.
For OpenAI Responses, it also shows the final HTTP status, upstream request ID, and abbreviated
session, thread, and prompt-cache hashes. Compare those hashes across attempts to confirm that
routing stayed stable without exposing the underlying identifiers.

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
> If a change does not take effect, first identify in Settings whether it is in the immediate / subsystem-restart / AppServer-restart tier. See [Settings Lifecycle](../../developing/lifecycle/settings-lifecycle).

## Run Modes

| Mode | Description |
|---|---|
| Read-only Dashboard | Inspect persisted state with `dotcraft dashboard` |
| AppServer Dashboard | Live Automations and external-channel state |

Setting `Host` to `0.0.0.0` exposes Dashboard to your network.

> [!CAUTION]
> Dashboard can show prompts, tool arguments, and tool results. Confirm your network boundary and authentication before exposing it publicly.

## Approval Audit

The Approvals page records every tool call that needed approval:

- Who / which entry point initiated the request
- Decision (approve / deny / auto-approve / auto-deny)
- Reason (user, workspace policy, Hook, API AutoApprove)
- Tool and arguments

Related: [Security & Sandbox](./security).

## API & Trace Events

To consume Trace events from your own dashboard, see the HTTP endpoints and event types in [Dashboard API](../../developing/protocols/dashboard-api). The events are the same data the AppServer protocol pushes over Wire Protocol — Dashboard just renders them as UI.

## Related docs

- [Security & Sandbox](./security)
- [Dashboard API](../../developing/protocols/dashboard-api)
- [Settings Lifecycle](../../developing/lifecycle/settings-lifecycle)
