# 开发 Desktop Plugin

Desktop Plugin 为 DotCraft Desktop 添加可信的 TypeScript 与 React UI。它可以贡献视图、设置、命令、操作和工具呈现，并与同一个 DotCraft Plugin 中的 skills、tools、apps 或 .NET 代码一起分发。

本页面向插件开发者。使用 `$plugin-creator` 生成推荐的项目结构，并通过 `@dotcraft/plugin` 使用公共契约、共享 UI 组件与构建命令。

> [!CAUTION]
> Desktop Plugin 在 DotCraft renderer 中运行，无论通过何种来源分发，都获得相同的 Host API。安装并启用插件就是信任决定；这里没有按插件划分的权限层，也没有 JavaScript 沙箱。不受信任的交互式工具 UI 应使用 [MCP Apps](./mcp-apps)。

## 创建插件

让 `$plugin-creator` 创建带 Desktop 支持的插件。生成的 Desktop 项目属于插件 bundle：

```text
.craft/plugins/acme-board/
├── .craft-plugin/
│   └── plugin.json
└── desktop/
    ├── package.json
    ├── tsconfig.json
    └── src/
        ├── index.css
        └── index.tsx
```

Scaffold 会把 `@dotcraft/plugin` 固定为创建它的 DotCraft 版本。请让该包与加载插件的 Desktop 版本保持一致。

## 声明 Desktop 模块

在 `.craft-plugin/plugin.json` 中内联声明一个 Desktop 模块：

```json
{
  "schemaVersion": 1,
  "id": "acme-board",
  "version": "1.0.0",
  "displayName": "Acme Board",
  "desktop": {
    "entry": "./desktop/dist/index.mjs",
    "styles": ["./desktop/dist/index.css"]
  }
}
```

`version` 必须存在，并使用规范的 `MAJOR.MINOR.PATCH` 格式。`entry` 必须指向 `./desktop/dist/` 下已经存在的 `.mjs` 文件。可选 `styles` 中的每一项都必须指向同一输出树下已经存在的 `.css` 文件。导入的 chunks 与 assets 也必须留在该树中。

Desktop 模块共享父插件的 id、version、启用状态和 interface metadata。

## 激活插件

从 `desktop/src/index.tsx` 导出具名 `activate` 函数。它接收 `DesktopPluginHost`，并返回完整的贡献 generation：

```tsx
import {
  Button,
  type DesktopPluginActivate,
  type DesktopPluginViewProps,
} from "@dotcraft/plugin";
import "./index.css";

function BoardView({ host }: DesktopPluginViewProps) {
  return (
    <main className="acme-board">
      <h1>Acme Board</h1>
      <Button
        onClick={() => host.ui.showToast({ message: "Board is ready." })}
      >
        Check status
      </Button>
    </main>
  );
}

export const activate: DesktopPluginActivate = () => ({
  mainViews: [
    {
      id: "board",
      label: { default: "Acme Board" },
      component: BoardView,
    },
  ],
});
```

贡献 id 在整个插件的激活结果中必须唯一。使用 `label.translations` 提供本地化标签；只有在位置顺序有意义时才设置可选的 `order`。

## 选择贡献点

`DesktopPluginActivation` 接受七类贡献数组：

| 字段 | 位置与行为 |
|---|---|
| **`mainViews`** | 向 Desktop 导航添加完整视图。 |
| **`settingsPages`** | 向 Desktop Settings 添加页面。 |
| **`conversationViews`** | 在宿主自有的 Chat 视图旁添加 thread 级 tab。 |
| **`commands`** | 添加可搜索命令，可带可用性判断与 `execute` 回调。 |
| **`toolRenderers`** | 渲染一个精确的 `presentationId`；没有匹配插件 renderer 时，Desktop 继续使用优化 renderer 与通用 fallback。 |
| **`composerActions`** | 向 composer 操作区添加组件，并获得只读 thread 与 mode context。 |
| **`messageActions`** | 向 assistant message 添加操作，并获得只读 message model。 |

激活结果还可以提供 `dispose()`。贡献组件通过 typed props 接收 `host` 与 contribution id；conversation 与 presentation 贡献还会接收对应的只读 model。

## 使用 Host API

