# DotCraft App Binding Specification

| Field | Value |
|-------|-------|
| **Version** | 0.5.0 |
| **Status** | Living |
| **Date** | 2026-06-19 |
| **Related Specs** | [AppServer Protocol](appserver-protocol.md), [Tool Result Presentation](tool-result-presentation.md), [Plugin Architecture](../extensions/plugin-architecture.md), [Session Core](../core/session-core.md), [Desktop Client](../clients/desktop-client.md), [SDK](../sdk/sdk.md), [TypeScript SDK Binding](../sdk/typescript.md) |

Purpose: define the product-grade App Binding architecture for DotCraft. App Binding lets a user connect an installed native application or a managed channel runtime and grant one specific DotCraft thread access to app-owned tools or social-channel conversation input, while keeping app/channel authority, account consent, and high-risk operations under the owning runtime's control.

Oratorio is the first validating app, but this specification is not Oratorio-specific.

---

## 1. Scope

This specification defines:

- Plugin-contributed app descriptors and catalog-visible app metadata.
- Native app installation/availability discovery for Desktop clients.
- OS deep link handoff for app connection and thread binding.
- App-owned connection and binding consent flows.
- AppServer RPCs and notifications for discovery, connection, binding, and tool attachment.
- Runtime tool exposure rules for app-bound Dynamic Tools and future app-bound MCP tools.
- Social-channel bindings that attach an existing Desktop/AppServer thread to a QQ/WeCom/Telegram/Feishu/Weixin conversation without changing `Thread.OriginChannel`.
- Safe app-published connection metadata for Desktop extensions.
- Optional declarative tool-result presentation contracts for app-bound tools.
- Security, approval, lifecycle, audit, Desktop UX, and SDK requirements.
- Oratorio-specific validation guidance.

This specification does not define:

- A remote marketplace registry or automatic native app downloader.
- The internal OAuth, account linking, or business policy implementation of any app.
- Exact Oratorio board-management tool schemas.
- A replacement for Runtime Dynamic Tools, MCP, Session Core, or AppServer.
- Development-only local command launch flows.

---

## 2. Product Model

App Binding has four explicit user-visible layers:

| Layer | Scope | Owner | Meaning |
|-------|-------|-------|---------|
| Plugin install | Workspace | DotCraft | Makes app metadata, optional skills, and tool catalog visible in DotCraft. |
| Native app install | Machine/user | Operating system + app | Makes the native app launchable through its registered OS identity or protocol. |
| App connection | Workspace + user + app | App and DotCraft | Connects one app/account/workspace to DotCraft through app-side user consent. |
| Thread binding | Thread + app + grant | App and DotCraft | Grants selected scopes and tools to one DotCraft thread through app-controlled authorization. |

The expected product flow is:

1. The user installs or enables a DotCraft plugin from the DotCraft catalog.
2. DotCraft shows whether the required native app is installed.
3. If the native app is missing, DotCraft opens the app's install or release page.
4. If the native app is installed, DotCraft opens an OS deep link such as `oratorio://dotcraft/connect?...`.
5. The native app is launched or focused by the OS.
6. The native app shows connection confirmation UI. For binding handoffs, the app must inspect and validate the request, then either auto-accept under its own policy or show additional confirmation when policy requires it.
7. After the app accepts the connection or binding, it calls DotCraft AppServer to complete the flow and attach tools.

DotCraft must not require the user to select a source checkout, plugin root, executable path, or `localCommand` for the product flow.

---

## 3. Design Goals

App Binding must:

1. Keep app authority scoped to the thread that the user explicitly grants.
2. Separate plugin installation, native app installation, app connection, and thread binding.
3. Let Desktop and future clients expose the same connection and binding state.
4. Keep real authorization app-owned while letting DotCraft control model-visible tool exposure.
5. Preserve prompt-cache stability when a bound app is temporarily offline.
6. Support Dynamic Tools first, while leaving a future MCP transport path through the same binding exposure layer.
7. Make high-risk app actions visible, auditable, revocable, and app-confirmed.
8. Avoid product UX that exposes development paths, source roots, or command-line handoff details.

Non-goals:

- Do not make every installed app's tools globally available to every thread.
- Do not persist app grants in `ThreadConfiguration.McpServers`.
- Do not treat an `app://` mention as consent to install, connect, or bind an app.
- Do not let DotCraft silently bind an app after plugin installation or app connection.
- Do not launch headless app servers as the user-facing authorization surface.

---

## 4. Authority Split

Grants are app-owned.

DotCraft owns:

- Catalog, plugin, connection, and binding records.
- Thread-scoped model-visible tool exposure.
- Descriptor, namespace, scope, tool-catalog, risk, and exposure validation.
- DotCraft-side approval gates before dispatching model tool calls.
- Lifecycle audit and tool-call shell audit.

The app owns:

- Account selection, authentication, and user consent UI.
- Real authorization and resource policy enforcement.
- Grant proof, grant revocation, and app-side audit references.
- Final validation of every attached tool call.
- Native app lifecycle and any app-owned local services needed to serve tools.

DotCraft validation is intentionally not a substitute for app-side authorization.

Connected apps may publish a small `publicMetadata` object when completing or refreshing an App Binding connection. DotCraft may expose this metadata through connection status only after validating that it is safe for Desktop clients. v1 public metadata is limited to redacted display values and loopback surface endpoints such as local HTTP or WebSocket URLs used by a trusted Desktop extension. Secret tokens, account credentials, raw grants, and app-private proof material must remain in `connectionProof` and must never be echoed to clients.

A connected app may refresh its own `publicMetadata` after the initial connect — without a new user grant, handoff, or dialog — through `app/connection/refreshMetadata` (§9.6). This is a transport-only update: it lets an app that reopened on a new dynamic loopback port re-publish its current surface endpoints so durable Desktop surfaces keep working across app restarts. The refresh is authorized solely by the existing connection (matching `appId` plus the app-owned `connectionProof`) over the loopback app-server, mutates only `publicMetadata` (re-validated by the same loopback sanitizer), and never widens scope or changes grants — it is maintenance of an existing consent, analogous to renewing a lease, not a new authorization.

Trusted Desktop extensions may use `publicMetadata.surfaceEndpoints` only as a discovery layer for app-owned local surfaces. The extension must still declare the expected loopback origins in its Desktop extension descriptor, and Desktop's main process must enforce those origins from the verified descriptor before issuing renderer-initiated HTTP requests. Surface endpoints are read-only presentation endpoints by default. A trusted Desktop extension that declares `surfaceWriteScopes` may issue scoped mutating requests to a surface endpoint while its required app is connected, as defined by the extension surface write transport in [Plugin Architecture](../extensions/plugin-architecture.md). The app's loopback surface authorizes each write with the connection credential it issued and remains authoritative; App Binding tools and app-owned approval remain the path for agent-invoked and externally-visible writes.

---

## 5. App Descriptor Contributions

DotCraft plugins are the product-facing package layer. A plugin may contribute skills, MCP servers, LSP servers, interface metadata, and one or more App Binding app descriptors. DotCraft must not expose a product App Binding flow for a naked app descriptor that has no owning plugin.

Plugins contribute apps through a manifest-relative `apps` path:

```json
{
  "schemaVersion": 1,
  "id": "oratorio",
  "version": "0.1.0",
  "displayName": "Oratorio",
  "description": "Connect Oratorio boards to selected DotCraft threads.",
  "capabilities": ["skill", "app"],
  "skills": "./skills/",
  "apps": "./apps.json"
}
```

The app descriptor document contains one or more descriptors:

