# Observability

The DotCraft Dashboard is a web page for seeing what the agent actually did. Session traces, tool calls, merged configuration, and approval records all land there. When something goes wrong, you read a timeline instead of digging through logs.

![Sessions from every entry point emit trace events into the Dashboard window, where the Trace Timeline page lays out agent, tool, and error events in order, with Approvals and merged config alongside](/observability-trace-overview.svg)

## Open the Dashboard

In Desktop, choose **View → Open Dashboard** or press **Ctrl+Shift+D** (**⌘⇧D** on macOS). You can also open Dashboard from the tray. Use the selector at the top to switch between running local workspaces.

To browse saved traces in a standalone viewer, run:

```bash
dotcraft dashboard
```

Open `http://127.0.0.1:8080/dashboard`. For live diagnostics on a self-hosted server, see [Server Deployment](./server-deployment).

## What each page answers

### Confirm the model is responding

Trigger a session, then open it under **Sessions**. Its timeline lays the events out in order, turn by turn: each numbered model request, every tool call with its result, and any error along the way. One pass tells you where things stopped. Drag across the timeline strip at the top to focus on one stretch of time.

If there is no model output at all, provider credentials or the endpoint usually don't match — check the merged provider configuration on the **Settings** page. The **Provider** filter also shows retry behavior: how many attempts were made, how each one ended, and why retrying stopped.

### Diagnose a failed or blocked tool call

Open the session under **Sessions**, switch to the **Tools** or **Errors** filter, and select a call. Its arguments, result, latency, and stderr open beside the timeline.

If approval was the blocker, **Approvals** records every call that needed one: which entry point asked, whether it was approved or denied, and whether the decision came from you, a workspace policy, or a Hook. For the policies themselves, see [Security](./security).

### See why a config value wins

The **Settings** page renders the global `~/.craft/config.json` and the workspace `.craft/config.json` side by side: which layer defines each field, which value wins after the merge, and which fields need a restart.

When a change doesn't take effect, check here first whether it applies immediately, needs a subsystem restart, or needs an AppServer restart, then read [Settings Lifecycle](../configuration/settings-lifecycle).

### Check the project instructions in effect

The **Instructions** filter shows the `AGENTS.md` content the session actually carried and the files it came from. It is a snapshot captured by the thread and refreshed when the thread reloads project instructions, not a live view of the disk.

### Check automation and dream runs

**Automations** lists the automation definitions AppServer hosts, along with their current activity. **Dreams** is where you review the memory changes each Dreams run proposes, file by file, and apply or discard them. If an applied run turns out wrong, restore the memory it replaced.

## Consume the events yourself

To feed trace events into your own dashboard, the HTTP endpoints and event types are listed in [Dashboard API](../extend/protocols/dashboard-api). Dashboard renders that same data, and the AppServer protocol pushes it over Wire Protocol.

## Related docs

- [Security](./security) — which actions need approval, and where Dashboard's blocked records come from
- [Server Deployment](./server-deployment) — run Dashboard and AppServer together on a server