`DesktopPluginHost` 提供产品操作，但不暴露 Desktop stores、Electron IPC、插件文件系统路径或产品功能组件。

| 区域 | 公共操作 |
|---|---|
| **`plugin`** | 读取 `id`、`version` 与 `displayName`。 |
| **`environment`** | 读取当前 `locale` 与 `theme`。 |
| **`navigation`** | 调用 `openMainView`、`openSettingsPage` 或 `openThread`，并通过 `onOpenUrl` 订阅 Desktop custom-scheme URL。 |
| **`ui`** | 调用 `showToast` 或 `confirm`。 |
| **`appServer`** | 发送遵循生成契约的 `request`，并通过 `onNotification` 订阅通知。 |
| **`appBindings`** | 调用 `getConnectionStatus`、`startConnection` 或 `openNativeApp`。 |
| **`appSurfaces`** | 通过 Desktop 的 App Surface proxy 调用 `getJson` 或 `postJson`。 |
| **`workspaces`** | 使用 `listLocalProjects` 列出本地项目。 |
| **`oratorio`** | 调用 `getContext`、`request`、`retry`、`getPendingHandoff`、`resolveHandoff`、`focusRun` 或 `onEvent`。 |

`navigation.onOpenUrl` 会把 scheme 不是 HTTP、HTTPS 或 `mailto` 的绝对 URL 分发给已激活的 Desktop Plugins。Listener 处理 URL 后返回 `true`；Desktop 会按稳定的插件顺序在首个处理者处停止。如果没有 listener 处理，Desktop 会拒绝该 URL，不会把它交给操作系统 shell。HTTP、HTTPS 与 `mailto` URL 继续使用现有的 AppServer 校验与 shell 路径。App Surface 调用只接收相对路径，Desktop Main 负责解析本地 endpoint 并注入 bearer。

从 `@dotcraft/plugin` 导入共享 UI 组件。除了字段与操作原语（`Button`、`IconButton`、`Input`、`Textarea`、`Select`、`Checkbox`、`Spinner` 和 `Skeleton`），该包还提供 bundled plugins 使用的聚焦组合组件（`ActionTooltip`、`Combobox`、`ModalHeader`、`PillSwitch`、`SettingsPanelShell`、`SettingsBreadcrumb`、`SettingsGroup`、`SettingsRow`，以及窄接口的 `InlineDiff` adapter）。React hooks 与 JSX 使用 Desktop 自有的 React runtime；官方 builder 会阻止第二份 React runtime 进入输出。Contribution 的 `icon` 可以是插件 React component，也可以是由 Desktop 解析的 string token。

插件 generation 停止时，宿主会移除 Host 自有的 subscriptions 与 toasts。主进程操作会对每个 Desktop Plugin 继续执行正常的 URL、route、bearer、size 与 timeout 校验。

## 构建并加载

在生成的 `desktop/` 目录安装依赖并构建：

```bash
npm install
npm run build
```

Scaffold 会先执行 TypeScript 检查，再运行 `dotcraft-plugin build`。Builder 把 `src/index.tsx`、导入的 CSS、chunks 和 assets 打包到 `dist/`，同时把 React 与共享组件连接到 Desktop runtime。

可运行的 [DotNetPluginSample](https://github.com/DotHarness/dotcraft/tree/main/sdk/dotnet/samples/DotNetPluginSample) 展示了如何在同一个插件中组合 .NET tool、Desktop view 与 renderer。

请在 DotCraft 发现插件或打包插件之前完成构建。刷新插件列表，然后启用插件以激活它的 Desktop 模块。

Desktop 会把一个插件 revision 的全部贡献作为单个 generation 发布。已经激活的相同 revision 不发生变化。更新、禁用、卸载或关闭 Desktop 会撤销整个 generation、移除其 styles 与 Host 自有 subscriptions，并调用 `dispose()`。

Desktop 不会从远程 AppServer 加载可执行插件代码。Desktop 使用远程 workspace 时，只会激活本地已经打包，并且 plugin id、version 与 Desktop content revision 都和远程插件 snapshot 一致的代码。

## 相关文档

- [插件市场](./plugin-market)
- [开发 .NET 插件](./dotnet-plugins)
- [MCP Apps](./mcp-apps)
- [插件与工具](../../features/agent-system/plugins-tools)
