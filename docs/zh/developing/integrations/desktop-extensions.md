# Desktop 扩展

Desktop 扩展让插件在 **DotCraft Desktop 内**渲染自己的界面——一个像内置页面那样打开的完整视图——而不只是贡献工具和技能。扩展的 bundle 作为可信本地代码运行在 Desktop renderer 中，并通过一套固定的宿主桥（host bridge）与 Desktop 的其余部分交互。

本页面向插件作者。插件的用户视角见 [插件与工具](../../features/agent-system/plugins-tools)；把原生 App 的工具连到某个会话见 [App Binding](./app-binding)。

![Oratorio Desktop 扩展](https://github.com/DotHarness/resources/raw/master/dotcraft/whats-new/desktop-extensions.gif)

旗舰示例是 **Oratorio** 看板:它的插件既贡献了一个 [App Binding](./app-binding)——让会话能读取与管理看板条目——又贡献了一个把看板嵌入为 main view 的 Desktop 扩展。

> [!NOTE]
> 扩展是 **UI 层**,App Binding 是 **工具层**。两者相互独立——扩展可以是纯 UI——但天然成对:扩展通过 binding 读取 App 数据并触发操作。

## Surfaces

一个扩展声明一个或多个 **surface**——它接入 Desktop 的插槽。每个 surface 有一个 `type`:

| Surface `type` | 渲染位置 |
|---|---|
| `mainView` | 一个完整的 main view,像 Conversation、Teams 一样从侧边栏打开。 |

`mainView` 是 Desktop 当前会渲染的 surface,Oratorio 看板用的就是它。每个 `mainView` 声明一个 `viewId`、一个 `label`(可带按语言区分的 `localizedLabel`)、一个会被解析为内置 Desktop 图标的 `icon`,以及决定列表位置的 `order`。

## 宿主桥（host bridge）

Desktop 渲染你的 surface 组件,并向它传入单个 `host` 对象——这是访问 Desktop 的唯一受认可入口:

| 区域 | 提供能力 |
|---|---|
| `react` | 用于渲染的 Desktop React 实例——不要自带。 |
| `plugin` / `extension` | 身份信息:id、显示名,以及插件的 `rootPath`。 |
| `appBindings` | 针对扩展所绑定 App 的 `getConnectionStatus`、`startConnection`、`openApp`。 |
| `network` | `getJson` / `postJson`,仅限扩展声明的 origin。 |
| `navigation` | `setActiveMainView` 与 `openThread`,用于在 Desktop 内跳转。 |
| `ui` | `showToast`——原生 toast,可带内联操作与 `onExpire` 回调。 |
| `components` | 可复用的 Desktop 共享组件,例如 `TeamsView`。 |

surface 需要从 Desktop 取用的一切都经由 `host`。触及所绑定 App 或网络的能力,由扩展的 descriptor 约束(见下)。

## 在 manifest 中声明

插件在 `plugin.json` 里指向一份 Desktop 扩展文档,方式与指向 `apps` 文档相同:

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

该文档列出每个扩展、它的 entry bundle 与 surfaces:

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

关键规则:

- **`entry`**——以及每个 `styles` 路径——都相对于 manifest,且必须留在插件根目录内。只有插件安装并启用后,Desktop 才会加载该 bundle。
- **`requiredAppIds`** 列出该 surface 依赖的 [App Binding](./app-binding);它们对 `appBindings` 桥起门控作用。
- **`connectOrigins`** 是 `network.getJson` / `postJson` 可访问主机的白名单。其余一律拒绝。
- **`surfaceWriteScopes`** 声明该 surface 可用于写操作的 App scope,并按会话的 binding 强制校验。

## 信任模型

扩展 bundle 作为 **可信本地 UI** 运行在 Desktop renderer 中——它**不是**不可信代码沙箱。约束它的是 descriptor:Desktop 主进程强制执行 `requiredAppIds`、`connectOrigins` 与 `surfaceWriteScopes`,且 surface 只能通过 `host` 桥访问 Desktop。正因为 bundle 是受信任的,与任何插件工具同理——只安装并启用你信任来源的插件。

## 参见

- [App Binding](./app-binding) — 把原生 App 的工具授予某个会话。
- [Build an App](./build-an-app) — App Binding 的开发者指南(handoff、scope、工具)。
- [插件与工具](../../features/agent-system/plugins-tools) — 插件如何打包工具、技能与扩展。
