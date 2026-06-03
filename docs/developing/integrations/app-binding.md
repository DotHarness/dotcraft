# App Binding

App Binding connects an installed native app to DotCraft and grants **one specific thread** access to that app's tools. The app can be a board, an IDE plugin, a managed runtime like [Teams](../../features/agent-system/teams), or your own tool — whatever it is, it keeps control of its accounts, consent, and high-risk actions. DotCraft only controls which tools the model can see, and gates every call with approvals and audit.

![DotCraft App Binding](https://github.com/DotHarness/resources/raw/master/dotcraft/whats-new/app.gif)

> [!NOTE]
> A binding is scoped to a single thread. Other threads — and other apps — see nothing unless you grant them too. Subagents and forks do not inherit bindings, and an imported thread never reactivates a binding on its own.

## How Access Is Granted

Access is split into separate steps on purpose, so nothing is granted by accident:

| Step | What it does | What it does *not* do |
|---|---|---|
| **1. Install the plugin** | Makes the app and its tool catalog visible in DotCraft. | Does not give any thread access. |
| **2. Install or open the native app** | Makes the app launchable through its registered OS identity. | Does not connect your account. |
| **3. Connect, then bind a thread** | Connects your app account, then grants selected scopes to the chosen thread. | Only the thread you pick is granted. |

Connecting opens the app through a deep link the app registers (for example `oratorio://dotcraft/connect?…`); the app then shows its own confirmation. DotCraft never asks you to pick an executable, source folder, or command line.

## What You Grant

When you bind a thread, you approve a set of **scopes**. Each scope carries a risk level that decides how its tools are exposed to the model:

| Risk | Meaning | Default exposure |
|---|---|---|
| **Read** | Reads app state without changing anything. | Loaded directly. |
| **Mutate** | Changes app-owned state or queues work. | Deferred — surfaced only when needed. |
| **External write** | Can publish, send, or write to an external system. | Deferred, and usually routed through an in-app confirmation. |

High-risk tools follow a propose-then-confirm pattern: the agent queues an operation, and you approve or publish it inside the app itself. Every tool call is recorded in DotCraft's audit trail, and the app keeps its own authorization records on top.

## In Desktop

App Binding shows up in three places:

- **Plugin detail page** — install the plugin, see whether the native app is installed, connect, bind the current thread, reconnect, or revoke.
- **Thread header** — bind, refresh, inspect, open the app, or revoke for the open thread.
- **Welcome flow** — start a new thread with one or more apps already bound before the first message.

Connection state and binding state are always shown separately, so you can tell "my account is connected" apart from "this thread has access".

## Binding State at a Glance

| State | Meaning |
|---|---|
| **Active** | The grant is valid and the app's tools are available to the thread. |
| **Offline** | The grant exists, but the app is closed or unreachable. Calls fail fast; reopen the app to reconnect. |
| **Expired** | The grant timed out. Tools are removed at the next safe point. |
| **Revoked** | You or the app cut access. Tools are disabled immediately. |

Closing the native app moves a binding to **offline**, not gone — reopening it reconnects and reattaches. You can **refresh** or **revoke** a binding at any time from the thread or plugin panel.

## Example Apps

App Binding works with any app that registers an OS protocol and speaks the AppServer handoff. A few that show the range:

- **[Oratorio](https://github.com/DotHarness/oratorio)** — an operator board for agent work. Connect it to a thread so the agent can list items, inspect a card, create tasks, and queue review rounds.
- **Teams** — DotCraft's multi-agent board is itself a managed App Binding runtime. See [Teams](../../features/agent-system/teams).
- **Your own tool** — wrap any service into an app with the SDK. See [Build an App](./build-an-app) for the integration guide.

## See Also

- [Build an App](./build-an-app) — the builder's guide for plugin and native app authors.
- [SDKs](../sdks/) — client libraries (.NET, TypeScript, Python), each with App Binding helpers.
- [Plugins & Tools](../../features/agent-system/plugins-tools) — how plugins package apps.
