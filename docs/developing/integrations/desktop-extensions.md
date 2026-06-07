# Desktop Extensions

A Desktop extension lets a plugin render its own UI **inside DotCraft Desktop** — a full view you open like any built-in screen — instead of only contributing tools and skills. The extension's bundle runs as trusted local code in the Desktop renderer and reaches the rest of Desktop through a fixed host bridge.

This page targets plugin authors. For the user-facing view of plugins, see [Plugins & Tools](../../features/agent-system/plugins-tools); for connecting a native app's tools to a thread, see [App Binding](./app-binding).

![Oratorio Desktop extension](https://github.com/DotHarness/resources/raw/master/dotcraft/whats-new/desktop-extensions.gif)

The flagship example is the **Oratorio** board: its plugin contributes both an [App Binding](./app-binding) — so a thread can read and manage board items — and a Desktop extension that embeds the board as a main view.

> [!NOTE]
> An extension is the **UI layer**; App Binding is the **tool layer**. They are independent — an extension can be pure UI — but they pair naturally: the extension reads the app's data and triggers actions through the binding.

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
| `appBindings` | `getConnectionStatus`, `startConnection`, and `openApp` for the extension's bound app. |
| `network` | `getJson` / `postJson`, restricted to the origins the extension declares. |
| `navigation` | `setActiveMainView` and `openThread` to move around Desktop. |
| `ui` | `showToast` — a native toast with an optional inline action and an `onExpire` callback. |
| `components` | Shared Desktop components you can reuse, such as `TeamsView`. |

Everything the surface needs from Desktop comes through `host`. Capabilities that reach the bound app or the network are gated by the extension's descriptor (below).

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

The document lists each extension, its entry bundle, and its surfaces:

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
      "connectOrigins": ["https://api.oratorio.app"],
      "surfaceWriteScopes": ["board.manage"]
    }
  ]
}
```

Key rules:

- **`entry`** — and every `styles` path — is manifest-relative and must stay inside the plugin root. Desktop loads the bundle only after the plugin is installed and enabled.
- **`requiredAppIds`** names the [App Binding](./app-binding)(s) the surface depends on; they gate the `appBindings` bridge.
- **`connectOrigins`** allow-lists the hosts `network.getJson` / `postJson` may reach. Anything else is refused.
- **`surfaceWriteScopes`** declares which app scopes the surface may use for write actions, enforced against the thread's binding.

## Trust model

An extension bundle runs as **trusted local UI** in the Desktop renderer — it is not an untrusted code sandbox. What keeps it bounded is the descriptor: Desktop's main process enforces `requiredAppIds`, `connectOrigins`, and `surfaceWriteScopes`, and the surface can only reach Desktop through the `host` bridge. Because the bundle is trusted, the same rule as any plugin tool applies — install and enable only plugins whose source you trust.

## See Also

- [App Binding](./app-binding) — grant a thread access to a native app's tools.
- [Build an App](./build-an-app) — the App Binding builder's guide (handoff, scopes, tools).
- [Plugins & Tools](../../features/agent-system/plugins-tools) — how plugins package tools, skills, and extensions.