```json
{
  "apps": [
    {
      "appId": "com.dotharness.oratorio",
      "toolNamespace": "oratorio",
      "displayName": "Oratorio",
      "developerName": "DotHarness",
      "description": "Manage Oratorio board items and review rounds from selected DotCraft threads.",
      "category": "Developer Tools",
      "icon": "./assets/oratorio.svg",
      "releasePage": "https://github.com/DotHarness/oratorio/releases",
      "nativeApplication": {
        "displayName": "Oratorio",
        "protocol": "oratorio",
        "installUrl": "https://github.com/DotHarness/oratorio/releases",
        "platforms": {
          "windows": { "appUserModelId": "com.oratorio.desktop", "protocol": "oratorio" },
          "macos": { "bundleId": "com.oratorio.desktop", "protocol": "oratorio" },
          "linux": { "desktopId": "oratorio.desktop", "protocol": "oratorio" }
        }
      },
      "connection": {
        "handoffModes": [
          {
            "mode": "customProtocol",
            "uriTemplate": "oratorio://dotcraft/{operation}?app={appId}&request={requestId}&token={requestToken}&endpoint={endpoint}"
          }
        ]
      },
      "scopes": [
        {
          "id": "board.read",
          "displayName": "Read boards",
          "description": "Read Oratorio board items, rounds, runs, and source state.",
          "risk": "read",
          "defaultSelected": true
        },
        {
          "id": "board.manage",
          "displayName": "Manage boards",
          "description": "Create local tasks and queue Oratorio review rounds.",
          "risk": "mutate"
        }
      ],
      "toolCatalog": [
        {
          "name": "ListBoardItems",
          "scope": "board.read",
          "risk": "read",
          "defaultExposure": "direct",
          "display": {
            "title": "List board items",
            "subtitle": "Oratorio"
          },
          "_meta": {
            "ui": { "resourceUri": "ui://oratorio/board", "visibility": ["model", "app"] }
          }
        },
        {
          "name": "QueueReviewRound",
          "scope": "board.manage",
          "risk": "mutate",
          "defaultExposure": "deferred",
          "display": {
            "title": "Queue review round",
            "subtitle": "Oratorio"
          },
          "_meta": {
            "ui": { "resourceUri": "ui://oratorio/review", "visibility": ["model", "app"] }
          }
        }
      ]
    }
  ]
}
```

### 5.1 Descriptor Fields

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `appId` | string | yes | Reverse-DNS globally unique app id, for example `com.dotharness.oratorio`. |
| `toolNamespace` | string | yes | Model-visible namespace required for all app-bound tools. |
| `displayName` | string | yes | Human-readable app name. |
| `developerName` | string | yes | Human-readable developer or organization name. |
| `description` | string | yes | User-visible description. |
| `category` | string | no | UI category. |
| `icon` | string | no | Manifest-relative icon path. AppServer exposes accepted icons to Desktop as a data URL or safe URL. |
| `originChannel` | string | no | The `SessionIdentity.ChannelName` this app stamps on threads it originates. When a thread's `originChannel` matches, the host attributes the thread to this app and renders the app's icon + `displayName` as the thread origin badge (see [AppServer Protocol] `thread/list` `originApp`). Opt-in; there is no implicit `toolNamespace` matching. Must exactly match the channel name the app uses at thread create/fork/resume. |
| `originMembers` | AppOriginMemberDescriptor[] | no | Finer-grained per-member branding for an app that originates threads for distinct members/roles (for example team roles). Requires `originChannel`. When a thread matches the app and its `channelContext` matches a member, the host renders that member's icon + `displayName` instead of the app-level visual. Each entry is `{ match: string, displayName: string, icon?: string }`: `match` is a case-insensitive substring matched against the thread's `channelContext`; `icon` is a manifest-relative path resolved the same way as the app `icon`. |
| `releasePage` | string | no | Human-readable release page. |
| `nativeApplication` | object | yes | OS app identity and install metadata. |
| `connection` | object | yes | Connection and handoff metadata. |
| `scopes` | AppScopeDescriptor[] | yes | Required scope catalog. Must not be empty. |
| `toolCatalog` | AppToolCatalogEntry[] | yes | Required static tool catalog. May be empty only when `dynamicToolCatalog.enabled` is true. |
| `dynamicToolCatalog` | AppDynamicToolCatalogDescriptor | no | Allows the app to attach a runtime tool catalog during binding attachment. Defaults to disabled. |
| `privacyUrl` | string | no | Optional privacy URL. |
| `termsUrl` | string | no | Optional terms URL. |

### 5.2 Identity and Collision Rules

`appId` rules:

- Must use reverse-DNS form.
- Must be lowercase ASCII.
- Must contain at least three dot-separated labels.
- Labels may contain `a-z`, `0-9`, and hyphen, but must not start or end with hyphen.
- Duplicate effective `appId` descriptors are rejected.

`toolNamespace` rules:

- Must match `^[A-Za-z_][A-Za-z0-9_]*$`.
- Must be unique across the effective app catalog.
- Must not collide with a built-in tool namespace that DotCraft reserves.
- Must not include dots, slashes, whitespace, or display-only punctuation.

`originChannel` rules:

- Optional and free-form (it mirrors a `ChannelName`, not a `toolNamespace`).
- Should be unique across the effective app catalog. If two installed apps declare the same `originChannel`, the host resolves attribution deterministically (lowest `appId` ordinal) and may surface a diagnostic; this is a configuration error, not a hard failure — thread listing is unaffected.
- Attribution is opt-in: a thread is branded only when its `originChannel` equals a declared `originChannel`. A mismatch (or an uninstalled app) silently falls back to the generic channel badge.

`originMembers` rules:

- Requires `originChannel`; ignored without it.
- `match` is matched as a case-insensitive substring of the thread's `channelContext`. The app must choose `match` values that are unambiguous within its own `channelContext` format. Members are evaluated in declared order; the first match wins.
- When no member matches (or the thread has no `channelContext`), the host falls back to the app-level `originChannel` visual.
- `icon` resolution and safety are identical to the app `icon` (host exposes it to clients as a data URL or safe URL).

### 5.3 Native App Requirement

`nativeApplication` describes how Desktop determines whether the required native app is installed and how it should be launched.

Required behavior:

- Desktop should treat OS registration as the product source of truth for app availability.
- Platform-specific identifiers may be used for richer installed-state detection.
- `protocol` is required for `customProtocol` handoff modes. URL-only apps may omit it.
- If availability cannot be determined, Desktop reports `unknown` and may still offer to open the deep link after user confirmation.
- If the app is missing, Desktop opens `installUrl` or `releasePage`; it does not download or install native binaries in v1.

Native app status values:

| Status | Meaning |
|--------|---------|
| `installed` | The OS indicates the app/protocol is registered and launchable. |
| `missing` | The OS indicates the app/protocol is not installed. |
| `unknown` | The client cannot determine availability. |

### 5.4 Scope Descriptor

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `id` | string | yes | App-defined scope id. Must be unique inside the descriptor. |
| `displayName` | string | yes | User-visible scope label. |
| `description` | string | yes | User-visible explanation of what the scope allows. |
| `risk` | `"read" \| "mutate" \| "externalWrite"` | yes | Coarse risk category. |
| `defaultSelected` | boolean | no | UI hint. Defaults to false. |

Risk categories:

| Risk | Meaning | Default Exposure |
|------|---------|------------------|
| `read` | Reads app state without changing app or external systems. | Direct tools may be loaded eagerly. |
| `mutate` | Changes app-owned state, queues operations, or updates local workflow records. | Deferred by default unless app and DotCraft policy allow direct. |
| `externalWrite` | Can publish, send, merge, comment, or otherwise write to an external system. | Deferred by default and should prefer app-side operation requests. |

