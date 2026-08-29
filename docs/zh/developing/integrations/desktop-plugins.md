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

`version` 必须存在，并使用规范的 `MAJOR.MINOR.PATCH` 格式。可选的 `description` 说明这个 Desktop 模块做什么，插件详情页会优先显示它，父插件的描述只作为兜底。`entry` 必须指向 `./desktop/dist/` 下已存在的 `.mjs` 文件，可选 `styles` 的每一项都必须指向同一输出目录下已存在的 `.css` 文件。导入的 chunk 与 asset 也必须留在这个目录里。

Desktop 模块共享父插件的 id、版本、启用状态与 `interface` 元数据。同时包含 .NET 的 bundle 可以声明依赖，但依赖只决定 managed generation 的顺序，不决定 Desktop 的激活顺序。Manifest 里的 `capabilities` 标签既不授予也不限制 renderer 访问权限。

## 激活插件

从 `desktop/src/index.tsx` 导出具名 `activate` 函数。它接收 `DesktopPluginHost`，可以直接注册运行时行为，不需要返回值：

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

每次调用都会立即生效，并归属于当前插件 revision。如果 `activate` 随后失败，Desktop 会撤销此前已经生效的注册。`activate` 也可以返回 `DesktopPluginActivation` 便捷对象，或者把返回的 contribution 与直接的 kernel 注册混用。

## 构建并重新加载

在生成的 `desktop/` 目录安装依赖并构建：

```bash
npm install
npm run build
```

构建脚本先做 TypeScript 类型检查，再运行 `dotcraft-plugin build`，把 `src/index.tsx`、导入的 CSS、chunk 与 asset 打包进 `dist/`，并把 React 与共享组件接到 Desktop 自己的 React runtime 上。

请在 DotCraft 发现插件或打包插件之前完成构建。刷新插件列表，然后启用插件。源码变更后重新构建，再刷新或重新启用插件。

可运行的 [DotNetPluginSample](https://github.com/DotHarness/dotcraft/tree/main/sdk/dotnet/samples/DotNetPluginSample) 包含两个同时带 .NET 与 Desktop 的 bundle。Core 插件替换背景、包装整个应用，并拥有一个 renderer service、一个 event 与一个自定义 surface。Consumer 插件添加 Composer 控件，并向 Core 拥有的 surface 注入 UI。

完整的 surface 目录、context、组合语义、Host API 与 generation 生命周期请参阅 [Desktop Plugin API](./desktop-plugin-api)。

## 相关文档

- [开发 .NET 插件](./dotnet-plugins)——功能需要后端执行或 Agent 工具时，为同一个 bundle 补上 .NET 模块。
- [插件市场](./plugin-market)——把构建好的插件发布出去，供其他人安装。
