# App Binding

App Binding lets you connect an installed native app — [Oratorio](#examples), an IDE plugin, or your own tool — and grant **one specific thread** access to that app's tools. The app keeps control of its accounts, consent, and high-risk actions; DotCraft controls which tools the model can see and gates every call with approvals and audit.

![DotCraft App Binding](https://github.com/DotHarness/resources/raw/master/dotcraft/whats-new/app.gif)

> [!NOTE]
> A binding is scoped to a single thread. Other threads — and other apps — see nothing unless you grant them too. Subagents and forks do not inherit bindings, and an imported thread never reactivates a binding on its own.

## Three Separate Steps

App access is split into separate steps on purpose, so nothing is granted by accident:

| Step | What it does | What it does *not* do |
|---|---|---|
| **1. Install the plugin** | Makes the app and its tool catalog visible in DotCraft. | Does not give any thread access. |
| **2. Install or open the native app** | Makes the app launchable through its registered OS identity. | Does not connect your account. |
| **3. Connect, then bind a thread** | Connects your app account, then grants selected scopes to the chosen thread. | Only the thread you pick is granted. |

Connecting opens the app through a deep link (for example `oratorio://dotcraft/connect?…`); the app then shows its own confirmation. DotCraft never asks you to pick an executable, source folder, or command line.

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

## Examples

- **Oratorio** — connect Oratorio boards to a thread so the agent can list items, inspect a card, create tasks, and queue review rounds.
- **Teams** — DotCraft's multi-agent board is itself a managed App Binding runtime. See [Teams](../../features/agent-system/teams).
- **Your own tool** — wrap any service into an app with the SDK. See [App Binding Integration](#app-binding-integration) to build one.

## App Binding Integration

App Binding is the platform flow for connecting an **external native app** to DotCraft and granting one thread access to app-owned tools — without the app handing over its accounts, authorization, or high-risk operations. This section is the builder's guide for plugin and native app authors.

### Product Model

App Binding has four explicit layers. Keeping them separate is what makes access opt-in rather than ambient:

| Layer | Scope | Owner | Meaning |
|---|---|---|---|
| Plugin install | Workspace | DotCraft | Makes app metadata and tool catalog visible. |
| Native app install | Machine / user | OS + app | Makes the app launchable via its registered OS identity. |
| App connection | Workspace + user + app | App + DotCraft | Connects one account/workspace through app-side consent. |
| Thread binding | Thread + app + grant | App + DotCraft | Grants selected scopes and tools to one thread. |

### Authority Split

Real authorization stays app-owned. DotCraft validates and gates, but it is **not** a substitute for the app's own checks.

| DotCraft owns | The app owns |
|---|---|
| Catalog, plugin, connection, and binding records | Account selection, authentication, and consent UI |
| Thread-scoped model-visible tool exposure | Real authorization and resource policy |
| Descriptor / scope / namespace / risk validation | Grant proof, revocation, and app-side audit |
| Approval gates before dispatching a tool call | Final validation of every attached tool call |
| Lifecycle and tool-call audit | Native app lifecycle and any local services |

### What You Build

Two pieces:

1. **A DotCraft plugin** that contributes an app descriptor. A bare app descriptor with no owning plugin gets no product flow.
2. **A native app** that registers an OS protocol, handles the deep-link handoff, inspects the request over AppServer, accepts it, and attaches its tools.

#### 1. Contribute an App Descriptor

A plugin points at an `apps` document from its manifest:

```json
{
  "schemaVersion": 1,
  "id": "oratorio",
  "displayName": "Oratorio",
  "capabilities": ["skill", "app"],
  "skills": "./skills/",
  "apps": "./apps.json"
}
```

`apps.json` declares the app's identity, native app metadata, scopes, and a static tool catalog:

```json
{
  "apps": [
    {
      "appId": "com.dotharness.oratorio",
      "toolNamespace": "oratorio",
      "displayName": "Oratorio",
      "developerName": "DotHarness",
      "description": "Manage Oratorio boards from selected DotCraft threads.",
      "nativeApplication": {
        "protocol": "oratorio",
        "installUrl": "https://github.com/DotHarness/oratorio/releases"
      },
      "connection": {
        "handoffModes": [
          { "mode": "customProtocol", "uriTemplate": "oratorio://dotcraft/{operation}?app={appId}&request={requestId}&token={requestToken}&endpoint={endpoint}" }
        ]
      },
      "scopes": [
        { "id": "board.read", "displayName": "Read boards", "description": "Read board items and rounds.", "risk": "read", "defaultSelected": true },
        { "id": "board.manage", "displayName": "Manage boards", "description": "Create tasks and queue review rounds.", "risk": "mutate" }
      ],
      "toolCatalog": [
        { "name": "ListBoardItems", "scope": "board.read", "risk": "read", "defaultExposure": "direct" },
        { "name": "QueueReviewRound", "scope": "board.manage", "risk": "mutate", "defaultExposure": "deferred" }
      ]
    }
  ]
}
```

Key rules:

- `appId` is reverse-DNS, lowercase, at least three labels. `toolNamespace` matches `^[A-Za-z_][A-Za-z0-9_]*$`, is unique across the catalog, and prefixes every app-bound tool.
- The static `toolCatalog` is a coarse declaration for discovery, consent, and validation — not the executable schema. Concrete schemas arrive at attach time. Set `dynamicToolCatalog.enabled` to attach a runtime catalog instead.
- A tool's `risk` must not be lower than its scope's. `mutate` and `externalWrite` default to deferred exposure.

#### 2. Handle the Handoff in the Native App

DotCraft launches your registered protocol (it never spawns an executable). The app parses the URL, inspects the request over the short-lived handoff endpoint, accepts it, and attaches tools. With the [.NET SDK](../sdks/dotnet):

```csharp
var handoff = AppBindingHandoff.Parse(handoffUrl, expectedScheme: "oratorio", expectedAppId: "com.dotharness.oratorio");

await using var client = await DotCraftClient.ConnectRemoteAsync(
    handoff.AppServerUrl!, options: new DotCraftClientOptions { ClientName = "oratorio" }, ct);

// Inspect the request from a trusted source — never from deep-link query text alone.
var request = await client.AppBindings.GetBindingRequestAsync<JsonElement>(new {
    appId = handoff.AppId, bindingRequestId = handoff.RequestId, requestToken = handoff.RequestToken
}, ct);

// Accept after app-side authorization. Scopes may be narrowed, never expanded.
await client.AppBindings.AcceptBindingAsync<JsonElement>(new {
    bindingRequestId = handoff.RequestId, requestToken = handoff.RequestToken,
    grantId = "grant_" + Guid.NewGuid().ToString("N"),
    grantedScopes = new[] { "board.read" },
    approvalMode = "appAccepted", approvedBy = Environment.UserName
}, ct);

// Attach concrete tool specs, then keep the connection alive by draining notifications.
```

The full RPC surface is in the [.NET SDK](../sdks/dotnet) and the [SDK overview](../sdks/). The same flow is available to any language over the [AppServer Protocol](../protocols/appserver-protocol).

### Binding Flow

![DotCraft App Binding handoff](/app-binding-flow.svg)

Connection (`app/connection/*`) works the same way at workspace+user+app scope and must complete before a binding request. DotCraft requires user confirmation before launching a handoff; the app inspects and authorizes before accepting.

### Tool Exposure

- **Dynamic-first transport.** App-bound tools ride on Runtime Dynamic Tools but are bound to a persisted thread binding (not a transient connection), so the app can reattach after reconnecting. Clients render them as `dynamicToolCall` items.
- **Validation.** For every attached tool DotCraft checks: plugin installed/enabled, binding usable for the thread, namespace equals `toolNamespace`, tool name is in the catalog, granted scopes cover the tool's scope, and risk/exposure fit policy.
- **Direct vs deferred.** `read` may be direct; `mutate` and `externalWrite` default to deferred. DotCraft may override placement for policy or prompt-cache stability.
- **Approval.** App-bound tools reuse `DynamicToolSpec.approval`. DotCraft gates *before* dispatch; the app still validates *after*. Prefer the propose → record → human approve → app writes pattern for external writes.
- **Offline stubs.** When a binding is `offline`, calls fail fast with a structured error. Standard codes: `AppBindingOffline`, `AppBindingExpired`, `AppBindingRevoked`, `AppBindingScopeDenied`, `AppBindingToolUnavailable`, `AppBindingProtocolViolation`.

### App Context

Runtime `thread/start.additionalContext` and `thread/resume.additionalContext` are client-runtime hints. Use them with Runtime Dynamic Tools when the connected client needs to add short guidance, such as telling the agent to search for a deferred tool first.

App Binding context blocks use `app/binding/context/*`. They are persisted thread+app business context from an accepted binding, such as selected project metadata or app-side state that should survive reconnects. Both surfaces use App Context prompt semantics, but their lifecycle and write APIs are different.

### Security Essentials

- **Handoff tokens** default to a 10-minute TTL, are single-purpose, bound to one request/app/workspace/user/operation (binding tokens also to thread + scopes), consumed on success, and never exposed to the model.
- **The handoff endpoint** is short-lived and only permits inspecting/completing the matching request and keeping the tool channel alive — not arbitrary thread execution or config mutation.
- **Connection credentials** are scoped to workspace + user + appId and permit only App Binding methods.
- **Grant proof** is app-owned. DotCraft stores only enough to ask the app to revalidate; treat `grantId` / `grantProof` as references requiring app-side validation.
- **Deep links are activation hints, not authorization.** Always inspect over AppServer before rendering confirmation or accepting.

### Capability Check

Servers advertise `capabilities.appBinding: true`. Check it before calling any `app/*` or `thread/appBindings/*` method. App context blocks (`app/binding/context/*`) require `capabilities.appContextBlocks`; thread input dispatch (`app/threadInput/enqueue`) requires `capabilities.appThreadInputEnqueue`.

### RPC Reference

| Area | Methods |
|---|---|
| Discovery | `app/list`, `app/view` |
| Connection | `app/connection/{start,request/get,connect,status,revoke}` |
| Binding | `app/binding/{request/create,request/get,request/cancel,accept,attachTools}` |
| Context & input | `app/binding/context/{upsert,remove}`, `app/threadInput/enqueue` |
| Thread management | `thread/appBindings/{list,revoke,refresh}`, `thread/appContextBlocks/list` |
| Notifications | `app/list/updated`, `app/connection/changed`, `thread/appBindings/changed` |

For typed parameters and results, use the [.NET SDK](../sdks/dotnet) or the [AppServer Protocol](../protocols/appserver-protocol). Persisted App Binding state lives at `.craft/app-bindings/state.json`.

### See Also

- [SDKs](../sdks/) and [.NET SDK](../sdks/dotnet) — client libraries and App Binding helpers.
- [AppServer Protocol](../protocols/appserver-protocol) — the wire contract behind every SDK.
- [Plugins & Tools](../../features/agent-system/plugins-tools) — how plugins package apps.
