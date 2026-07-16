# Desktop Extensions

A Desktop extension lets a plugin render its own UI **inside DotCraft Desktop** — a full view you open like any built-in screen — instead of only contributing tools and skills. The extension's bundle runs as trusted local code in the Desktop renderer and reaches the rest of Desktop through a fixed host bridge.

This page targets plugin authors. For the user-facing view of plugins, see [Plugins & Tools](../../features/agent-system/plugins-tools); for connecting a native app's tools to a thread, see [App Binding](./app-binding).

![Oratorio Desktop extension](https://github.com/DotHarness/resources/raw/master/dotcraft/whats-new/desktop-extensions.gif)

The flagship example is the **Oratorio** board: its plugin contributes both an [App Binding](./app-binding) — so a thread can read and manage board items — and a Desktop extension that embeds the board as a main view.

> [!NOTE]
> An extension is the **UI layer**; App Binding is the **tool layer**. They are independent, but they pair naturally. A connected app can publish a short-lived App Surface for its extension UI while its thread tools continue to use App Binding.

## Surfaces

An extension declares one or more **surfaces** — the slots in Desktop it plugs into. Each surface has a `type`:

| Surface `type` | Where it renders |
|---|---|
| `mainView` | A full main view, opened from the sidebar like Conversation or Teams. |

`mainView` is the surface Desktop renders today, and the Oratorio board uses it. Each `mainView` declares a `viewId`, a `label` (with an optional per-locale `localizedLabel`), an `icon` resolved to a built-in Desktop icon, and an `order` for its place in the list.

## The host bridge

Desktop renders your surface component and passes it a single `host` object — the sanctioned way to reach Desktop:

| Area | What it provides |
|---|---|
| `react` | The Desktop React instance to render with — don't bundle your own. |
| `plugin` / `extension` | Identity: ids, display names, and the plugin `rootPath`. |
| `appBindings` | `getConnectionStatus`, `startConnection`, and `openApp` for declared apps. |
| `appSurfaces` | `getJson` / `postJson` for descriptor-authorized, app-published surfaces. |
| `navigation` | `setActiveMainView` and `openThread` to move around Desktop. |
| `ui` | `showToast` — a native toast with an optional inline action and an `onExpire` callback. |
| `components` | Shared Desktop components you can reuse, such as `TeamsView`. |

Everything the surface needs from Desktop comes through `host`. Capabilities that reach an app are gated by the extension descriptor and enforced by the Desktop main process.

## Declare it in the manifest

A plugin points at a Desktop-extensions document from its `plugin.json`, the same way it points at an `apps` document:

```json
{
  "schemaVersion": 1,
  "id": "oratorio",
  "displayName": "Oratorio",
  "capabilities": ["app", "desktopExtension"],
  "apps": "./apps.json",
  "desktopExtensions": "./desktop-extensions.json"
}
```

The document lists each extension, its entry bundle, surfaces, and required App Surfaces:

```json
{
  "extensions": [
    {
      "id": "oratorio-board",
      "displayName": "Oratorio Board",
      "description": "Shows the Oratorio board inside DotCraft Desktop.",
      "entry": "./desktop/board.js",
      "styles": ["./desktop/board.css"],
      "surfaces": [
        {
          "type": "mainView",
          "viewId": "board",
          "label": "Board",
          "localizedLabel": { "zh-Hans": "看板" },
          "icon": "dashboard",
          "order": 10
        }
      ],
      "requiredAppIds": ["com.dotharness.oratorio"],
      "requiredAppSurfaces": [
        {
          "appId": "com.dotharness.oratorio",
          "surfaceId": "board",
          "access": ["read", "write"]
        }
      ]
    }
  ]
}
```

Key rules:

- **`entry`** — and every `styles` path — is manifest-relative and must stay inside the plugin root. Desktop loads the bundle only after the plugin is installed and enabled.
- **`requiredAppSurfaces`** is the complete allow-list for app-owned extension APIs. Each entry identifies an `appId` and `surfaceId`, with a non-empty `access` array containing `read`, `write`, or both.
- `read` enables `host.appSurfaces.getJson(appId, surfaceId, path)`. `write` enables `host.appSurfaces.postJson(appId, surfaceId, path, body)`.
- **`requiredAppIds`** independently scopes the `appBindings` connection-status, start, and open helpers. A surface grant does not grant those helpers.
- An omitted or empty `requiredAppSurfaces` grants no App Surface access.

## Call an App Surface

The connected native app publishes its current loopback HTTP(S) endpoint and bearer to AppServer. Each publication lasts two minutes. Publishing the same `appId` and `surfaceId` again replaces the endpoint and bearer and renews the two-minute lease, so the app should republish before expiry and whenever its local port or credential changes.

Extension code supplies only the declared ids and a relative path:

```js
const board = await host.appSurfaces.getJson(
  "com.dotharness.oratorio",
  "board",
  "/api/board"
)

await host.appSurfaces.postJson(
  "com.dotharness.oratorio",
  "board",
  "/api/cards/move",
  { cardId, columnId }
)
```

The path must begin with `/` and cannot contain a scheme, host, user info, or fragment. Do not pass an absolute URL. Desktop main checks `requiredAppSurfaces`, resolves the current publication, proxies the request to its loopback endpoint, and injects `Authorization: Bearer <token>`. The endpoint and bearer are never exposed to extension code.

If the app has not published the surface or its two-minute lease has expired, the call fails with `AppSurfaceUnavailable`. Prompt the user to connect or reopen the app; do not fall back to direct renderer networking.

## Trust model

An extension bundle runs as **trusted local UI** in the Desktop renderer — it is not an untrusted code sandbox. The descriptor still bounds app access: Desktop main enforces `requiredAppSurfaces`, only accepts relative paths, and owns surface resolution, loopback proxying, and bearer injection. Because the bundle is trusted, install and enable only plugins whose source you trust.

## Related docs

- [App Binding](./app-binding) — grant a thread access to a native app's tools.
- [Build an App](./build-an-app) — the App Binding builder's guide.
- [Plugins & Tools](../../features/agent-system/plugins-tools) — how plugins package tools, skills, and extensions.
