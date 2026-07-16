# Desktop 扩展

Desktop 扩展让插件在 **DotCraft Desktop 内**渲染自己的界面——一个像内置页面那样打开的完整视图——而不只是贡献工具和技能。扩展 bundle 作为可信本地代码运行在 Desktop renderer 中，并通过固定的宿主桥（host bridge）与 Desktop 的其余部分交互。

本页面面向插件作者。插件的用户视角见[插件与工具](../../features/agent-system/plugins-tools)；把原生 App 的工具连接到会话见 [App Binding](./app-binding)。

![Oratorio Desktop 扩展](https://github.com/DotHarness/resources/raw/master/dotcraft/whats-new/desktop-extensions.gif)

旗舰示例是 **Oratorio** 看板：它的插件既贡献了一个 [App Binding](./app-binding)——让会话能读取与管理看板条目——又贡献了一个把看板嵌入为 main view 的 Desktop 扩展。

> [!NOTE]
> 扩展是 **UI 层**，App Binding 是 **工具层**。两者相互独立，但可以自然配合。已连接的 App 可以为扩展 UI 发布短期 App Surface，而会话工具继续使用 App Binding。

## Surfaces

一个扩展声明一个或多个 **surface**——它接入 Desktop 的插槽。每个 surface 有一个 `type`：

| Surface `type` | 渲染位置 |
|---|---|
| `mainView` | 一个完整的 main view，像 Conversation、Teams 一样从侧边栏打开。 |

`mainView` 是 Desktop 当前会渲染的 surface，Oratorio 看板用的就是它。每个 `mainView` 声明一个 `viewId`、一个 `label`（可带按语言区分的 `localizedLabel`）、一个会被解析为内置 Desktop 图标的 `icon`，以及决定列表位置的 `order`。

## 宿主桥（host bridge）

Desktop 渲染你的 surface 组件，并向它传入单个 `host` 对象——这是访问 Desktop 的唯一受认可入口：

| 区域 | 提供能力 |
|---|---|
| `react` | 用于渲染的 Desktop React 实例——不要自带。 |
| `plugin` / `extension` | 身份信息：id、显示名，以及插件的 `rootPath`。 |
| `appBindings` | 针对已声明 App 的 `getConnectionStatus`、`startConnection`、`openApp`。 |
| `appSurfaces` | 针对 descriptor 已授权、由 App 发布的 surface 提供 `getJson` / `postJson`。 |
| `navigation` | `setActiveMainView` 与 `openThread`，用于在 Desktop 内跳转。 |
| `ui` | `showToast`——原生 toast，可带内联操作与 `onExpire` 回调。 |
| `components` | 可复用的 Desktop 共享组件，例如 `TeamsView`。 |

surface 需要从 Desktop 取用的一切都经由 `host`。触及 App 的能力由扩展 descriptor 约束，并由 Desktop 主进程强制执行。

## 在 manifest 中声明

插件在 `plugin.json` 里指向一份 Desktop 扩展文档，方式与指向 `apps` 文档相同：

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

该文档列出每个扩展、它的 entry bundle、surfaces 与所需 App Surfaces：

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

关键规则：

- **`entry`**——以及每个 `styles` 路径——都相对于 manifest，且必须留在插件根目录内。只有插件安装并启用后，Desktop 才会加载该 bundle。
- **`requiredAppSurfaces`** 是 App 自有扩展 API 的完整 allow-list。每项指定 `appId`、`surfaceId`，以及非空的 `access` 数组；数组可包含 `read`、`write` 或两者。
- `read` 启用 `host.appSurfaces.getJson(appId, surfaceId, path)`；`write` 启用 `host.appSurfaces.postJson(appId, surfaceId, path, body)`。
- **`requiredAppIds`** 独立限定 `appBindings` 的连接状态、启动连接与打开 App helper；声明 surface 不会隐式授予这些 helper。
- 省略 `requiredAppSurfaces` 或传入空数组时，不授予任何 App Surface 访问权。

## 调用 App Surface

已连接的原生 App 向 AppServer 发布当前 loopback HTTP(S) endpoint 和 bearer。每次发布的有效期固定为两分钟。再次发布相同的 `appId` 与 `surfaceId` 会替换 endpoint 和 bearer，并把 lease 续期两分钟；因此 App 应在到期前重新发布，并在本地端口或凭据变化时立即发布。

扩展代码只提供已声明的 id 与相对路径：

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

路径必须以 `/` 开头，且不能包含 scheme、host、user info 或 fragment。不要传入绝对 URL。Desktop 主进程会检查 `requiredAppSurfaces`、解析当前发布记录、把请求代理到其 loopback endpoint，并注入 `Authorization: Bearer <token>`。endpoint 与 bearer 永远不会暴露给扩展代码。

如果 App 尚未发布该 surface，或其两分钟 lease 已过期，调用会以 `AppSurfaceUnavailable` 失败。此时应提示用户连接或重新打开 App；不要回退到 renderer 直接联网。

## 信任模型

扩展 bundle 作为**可信本地 UI**运行在 Desktop renderer 中——它不是不可信代码沙箱。descriptor 仍会限制 App 访问：Desktop 主进程强制执行 `requiredAppSurfaces`，只接受相对路径，并负责 surface 解析、loopback 代理与 bearer 注入。正因为 bundle 是受信任的，只应安装并启用你信任来源的插件。

## 相关文档

- [App Binding](./app-binding) — 把原生 App 的工具授予某个会话。
- [Build an App](./build-an-app) — App Binding 开发者指南。
- [插件与工具](../../features/agent-system/plugins-tools) — 插件如何打包工具、技能与扩展。