### 5.5 Tool Catalog Entry

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `name` | string | yes | Dynamic tool `name` without namespace. |
| `scope` | string | yes | Scope id required to expose or call the tool. |
| `risk` | `"read" \| "mutate" \| "externalWrite"` | yes | Tool risk. Must not be lower risk than its scope. |
| `defaultExposure` | `"direct" \| "deferred"` | yes | Default loading group. |
| `description` | string | no | Optional catalog description. The runtime `DynamicToolSpec.description` remains authoritative for the model. |
| `display` | object | no | Optional user-facing display metadata for clients. See [Tool Result Presentation](tool-result-presentation.md#51-display). |
| `_meta` | object | no | Optional interactive UI metadata under `_meta.ui` (UI resource `resourceUri`, tool `visibility`, CSP/permissions). See [Interactive Tool UI](tool-result-presentation.md). |

The catalog is not the executable tool schema. It is a coarse declaration used for discovery, user consent, DotCraft validation, and optional client rendering. Concrete tool schemas are attached later by `app/binding/attachTools`.

### 5.6 Dynamic Tool Catalog Descriptor

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `enabled` | boolean | yes | When true, the app may provide `toolCatalog` entries at `app/binding/attachTools` time. |
| `description` | string | no | User-facing explanation of why the runtime catalog is dynamic. |

Dynamic catalog entries are still bounded by the descriptor's `toolNamespace` and `scopes`. They are useful for native apps whose available tools depend on loaded plugins, editor state, or runtime extension discovery.

### 5.7 Handoff Modes

Supported product handoff modes:

| Mode | Description |
|------|-------------|
| `customProtocol` | Open a registered OS protocol URI such as `oratorio://dotcraft/connect?...`. |
| `url` | Open an HTTPS or localhost URL when the app explicitly uses browser-based consent. |

`localCommand` is not a product App Binding handoff mode. Development harnesses may use private tooling to test app-side flows, but product catalog entries and Desktop UX must not ask users to select or approve arbitrary local executables.

Template variables:

| Variable | Meaning |
|----------|---------|
| `{operation}` | `connect` or `bind`. |
| `{requestId}` | Connection or binding request id. |
| `{requestToken}` | Short-lived, single-purpose handoff token. |
| `{appId}` | App id. |
| `{endpoint}` | Short-lived AppServer handoff endpoint for this request. |
| `{returnTo}` | Optional Desktop return hint. |

Template values embedded in URLs must be URI-escaped.

---

## 6. App and Binding States

### 6.1 App Connection States

| State | Meaning |
|-------|---------|
| `notConnected` | No active app connection credential exists for this workspace, user, and app. |
| `connecting` | A connection handoff is in progress and waiting for app-side confirmation or expiry. |
| `connected` | The app connection credential is valid enough for App Binding methods. |
| `needsAuth` | The app requires user action or account reauthentication. |
| `error` | The last connection attempt failed or the connection cannot be verified. |

### 6.2 Binding States

| State | Meaning |
|-------|---------|
| `pending` | A binding request exists and is waiting for app-side authorization, cancellation, or expiry. |
| `active` | The app grant is valid and tools may be exposed according to granted scopes. |
| `offline` | The grant record exists, but the native app or app-owned tool channel is temporarily unreachable. |
| `expired` | The app grant naturally expired. Calls fail and tools are removed at a safe turn boundary. |
| `revoked` | The user, DotCraft, or the app revoked the binding. Tools must be disabled immediately. |
| `error` | The binding cannot be used because of an unrecoverable or app-reported error. |

State transitions must be auditable. A binding may move from `offline` back to `active` when app-side reattachment succeeds. A binding must not move from `revoked` to `active`; it requires a new binding request.

### 6.3 Binding Kinds and Social Targets

Every binding record has a `bindingKind`:

| Kind | Meaning |
|------|---------|
| `app` | Ordinary app binding. The thread is granted app-owned scopes/tools. |
| `managedApp` | First-party managed runtime binding created by DotCraft without an external native-app handoff. |
| `socialChannel` | A social conversation address is explicitly bound to a thread. Inbound channel messages can route into that thread. |

`Thread.OriginChannel` remains the thread creation attribution and must not be rewritten by `socialChannel` binding. A Desktop-created thread can be bound to QQ, WeCom, or another social channel; later inbound social messages resolve the binding by social address.

A social binding stores:

```json
{
  "bindingKind": "socialChannel",
  "socialTarget": {
    "channelName": "qq",
    "accountId": null,
    "conversationKind": "group",
    "conversationId": "123456",
    "displayName": "Release group",
    "deliveryTarget": "group:123456",
    "boundBy": {
      "platformUserId": "9988",
      "displayName": "Ada"
    }
  }
}
```

`channelName`, `conversationKind`, and `conversationId` are the stable lookup key. `accountId` is optional and lets a multi-account adapter distinguish bot/accounts when it has a stable account id. `deliveryTarget` is adapter-owned and is the value passed back to the channel runtime for outbound delivery.

Within one workspace and app/channel, at most one active unexpired `socialChannel` binding may exist for the same `(channelName, accountId, conversationKind, conversationId)` tuple.

---

## 7. AppServer Capability

Servers that implement this specification advertise:

```json
{
  "capabilities": {
    "appBinding": true,
    "appContextBlocks": true,
    "appThreadInputEnqueue": true
  }
}
```

Clients must check `capabilities.appBinding` before calling `app/*` or `thread/appBindings/*` methods.
Clients must check `capabilities.appContextBlocks` before calling `app/binding/context/*` or `thread/appContextBlocks/*` methods.

Clients must check `capabilities.appThreadInputEnqueue` before calling `app/threadInput/enqueue`.

---

## 8. App Discovery and Status RPCs

### 8.1 `app/list`

Returns plugin-contributed app descriptors merged with catalog/plugin install, native app availability, connection, and optional thread binding summary state.

**Direction**: client -> server

**Params**:

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `includeCatalog` | boolean | no | Include catalog-visible apps that are not installed. Default true. |
| `includeDisabled` | boolean | no | Include apps whose owning plugin is installed but disabled. Default true. |
| `threadId` | string | no | Include binding summaries for this thread. |
| `forceRefresh` | boolean | no | Ask the server to refresh catalog and plugin-derived app metadata. Default false. |
| `surface` | string | no | Caller surface. Valid values are `pluginDetail`, `welcome`, `threadBinding`, and `sdk/default`. Default `sdk/default`. |

**Result**:

```json
{
  "apps": [
    {
      "appId": "com.dotharness.oratorio",
      "toolNamespace": "oratorio",
      "displayName": "Oratorio",
      "developerName": "DotHarness",
      "description": "Manage Oratorio board items and review rounds from selected DotCraft threads.",
      "category": "Developer Tools",
      "pluginId": "oratorio",
      "installed": true,
      "enabled": true,
      "catalogVisible": true,
      "nativeApp": {
        "status": "installed",
        "displayName": "Oratorio",
        "protocol": "oratorio",
        "installUrl": "https://github.com/DotHarness/oratorio/releases"
      },
      "connectionState": "connected",
      "bindingSummary": {
        "threadId": "thread_123",
        "bindingId": "bind_123",
        "appId": "com.dotharness.oratorio",
        "displayName": "Oratorio",
        "icon": "data:image/svg+xml;base64,...",
        "toolNamespace": "oratorio",
        "state": "active",
        "connectionState": "connected",
        "grantedScopes": ["board.read", "board.manage"],
        "expiresAt": null
      }
    }
  ]
}
```

`installed` refers to the owning DotCraft plugin. Native app availability is reported through `nativeApp.status`.

`app/list` is product-surface aware. Plugin-contributed product apps are visible on all user-facing surfaces unless the owning plugin is filtered out. First-party managed runtime descriptors normally stay internal unless they declare an owning plugin and the requested surface is one of its explicitly allowed catalog surfaces. Social channel managed runtimes are the exception: enabled first-party channel runtimes may appear on the `threadBinding` surface as synthetic apps such as `com.dotharness.channel.qq` so Desktop can create a bind-code request for a social conversation.

Managed runtimes that do not require external authorization must report `managed: true` and `requiresExternalConnection: false` on `AppInfoWire`. Clients must not render native app install, deep-link handoff, or "waiting for confirmation in the app" flows for those entries. Enabling such a runtime for a thread creates a managed binding immediately, subject to the owning plugin and surface policy.

### 8.2 `app/view`

Returns one app descriptor and current availability state.

**Direction**: client -> server

**Params**:

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `appId` | string | yes | App id. |
| `threadId` | string | no | Include binding summary for this thread if present. |

**Result**: `{ "app": AppInfo }`

`AppInfo` includes descriptor metadata, native app status, scope descriptors, tool catalog entries, handoff modes, plugin state, connection state, diagnostics, and optional thread binding summary.

`thread/list` and `thread/read` MAY include lightweight `appBindings` summaries with the same summary fields, including `icon`, so Desktop can render thread-level app state without a separate detail fetch.

---

## 9. App Connection RPCs

App connection RPCs operate at workspace + user + app scope. They do not create thread grants.

### 9.1 `app/connection/start`

Starts app connection handoff.

**Direction**: client -> server

**Params**:

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `appId` | string | yes | App id. |
| `handoffMode` | `"customProtocol" \| "url"` | no | Preferred handoff mode. Server may choose another supported mode. |
| `returnTo` | string | no | Client return hint, such as a Desktop route. |

**Result**:

```json
{
  "connectionRequestId": "appconn_req_123",
  "appId": "com.dotharness.oratorio",
  "state": "connecting",
  "expiresAt": "2026-05-17T13:10:00Z",
  "handoff": {
    "mode": "customProtocol",
    "uri": "oratorio://dotcraft/connect?request=appconn_req_123&token=...&endpoint=..."
  }
}
```

Rules:

- The DotCraft plugin/package must be installed and enabled.
- The native app should be installed before Desktop starts the handoff.
- Desktop launches the handoff through OS URL/protocol APIs, not by spawning an executable.
- The app must show connection confirmation in its own UI.
- Connection request tokens are short-lived and single-purpose.

### 9.2 `app/connection/request/get`

Lets the app inspect a pending connection request before showing its confirmation UI.

**Direction**: app -> server

**Params**:

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `connectionRequestId` | string | yes | Pending connection request id. |
| `requestToken` | string | yes | Handoff token. |
| `appId` | string | yes | App id. |

**Result**:

```json
{
  "connectionRequestId": "appconn_req_123",
  "appId": "com.dotharness.oratorio",
  "workspaceLabel": "dotcraft",
  "userLabel": "Local user",
  "expiresAt": "2026-05-17T13:10:00Z"
}
```

The app must use this result, not only deep link query text, to render trustworthy confirmation details.

### 9.3 `app/connection/connect`

Completes an app connection handoff and creates or refreshes the scoped app connection credential.

**Direction**: app -> server

**Params**:

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `connectionRequestId` | string | yes | Pending connection request id. |
| `requestToken` | string | yes | Short-lived single-purpose connection request token. |
| `appId` | string | yes | App id. |
| `accountLabel` | string | no | User-visible app account or workspace label. |
| `expiresAt` | string | no | Optional connection credential expiration timestamp. |
| `connectionProof` | object | no | App-owned proof or metadata needed for later validation. |

**Result**:

```json
{
  "appId": "com.dotharness.oratorio",
  "state": "connected",
  "connectedAt": "2026-05-17T13:00:00Z",
  "expiresAt": null,
  "accountLabel": "Oratorio local"
}
```

Rules:

- The connection request token must match the request id, app id, workspace, and user.
- The token is consumed on success and rejected on replay.
- The created credential is scoped to workspace + user + appId.
- The credential permits only App Binding methods and related status methods.

### 9.4 `app/connection/status`

Returns current connection state for one app.

**Direction**: client -> server

**Params**:

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `appId` | string | yes | App id. |

**Result**:

```json
{
  "appId": "com.dotharness.oratorio",
  "state": "connected",
  "connectedAt": "2026-05-17T13:00:00Z",
  "expiresAt": null,
  "accountLabel": "Oratorio local",
  "diagnostic": null
}
```

### 9.5 `app/connection/revoke`

Revokes the app connection credential for the current workspace, user, and app.

**Direction**: client -> server

**Params**:

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `appId` | string | yes | App id. |
| `reason` | string | no | Optional user-visible reason. |

**Result**: `{ "appId": string, "state": "notConnected" }`

Revoking a connection does not delete historical binding records. Active bindings for that app become `offline` unless the app also reports grant revocation.

### 9.6 `app/connection/refreshMetadata`

Refreshes only the `publicMetadata` of an existing connected connection (for example to re-publish a loopback surface endpoint after the app reopened on a new dynamic port). This does not create connections, does not consume a handoff token, and does not require a `connectionRequestId` or user dialog.

**Direction**: app -> server

**Params**:

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `appId` | string | yes | App id. |
| `connectionProof` | object | yes | The app-owned proof presented at connect; must match the stored proof. |
| `publicMetadata` | object | yes | New public metadata; re-validated by the same loopback sanitizer used by `app/connection/connect`. |

**Result**: the connection status object (same shape as `app/connection/status`), reflecting the refreshed `publicMetadata`.

Rules:

- The target connection must already exist and be `connected`. The method never creates a connection.
- The connection is located by `appId` and an exact match of the stored app-owned `connectionProof` — the refresh is initiated by the app over its own loopback app-server connection, which does not share the Desktop initiator's user id, so authority is the proof, not the caller's user.
- Only `publicMetadata` is updated; `state`, `connectedAt`, `expiresAt`, `accountLabel`, and `connectionProof` are unchanged.
- `publicMetadata` is re-validated by the same loopback-only sanitizer as connect; non-loopback surface endpoints are dropped.
- The refresh is loopback-only and grants no new authority. Desktop surfaces observe the new endpoint on their next `app/connection/status` read.

---

## 10. Binding Request and Attachment RPCs

Binding RPCs create and activate thread grants. They are split into request, inspect, accept, and attach stages so DotCraft and the app can both enforce their own policy.

### 10.1 `app/binding/request/create`

Creates a pending thread binding request and returns an app handoff.

**Direction**: client -> server

**Params**:

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `threadId` | string | yes | Target thread. |
| `appId` | string | yes | App id. |
| `requestedScopes` | string[] | yes | Scopes requested from the descriptor catalog. |
| `requestedTools` | string[] | no | Optional catalog tool names requested by the client. |
| `reason` | string | no | User-visible reason or agent suggestion text. |
| `source` | `"pluginDetail" \| "threadMenu" \| "welcome" \| "agentSuggestion" \| "sdk"` | yes | Binding request origin. |
| `bindingKind` | `"app" \| "socialChannel"` | no | Omitted means `app`. `socialChannel` creates a bind-code handoff instead of an app deep link. |
| `socialIntent` | object | required for `socialChannel` | `{ channelName, targetSelection, displayHint? }`. `targetSelection` is usually `confirmInChannel`, meaning the user must run `/bind <code>` in the target conversation. |

**Result**:

```json
{
  "bindingRequestId": "bind_req_123",
  "threadId": "thread_123",
  "appId": "com.dotharness.oratorio",
  "requestedScopes": ["board.read"],
  "state": "pending",
  "tokenExpiresAt": "2026-05-17T13:10:00Z",
  "handoff": {
    "mode": "customProtocol",
    "uri": "oratorio://dotcraft/bind?request=bind_req_123&token=...&endpoint=..."
  },
  "confirmation": {
    "required": true,
    "risk": "read",
    "message": "Grant Oratorio access to this thread?"
  }
}
```

For a social-channel request, `handoff` uses a short bind code:

```json
{
  "bindingRequestId": "bind_req_qq_123",
  "threadId": "thread_123",
  "appId": "com.dotharness.channel.qq",
  "requestedScopes": ["conversation.receive", "message.send"],
  "state": "pending",
  "tokenExpiresAt": "2026-06-19T13:10:00Z",
  "handoff": {
    "mode": "bindCode",
    "bindCode": "DTC-482913",
    "instructions": "Send /bind DTC-482913 in the QQ conversation to bind it to this thread."
  }
}
```

Rules:

- The DotCraft plugin/package must be installed and enabled.
- The app connection must be usable before creating a binding request.
- DotCraft must require user confirmation before creating or launching a binding request.
- The app must inspect and validate the binding request before accepting. It may auto-accept when the user already selected this connected app in DotCraft and app policy permits it; otherwise it must show its own confirmation UI.
- Pending requests appear in `thread/appBindings/list`.
- Pending requests can be cancelled by DotCraft or the app.
- `socialChannel` requests are accepted by the matching channel adapter after the user proves conversation control with `/bind <code>`.

### 10.2 `app/binding/request/get`

Lets the app inspect a pending binding request before accepting it or showing confirmation UI.

**Direction**: app -> server

**Params**:

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `bindingRequestId` | string | yes for ordinary app handoff | Pending binding request id. |
| `requestToken` | string | yes | Handoff token. For social requests this may be the bind code. |
| `bindCode` | string | no | Alternate social-channel lookup token. |
| `appId` | string | yes | App id. |

**Result**:

```json
{
  "bindingRequestId": "bind_req_123",
  "threadId": "thread_123",
  "threadTitle": "Investigate release blockers",
  "appId": "com.dotharness.oratorio",
  "requestedScopes": [
    {
      "id": "board.read",
      "displayName": "Read boards",
      "description": "Read Oratorio board items, rounds, runs, and source state.",
      "risk": "read"
    }
  ],
  "requestedTools": [
    {
      "name": "ListBoardItems",
      "scope": "board.read",
      "risk": "read",
      "defaultExposure": "direct"
    }
  ],
  "source": "threadMenu",
  "reason": null,
  "expiresAt": "2026-05-17T13:10:00Z",
  "bindingKind": "app",
  "socialIntent": null
}
```

The app must use this result to validate scope, risk, target thread, and tool details before accepting. If the app policy requires additional confirmation, the confirmation UI must be rendered from this trusted result rather than from deep link query text.

For social-channel requests, the channel adapter calls this method with its app id (`com.dotharness.channel.<channelName>`) and the bind code before accepting. Only the matching channel adapter connection may inspect requests for its channel app id.

### 10.3 `app/binding/request/cancel`

Cancels a pending binding request.

**Direction**: client or app -> server

**Params**:

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `bindingRequestId` | string | yes | Pending binding request id. |
| `reason` | string | no | Optional cancellation reason. |

**Result**:

```json
{
  "bindingRequestId": "bind_req_123",
  "threadId": "thread_123",
  "appId": "com.dotharness.oratorio",
  "state": "cancelled"
}
```

### 10.4 `app/binding/accept`

Accepts a pending binding request after app-side authorization.

**Direction**: app -> server

**Params**:

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `bindingRequestId` | string | yes for ordinary app handoff | Pending binding request id. Social adapters may supply it after `app/binding/request/get`; token-only lookup is allowed when the token is unique and valid. |
| `requestToken` | string | yes | Short-lived single-purpose binding request token. |
| `grantId` | string | yes | App-owned grant reference. |
| `grantedScopes` | string[] | yes | Granted scopes. May be narrower than requested scopes. |
| `approvalMode` | string | yes | App-reported approval mode, for example `localUserApproved` or `dotcraftConfigured`. |
| `approvedBy` | string | no | User-visible app account or actor label. |
| `expiresAt` | string | no | Optional grant expiration timestamp. |
| `grantProof` | object | no | App-owned proof or metadata needed for later validation or reattachment. |
| `auditRef` | string | no | App-side audit reference. |
| `socialTarget` | SocialChannelTarget | required for `socialChannel` | Social conversation address accepted by the channel adapter. |

**Result**:

```json
{
  "binding": {
    "bindingId": "bind_123",
    "threadId": "thread_123",
    "appId": "com.dotharness.oratorio",
    "grantId": "grant_123",
    "bindingKind": "app",
    "state": "active",
    "grantedScopes": ["board.read"],
    "attachedToolCount": 0
  }
}
```

Rules:

- The binding request token must match the binding request id, app id, thread id, workspace, requested scopes, and user.
- The token is consumed on success and rejected on replay.
- The app may narrow scopes but must not expand them.
- Acceptance makes the binding active, but tools are not model-visible until attachment succeeds and the next safe exposure boundary is reached.
- `socialChannel` acceptance must come from a connection whose `channelAdapter.channelName` matches `socialTarget.channelName`.
- `socialChannel` acceptance stores the normalized `socialTarget` and enforces the active-target uniqueness rule from §6.3.

### 10.5 `app/binding/attachTools`

Attaches concrete Dynamic Tool specs to an accepted binding.

**Direction**: app -> server

**Params**:

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `bindingId` | string | yes | Active binding id. |
| `threadId` | string | yes | Binding thread id. |
| `appId` | string | yes | App id. |
| `grantId` | string | yes | App grant reference previously accepted. |
| `tools` | DynamicToolSpec[] | yes | Concrete tool specs. |
| `toolCatalog` | AppToolCatalogEntry[] | no | Runtime catalog entries for dynamic catalog apps. Required for attached tools that are not declared in the static descriptor `toolCatalog`. |
| `directToolNames` | string[] | no | Tool names to expose directly. |
| `deferredToolNames` | string[] | no | Tool names to expose deferred. |
| `grantProof` | object | no | App-owned proof or validation material. |

**Result**:

```json
{
  "binding": {
    "bindingId": "bind_123",
    "threadId": "thread_123",
    "appId": "com.dotharness.oratorio",
    "state": "active",
    "attachedToolCount": 2
  },
  "acceptedToolCount": 2,
  "rejectedTools": []
}
```

Rules:

- The caller must be the connected app for the binding's `appId`.
- App, binding, scope, and grant data are validated from outer params and the descriptor catalog.
- Every tool namespace must match the descriptor `toolNamespace`.
- Every tool name must exist in the descriptor `toolCatalog` or in this attachment's `toolCatalog` when `dynamicToolCatalog.enabled` is true.
- Runtime catalog entries may only reference descriptor-declared scopes and must not declare lower risk than their scope.
- Runtime catalog entries may declare `display` and `presentation`, but those declarations are bounded by the same scope, risk, namespace, and validation rules as static catalog entries.
- Attached `DynamicToolSpec.presentation` declarations may narrow the accepted catalog card contract but must not expand the allowed action kinds or callable surface routes beyond it.
- Granted scopes must cover each tool's declared catalog scope.
- `mutate` and `externalWrite` tools default to deferred exposure unless DotCraft policy explicitly allows direct exposure.
- In Responses native deferred-loading mode, app-bound deferred tools are advertised through `tool_search` as namespace loadable tool specs. They are not injected into the top-level model tool list after discovery.

Future app-bound MCP tools must enter through an equivalent attachment path and must not be injected through ordinary workspace or per-thread MCP configuration.

### 10.6 `app/binding/context/upsert`

Creates or replaces one app-provided context block for an active binding. Context blocks are persisted with App Binding state and rendered into the bound thread's fixed App Context prompt section. Model-visible block content is wrapped in the shared `<app-context>` tag. They do not create Turns, Items, thread rollout records, or `ThreadConfiguration` updates.

**Direction**: app -> server

**Params**:

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `bindingId` | string | yes | Active binding id. |
| `appId` | string | yes | App id. Must match the binding. |
| `grantId` | string | yes | App grant reference previously accepted. |
| `blockId` | string | yes | App-owned stable block id within the binding. |
| `kind` | `"role" \| "teamState" \| "mailboxDigest" \| "artifactIndex" \| "policy"` | yes | Rendering and policy hint. |
| `title` | string | yes | Short title. |
| `content` | string | yes | Model-visible content. Maximum 16 KiB. |
| `order` | number | yes | Sort key inside the App Context section. |
| `version` | string | yes | App-owned revision id. |
| `expiresAt` | timestamp | no | Optional block expiry. |
| `visibility` | `"model" \| "hiddenFromModel"` | no | Omitted means `model`. |

Rules:

- `appId` and `grantId` must match the active binding.
- The app connection must be usable.
- A binding may hold at most 32 context blocks.
- `blockId`, `title`, and `version` must not exceed 128 characters.
- Upsert records lifecycle audit and refreshes only the target thread's App Context prompt page.

### 10.7 `app/binding/context/remove`

Removes one app-provided context block from an active binding.

**Direction**: app -> server

**Params**:

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `bindingId` | string | yes | Active binding id. |
| `appId` | string | yes | App id. Must match the binding. |
| `grantId` | string | yes | App grant reference previously accepted. |
| `blockId` | string | yes | Block id to remove. |

Rules:

- `appId` and `grantId` must match the active binding.
- The binding must be active and the app connection must be usable.
- Removing an unknown block is an error.
- Remove records lifecycle audit and refreshes only the target thread's App Context prompt page.

### 10.8 `app/threadInput/enqueue`

Adds app-provided input to the queue of the thread derived from an active binding. This method is the App Binding-safe path for app-owned runtimes such as DotCraft Teams to dispatch work to bound agent threads.

**Direction**: app -> server

**Params**:

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `bindingId` | string | yes | Active binding id. The target thread is derived from this binding. |
| `appId` | string | yes | App id. Must match the binding. |
| `grantId` | string | yes | App grant reference previously accepted. |
| `input` | `InputPart[]` | yes | Input parts to queue. M1 app dispatch is expected to use text input. |
| `displayText` | string | no | Optional UI preview text. |
| `triggerLabel` | string | no | Optional human-readable source label. |
| `triggerRefId` | string | no | Optional app-owned mission/task/id reference. |
| `startPolicy` | `"queueOnly" \| "runWhenIdle"` | no | Omitted means `queueOnly`. |
| `sender` | object | no | Channel/app-provided sender metadata to preserve on the queued user input. Social adapters use this for platform user id, display name, role, and conversation metadata. |

**Result**:

```json
{
  "queuedInput": {
    "id": "queue_...",
    "threadId": "thread_123",
    "status": "queued",
    "triggerKind": "app",
    "triggerLabel": "Board review"
  },
  "queuedInputs": []
}
```

Rules:

- `appId` and `grantId` must match the active binding.
- The app connection must be usable, or the app must be a first-party managed App Binding runtime.
- The binding must be active and unexpired.
- The target thread id is derived from the binding; callers cannot specify or override it.
- The method must reject cross-app, cross-thread, revoked, expired, cancelled, pending, offline, and wrong-grant attempts.
- Input is queued first and must not preempt an active turn.
- `startPolicy = "runWhenIdle"` starts the next queued input only when the target thread has no running, waiting-approval, waiting-input, or maintenance work.
- Queued input preserves `triggerKind`, `triggerLabel`, and `triggerRefId` through dequeue into the future `UserMessagePayload`.
- Queued social input preserves `sender` and the binding id that produced the default output delivery target.
- First-party Teams uses `triggerKind = "team"`; other App Binding apps use `triggerKind = "app"`.
- Enqueue records App Binding audit and emits the normal thread queue notification.

### 10.9 `app/socialBinding/resolve`

Resolves an inbound social-channel message address to an active social binding.

**Direction**: channel adapter -> server

**Params**:

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `appId` | string | yes | Channel app id, for example `com.dotharness.channel.qq`. |
| `channelName` | string | yes | Adapter channel name. Must match the caller's `channelAdapter.channelName`. |
| `accountId` | string | no | Optional bot/account id. Omitted and null are treated as the same empty lookup slot. |
| `conversationKind` | string | yes | Adapter-defined normalized conversation kind such as `group`, `user`, or `room`. |
| `conversationId` | string | yes | Adapter-defined stable conversation id. |

**Result**:

```json
{
  "binding": {
    "bindingId": "bind_qq_123",
    "threadId": "thread_123",
    "appId": "com.dotharness.channel.qq",
    "grantId": "grant_qq_123",
    "bindingKind": "socialChannel",
    "state": "active",
    "socialTarget": {
      "channelName": "qq",
      "conversationKind": "group",
      "conversationId": "123456",
      "deliveryTarget": "group:123456"
    }
  }
}
```

When no active binding exists, the result is `{ "binding": null }`.

Rules:

- Only a channel-adapter connection for the same `channelName` may call this method.
- `appId` must equal `com.dotharness.channel.<channelName>` for first-party channel runtimes.
- Successful results include the active binding's `grantId`; adapters use it when calling `app/threadInput/enqueue`.
- Revoked, expired, pending, cancelled, offline, and wrong-account bindings do not resolve.
- The method is an address lookup, not an enumeration API; callers cannot query across channels.

---

## 11. Thread Binding Management RPCs

### 11.1 `thread/appBindings/list`

Lists bindings and pending binding requests for a thread.

**Direction**: client -> server

**Params**:

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `threadId` | string | yes | Thread id. |
| `includeRevoked` | boolean | no | Include revoked records. Default false. |

**Result**:

```json
{
  "bindings": [
    {
      "bindingId": "bind_123",
      "bindingRequestId": "bind_req_123",
      "threadId": "thread_123",
      "appId": "com.dotharness.oratorio",
      "grantId": "grant_123",
      "bindingKind": "app",
      "displayName": "Oratorio",
      "icon": "data:image/svg+xml;base64,...",
      "toolNamespace": "oratorio",
      "state": "active",
      "connectionState": "connected",
      "grantedScopes": ["board.read"],
      "attachedToolCount": 2,
      "socialTarget": null,
      "exposureRevision": 3,
      "expiresAt": null,
      "lastChangedAt": "2026-05-17T13:00:00Z",
      "diagnostic": null
    }
  ]
}
```

Pending entries use `state: "pending"` and have no granted scopes or attached tools. Social-channel entries include `bindingKind: "socialChannel"` and, once active, their `socialTarget` so Desktop can show the bound conversation label. `exposureRevision` increments when tool exposure, context blocks, or social target metadata changes.

### 11.2 `thread/appContextBlocks/list`

Lists app context blocks for one thread. By default this returns only blocks that would currently be eligible for prompt rendering: active binding, unexpired binding, unexpired block, and `visibility = "model"`.

**Direction**: client -> server

**Params**:

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `threadId` | string | yes | Thread id. |
| `includeInactive` | boolean | no | Include hidden, expired, offline, expired-binding, and revoked-binding blocks. Default false. |

**Result**:

```json
{
  "blocks": [
    {
      "blockId": "mailbox-digest",
      "threadId": "thread_123",
      "bindingId": "bind_123",
      "appId": "com.example.teams",
      "kind": "mailboxDigest",
      "title": "Unread Team Messages",
      "content": "...",
      "order": 30,
      "version": "42",
      "updatedAt": "2026-05-22T10:05:00Z",
      "visibility": "model"
    }
  ]
}
```

### 11.3 `thread/appBindings/revoke`

Revokes one thread binding.

**Direction**: client -> server

**Params**:

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `threadId` | string | yes | Thread id. |
| `bindingId` | string | yes | Binding id. |
| `reason` | string | no | Optional user-visible reason. |

**Result**:

```json
{
  "bindingId": "bind_123",
  "state": "revoked"
}
```

User or app revocation interrupts a running turn, disables tools, and records lifecycle audit.

### 11.4 `thread/appBindings/refresh`

Refreshes binding status and attempts reattachment when possible.

**Direction**: client -> server

**Params**:

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `threadId` | string | yes | Thread id. |
| `bindingId` | string | no | Optional binding id. Omitted means all bindings for the thread. |

**Result**:

```json
{
  "bindings": [
    {
      "bindingId": "bind_123",
      "state": "active",
      "attachedToolCount": 2
    }
  ]
}
```

Refresh requires a valid app connection credential and app-owned grant proof for each binding.

---

## 12. Notifications

### 12.1 `app/list/updated`

Emitted when app catalog, plugin install, native app availability, plugin enablement, or descriptor diagnostics change.

```json
{
  "reason": "plugin/install",
  "appIds": ["com.dotharness.oratorio"]
}
```

### 12.2 `app/connection/changed`

Emitted when an app connection changes state.

```json
{
  "appId": "com.dotharness.oratorio",
  "state": "connected",
  "previousState": "connecting",
  "diagnostic": null
}
```

### 12.3 `thread/appBindings/changed`

Emitted when a thread binding is created, cancelled, accepted, refreshed, moved offline, expired, revoked, or has tools attached.

```json
{
  "threadId": "thread_123",
  "bindingId": "bind_123",
  "bindingRequestId": "bind_req_123",
  "appId": "com.dotharness.oratorio",
  "state": "active",
  "previousState": "pending",
  "changeKind": "accepted"
}
```

Clients should re-read `thread/appBindings/list` or `app/view` when they need full details.

---

## 13. Runtime Tool Exposure

### 13.1 Dynamic-First Transport

M1 App Binding uses Runtime Dynamic Tools as the executable tool transport. App-bound tools differ from ordinary `thread/start.dynamicTools`:

- Ordinary Runtime Dynamic Tools are bound to a declaring AppServer client connection.
- App-bound tools are bound to a persisted Thread App Binding and may be reattached by the app.
- App-bound tools are exposed by DotCraft's binding exposure layer, not directly by a transient client connection.
- Desktop and TUI clients must display app-bound tool invocations as normal tool activity. The AppServer projection is `dynamicToolCall`, so clients must render that item without requiring a companion `toolCall` or `toolResult`.
- App-bound tools may return optional Tool Result Presentation payloads. Presentation is client-only display data and must not replace `contentItems` or `structuredResult`.

### 13.2 Namespace and Catalog Validation

For every attached tool, DotCraft must validate:

- App id exists and the DotCraft plugin is installed and enabled.
- Binding exists, belongs to the target thread, and is usable.
- Tool namespace equals the app descriptor's `toolNamespace`.
- Tool name exists in the descriptor `toolCatalog` or in an accepted attachment-time catalog for a dynamic catalog app.
- Granted scopes cover the tool's declared catalog scope.
- Tool risk and exposure are compatible with descriptor defaults and DotCraft policy.
- Interactive tool UI metadata (`_meta.ui`: resource, visibility, CSP), when declared, is compatible with the accepted App Binding catalog entry and the [Interactive Tool UI](tool-result-presentation.md) contract.

### 13.3 Social-Channel Managed Tools

Managed social-channel apps expose enabled channel runtimes as app-bindable apps, for example `com.dotharness.channel.qq`. Their descriptor `toolNamespace` must equal the normalized channel name.

For a `socialChannel` binding, DotCraft may expose:

- The generic `SendMessageToBoundConversation` tool, which sends text to the bound conversation.
- Runtime-declared native channel tools from the channel adapter, such as image or media send helpers, when the adapter reports a usable tool descriptor and the runtime is ready.

Social-channel app-bound tool execution rules:

- The delivery target is always derived from the active binding's `socialTarget.deliveryTarget`.
- Tool execution must not use `Thread.OriginChannel` to choose the target. `OriginChannel` remains only thread creation attribution.
- Tool arguments must not override the bound social target through fields such as `target`, `deliveryTarget`, `chatId`, `groupId`, `conversationId`, or equivalent channel-specific aliases. DotCraft rejects such calls before dispatch with `AppBindingProtocolViolation`.
- The channel runtime receives the bound channel context and sender metadata from `socialTarget`, not from the caller's Desktop/AppServer execution context.
- If the runtime is offline or no longer advertises the native tool, calls fail with a structured app-binding tool error and must not mutate another conversation.

The legacy `ExternalChannelToolProvider` remains available for old external-channel threads whose tools are selected from `Thread.OriginChannel`. It is a compatibility path only; new social-channel bindings should route through App Binding and the binding's `socialTarget`.

### 13.4 Direct and Deferred Groups

Defaults:

- `read` tools may be direct.
- `mutate` tools are deferred by default.
- `externalWrite` tools are deferred by default and should usually create app-side operation requests.

DotCraft may override direct/deferred placement to satisfy thread policy, user settings, approval requirements, or prompt-cache constraints.

### 13.5 Approval

App-bound tools reuse `DynamicToolSpec.approval` descriptors. DotCraft approval gates happen before dispatch. The app must still enforce its own authorization after dispatch.

High-risk actions should prefer this pattern:

1. The model calls an app-bound tool to propose or queue an operation.
2. The app records an operation request and returns a stable app-side reference.
3. The human approves or publishes in the app.
4. The app performs the external write.

Direct external writes are allowed only when descriptor risk, granted scopes, DotCraft policy, app policy, and user confirmation all allow them.

### 13.6 Stable Offline Stubs

When a binding is `offline`, DotCraft should preserve stable model-visible tool stubs where possible. Calls fail quickly with structured errors.

Standard app binding tool error codes:

| Code | Meaning |
|------|---------|
| `AppBindingOffline` | The binding exists but the app is unreachable. |
| `AppBindingExpired` | The grant expired. |
| `AppBindingRevoked` | The binding was revoked. |
| `AppBindingScopeDenied` | The binding does not grant the required scope. |
| `AppBindingToolUnavailable` | The tool is not currently attached or permitted. |
| `AppBindingProtocolViolation` | The app returned invalid attachment or call data. |

### 13.7 Running Turn Behavior

| Event During Running Turn | Required Behavior |
|---------------------------|-------------------|
| App disconnects | Do not interrupt the running turn. Existing calls return `AppBindingOffline`. |
| Binding completes | Do not expose new tools mid-turn. Tools become visible next turn. |
| Binding naturally expires | Calls return `AppBindingExpired`. Tools are removed after the turn boundary. |
| User or app revokes binding | Interrupt the running turn, disable tools, and record lifecycle audit. |
| Plugin is disabled or removed | Disable exposure at the next safe boundary, interrupting only when required by revocation policy. Preserve records for reconciliation. |

### 13.8 Instructions and Mentions

DotCraft may add generic binding context to the agent, such as available app names, granted scopes, and unavailable-state hints. App-specific operating instructions should come from plugin skills or user-visible app documentation.

`app://<appId>` mentions select or reference an already available app. Mentions do not install plugins, connect apps, create grants, or attach tools by themselves.

---

## 14. Lifecycle Rules

### 14.1 Thread Lifecycle

- Thread archive keeps binding records.
- Thread delete revokes active bindings and records lifecycle audit.
- Thread export should include lightweight binding summaries, not app credentials or secret grant proofs.
- Thread import must not silently reactivate bindings. Imported bindings start unavailable until the user reconnects and rebinds or the app explicitly reattaches under policy.

### 14.2 Subagents and Forks

Subagents and forks do not inherit bindings by default.

A future explicit inheritance or delegation feature must define user confirmation, app approval, scope narrowing, separate audit identity, and clear parent-child binding relationships.

### 14.3 Plugin Lifecycle

- Plugin install makes app descriptors available.
- Plugin enablement makes installed apps eligible for connection and binding.
- Plugin disable disables app-bound tool exposure but preserves connection and binding records for reconciliation.
- Plugin remove disables exposure and preserves records.
- Re-enabling or reinstalling the plugin may reconcile existing records if descriptor identity and app policy still permit it.

### 14.4 Native App Lifecycle

- If the native app is not installed, Desktop shows install/open-download actions instead of connection or binding launch.
- If the native app is closed after a binding is active, DotCraft marks the binding `offline` when the app-owned tool channel is unreachable.
- Reopening the native app may reconnect and reattach non-revoked bindings if app policy allows.

### 14.5 App Connection Lifecycle

- App disconnect moves active bindings to `offline` unless the app also revokes grants.
- App reconnect may refresh or reattach existing non-revoked bindings if app-owned grant proof is still valid.
- Reopening the native app may silently refresh its published loopback surface endpoints via `app/connection/refreshMetadata` (§9.6) so durable Desktop surfaces keep working across app restarts and dynamic-port reallocation, without re-prompting the user.
- Connection credentials are scoped to workspace + user + appId and only permit App Binding methods.

---

## 15. Security Model

### 15.1 Handoff Tokens

Connection and binding handoff tokens:

- Default to a 10-minute TTL.
- Are single-purpose.
- Are bound to one request id, app id, workspace, user, and operation.
- Binding tokens are also bound to thread id and requested scope set.
- Must not be persisted in plaintext.
- Must not be exposed to the model.
- Must be invalidated after success, cancel, expiration, or failed replay.

### 15.2 Handoff Endpoint

The `{endpoint}` template value is a short-lived AppServer endpoint for the specific app handoff. It must not be a general-purpose long-lived AppServer credential.

The handoff endpoint may permit:

- AppServer initialize for an app-side client.
- Inspecting the matching pending request.
- Completing or cancelling the matching request.
- Attaching tools after a binding is accepted.
- Receiving lifecycle notifications needed to keep the app-bound tool channel alive.

It must not permit arbitrary thread execution, normal turn submission, workspace configuration mutation, or non-App-Binding AppServer methods.

### 15.3 App Connection Credential

App connection credentials:

- Are scoped to workspace + user + appId.
- Are revocable by the user.
- Permit only App Binding methods and related status methods.
- Must be stored outside model-visible thread content.

### 15.4 Grant Proof

Grant proof is app-owned. DotCraft stores only the minimum data required to ask the app to revalidate or reattach. Apps must treat DotCraft-stored grant references as identifiers, not as sufficient authorization.

### 15.5 Approval and Audit

DotCraft records:

- Connection request creation, cancellation, acceptance, expiration, and failure.
- Binding request creation, cancellation, acceptance, expiration, refresh, offline transition, and revocation.
- DotCraft-side user confirmation.
- App-reported approval mode and audit reference.
- Tool-call shell audit: thread id, binding id, app id, namespace, tool name, call id, timestamp, success or failure, and stable error code.

The app records:

- Business authorization decisions.
- Resource-level access checks.
- App-side operation requests and external writes.
- Human approval or publish actions inside the app.

### 15.6 Deep Link Safety

Deep links are activation hints, not sufficient authorization.

Requirements:

- The app must inspect the request through AppServer before accepting it or rendering confirmation.
- For connection, the app must show the requesting DotCraft workspace/user, operation, and risk before `app/connection/connect`.
- For binding, the app must validate the requesting workspace/user, requested scopes, requested tools, target thread, and risk before `app/binding/accept`. It may auto-accept a binding when the user already selected the connected app in DotCraft and app policy allows the requested scopes; otherwise it must show app-owned confirmation UI.
- DotCraft must reject expired, replayed, mismatched, or expanded requests.
- DotCraft must not include model-visible secrets in a deep link.

---

## 16. Desktop UX Contract

Desktop exposes three separate user flows:

1. Install or enable the DotCraft plugin.
2. Install or open the native app.
3. Connect the app and bind it to a selected thread.

Required entrypoints:

- Plugin detail page: install plugin, view included app, view required native app, open install page, connect, bind selected thread, reconnect, revoke.
- Thread menu/header: bind, refresh, inspect, open app, cancel pending request, revoke.
- Welcome flow: start a new thread with one or more app bindings before the first turn.

Required behavior:

- Show plugin installation and native app installation as separate states.
- Make clear that plugin installation does not grant thread access.
- Make clear that app connection does not grant thread access.
- Require DotCraft-side confirmation before launching a binding handoff.
- Require app-side confirmation before completing connection. For binding, require app-side authorization before completion; this may be policy-based auto-acceptance after request inspection.
- Show connection and binding states separately.
- For `pending`, show that DotCraft is waiting for confirmation in the native app, with actions to open the app, cancel, or retry.
- For missing native apps, show install/open-download actions instead of local path selection.
- Provide reconnect, refresh, revoke, and retry actions when applicable.
- Show safe error display for `offline`, `expired`, `revoked`, and `error` states.
- When an app-bound `dynamicToolCall` includes a supported Tool Result Presentation payload, render the client-owned presentation instead of making raw JSON the primary visible result.
- If the presentation is unsupported, invalid, or references a disallowed action target, fall back to the generic tool card and safe text/structured output.
- Interactive tool UI runs in a sandboxed iframe; UI-initiated `tools/call` is gated by the binding's granted scopes, risk, and approval, and `ui/open-link` is restricted to `https:` and the bound app's declared protocols. See [Interactive Tool UI](tool-result-presentation.md).
- In the Welcome flow, selected apps must finish app-side binding authorization and attach tools before Desktop submits the first user turn. If any selected app binding is cancelled, errors, or times out, Desktop must keep the draft and must not start the turn.
- Desktop should expose Welcome app selection from the same top-level app binding affordance used by thread headers, not as a Composer footer selector.
- Never let an agent-created suggestion skip user selection or app authorization.

Desktop must not show product users a source path picker, executable path picker, `localCommand` approval, or trusted-root command details for App Binding.

---

## 17. SDK and App Integration Expectations

SDKs should provide app-side helpers for:

- Parsing OS deep link handoff URLs.
- Connecting to the short-lived DotCraft handoff endpoint.
- Inspecting connection and binding requests.
- Completing or cancelling app connection requests.
- Accepting or cancelling thread binding requests.
- Attaching Dynamic Tool specs to a binding.
- Declaring optional tool result presentation contracts and returning runtime `presentation` payloads with safe fallbacks.
- Refreshing or reattaching a binding.
- Revoking a binding.
- Keeping an app-bound tool channel alive while the native app is running.
- Returning structured app binding tool errors.

External apps must:

- Register their OS protocol or native app identity during installation.
- Route deep links to the running app instance when already open.
- Show app-owned confirmation UI for connection, and for binding when app policy requires more than the user's DotCraft-side app selection.
- Validate workspace, user, thread, grant, scope, and resource policy on every call.
- Treat `grantId` and `grantProof` as app-owned references that require app-side validation.
- Narrow scopes when policy requires it.
- Return approval mode and audit identity for every accepted binding.
- Prefer operation-request workflows for high-risk external writes.

Apps must not use general AppServer thread methods as a substitute for App Binding once this platform flow is available.

---

## 18. Oratorio Validation Guidance

Oratorio currently uses run-bound Dynamic Tools to let a DotCraft run submit artifacts back to Oratorio:

- `oratorio.SubmitReviewDraft`
- `oratorio.SubmitImplementationDraft`
- `oratorio.SubmitFollowUpDraft`
- `oratorio.SubmitDiscussionReply`

Those tools remain separate. They are tied to an Oratorio-created run and continue to validate against that run's thread and workflow state.

App Binding covers Oratorio manager-thread board tools. These are tools a user may grant to a selected DotCraft thread so an agent can help manage an Oratorio board.

Oratorio board tools should validate Interactive Tool UI with these first-version expectations:

- `ListBoardItems` declares `_meta.ui.resourceUri = ui://oratorio/board`; the iframe renders the board, "Open in Oratorio" uses `ui/open-link` (no tool call), and refresh re-`fetch`es the app's loopback backend under CSP `connectDomains`.
- `GetBoardItem` declares `ui://oratorio/item` for one item plus activity.
- `QueueReviewRound` declares `ui://oratorio/review`; the queue action uses `tools/call` (risk `externalWrite` → approval) or an app-side operation request.
- Non-Desktop clients (TUI, channels) fall back to the tool result's text (`structuredResult` / `contentItems`).

Product validation requires:

- Oratorio Desktop registers an OS protocol such as `oratorio://`.
- DotCraft launches Oratorio Desktop through the OS deep link, not by spawning `Oratorio.Server.exe`.
- Oratorio Desktop supports single-instance deep link routing.
- Oratorio Desktop shows connection confirmation for `connect` handoffs.
- Oratorio Desktop auto-accepts `bind` handoffs after AppServer inspection when the request matches the connected DotCraft workspace, requested Oratorio scopes, and current app policy.
- Oratorio Desktop calls AppServer to inspect, accept, and attach tools after connection confirmation or binding authorization.
- Oratorio Desktop keeps the app-bound tool channel alive while the app is running.
- Closing Oratorio makes active bindings become `offline` in DotCraft until the app reconnects.
- Oratorio persists its DotCraft binding (resolved app-server endpoint, appId, and the app-owned connection proof) and, on startup, re-announces its current loopback `apiBase` via `app/connection/refreshMetadata` (§9.6). Because Oratorio Desktop allocates a dynamic loopback port per launch, this lets the embedded board reconnect after a restart with no manual re-bind. Oratorio Desktop should also reuse a stable loopback port across restarts when available to minimize churn.

Initial Oratorio manager tools:

- `oratorio.ListBoardItems`
- `oratorio.GetBoardItem`
- `oratorio.CreateBoardTask`
- `oratorio.QueueReviewRound`

Read tools are direct by default. Write and queue tools are deferred or approval-gated.

Development-only headless helpers may exist for tests, but they are not the user-facing App Binding product flow.

---

## 19. Compatibility

Existing Runtime Dynamic Tools remain supported:

- `thread/start.dynamicTools`
- `thread/resume.dynamicTools`
- `item/tool/call`

Existing plugin MCP behavior remains supported:

- Workspace MCP configuration.
- Per-thread `ThreadConfiguration.McpServers`.
- Plugin-bundled MCP declarations.

App-bound tools are a separate exposure layer. MCP may be used as an implementation transport later, but app-bound MCP tools must be mediated by App Binding and must not gain authority merely because an MCP server is globally configured.

The previous development-style `localCommand` App Binding handoff is removed from product conformance. Implementations must not expose it in catalog UX or require it for user validation.

---

## 20. Conformance Reference

App Binding conformance is defined by the behavior in this specification, with coverage focused on:

- Descriptor validation and discovery through plugin `apps` contributions.
- Connection, binding request, acceptance, attachment, revoke, refresh, and notification RPC behavior.
- Token handling, app connection credential scope, grant scope narrowing, audit identity, and plaintext-token avoidance.
- Binding state transitions, including pending, active, offline, expired, revoked, cancelled, and error states.
- Runtime tool exposure through the App Binding layer, including namespace validation, scope checks, deferred/direct defaults, unavailable errors, and running-turn behavior.
- Thread, plugin, native app, Desktop UX, SDK helper, and Oratorio reference flows.

Persisted App Binding state is workspace scoped under `.craft/app-bindings/state.json`. AppServer reports native app status as `unknown`; Desktop may refine that status to `installed` or `missing` by checking the registered OS protocol handler.

---

## 21. Open Questions

- Whether `app/list` needs pagination once a remote catalog exists.
- Whether DotCraft should expose a first-class app audit viewer, or only lifecycle summaries in thread history.
- Whether future app-bound MCP tools should reuse app-owned MCP server sessions or create binding-scoped MCP sessions.
