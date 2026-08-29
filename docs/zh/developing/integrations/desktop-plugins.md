# 开发 Desktop Plugin

Desktop Plugin 可以为 DotCraft Desktop 添加完全可信的 TypeScript 与 React 行为。它直接运行在 renderer 中，可以扩展 Core UI、替换或包装公共 surface、提供 service、响应 event，也可以创建供其他插件扩展的新 surface。

本页面面向插件开发者。使用 `$plugin-creator` 生成推荐的项目结构，并通过 `@dotcraft/plugin` 使用公共 runtime、React 组件与构建命令。

> [!CAUTION]
> 安装并启用 Desktop Plugin 会在 DotCraft renderer 中执行它。Desktop Plugin 没有权限层、沙箱或独立的 Extension Host。请只安装你信任的代码。如果交互式工具内容必须在沙箱中运行，请使用 [MCP Apps](./mcp-apps)。

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
    "description": "Adds a project board to DotCraft Desktop.",
    "entry": "./desktop/dist/index.mjs",
    "styles": ["./desktop/dist/index.css"]
  }
}
```

`version` 必须存在，并使用规范的 `MAJOR.MINOR.PATCH` 格式。使用可选的 `description` 说明 Desktop contribution 的作用；插件详情页会优先显示它，而不是父插件的描述。`entry` 必须指向 `./desktop/dist/` 下已经存在的 `.mjs` 文件。可选 `styles` 中的每一项都必须指向同一输出树下已经存在的 `.css` 文件。导入的 chunks 与 assets 也必须留在该树中。

Desktop 模块共享父插件的 identity、version、enabled state 与 interface metadata。同时包含 .NET 的 bundle 可以声明 dependencies，但它们只负责 managed generation 的顺序，不负责 Desktop activation 的顺序。Manifest 中的 `capabilities` labels 不会授予或限制 renderer access。

## 激活插件

从 `desktop/src/index.tsx` 导出具名 `activate` 函数。它接收 `DesktopPluginHost`，可以注册 runtime work，而不需要返回值：

```tsx
import type { DesktopPluginActivate } from "@dotcraft/plugin";
import "./index.css";

function Wallpaper() {
  return <div className="acme-board-wallpaper" aria-hidden="true" />;
}

function ComposerHint() {
  return <p className="acme-board-composer-hint">Review mode is active.</p>;
}

export const activate: DesktopPluginActivate = (host) => {
  host.ui.replace("app.background", Wallpaper);
  host.ui.add("composer.before", ComposerHint);
};
```

每次调用都会立即生效，并归属于该插件 revision。如果 `activate` 随后失败，Desktop 会清理已经完成的 registrations。`activate` 也可以返回 `DesktopPluginActivation` convenience object，或者同时使用返回的 contributions 与直接 kernel registrations。

## 构建并重新加载

在生成的 `desktop/` 目录安装依赖并构建：

```bash
npm install
npm run build
```

Scaffold 会先执行 TypeScript 检查，再运行 `dotcraft-plugin build`。Builder 把 `src/index.tsx`、导入的 CSS、chunks 和 assets 打包到 `dist/`，同时把 React 与共享组件连接到 Desktop runtime。

可运行的 [DotNetPluginSample](https://github.com/DotHarness/dotcraft/tree/main/sdk/dotnet/samples/DotNetPluginSample) 包含两个 .NET 与 Desktop bundle。Core 插件替换背景、包装整个应用，并拥有 renderer service、event 与自定义 surface；Consumer 插件添加 Composer 控件，并向 Core 拥有的 surface 注入 UI。

请在 DotCraft 发现插件或打包插件之前完成构建。刷新插件列表，然后启用插件。源码变更后重新构建，再刷新或重新启用插件。

完整的 surface 目录、context、组合语义、Host API 与 generation 生命周期请参阅 [Desktop Plugin API](./desktop-plugin-api)。

## 相关文档

- [Desktop Plugin API](./desktop-plugin-api)
- [插件市场](./plugin-market)
- [开发 .NET 插件](./dotnet-plugins)
- [MCP Apps](./mcp-apps)
- [插件与工具](../../features/agent-system/plugins-tools)
