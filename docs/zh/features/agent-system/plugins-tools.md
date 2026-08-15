# 插件与工具

插件和工具让 DotCraft 可以编辑文件、运行命令、连接外部服务，并执行可复用的工作流。

## 能力来自哪里

| 来源 | 提供的能力 |
|---|---|
| **内置工具** | 文件编辑、Shell、Web、搜索、规划等核心操作 |
| **插件** | 打包分发的 skills、tools、workflows、apps、面板和生命周期 hooks |
| **MCP servers** | 由本地进程或远程服务提供的 tools |

Agent 使用这些能力时，DotCraft 仍会执行工作区边界、审批和安全设置。

## 安装插件

1. 在 DotCraft Desktop 中打开 **Plugins / 插件**。
2. 搜索或浏览插件目录。
3. 打开插件，检查发布者、能力和相关链接。
4. 点击 **Install / 安装**。
5. 检查确认信息，然后点击 **Add to DotCraft / 添加到 DotCraft**。
6. 按安装对话框提示完成所需的 App 配置。
7. 点击 **Try in chat / 在对话中试用**，或新建对话并描述你的任务。

如需从其他目录安装插件，请阅读[插件市场](./plugin-marketplaces)。

## 管理已安装插件

打开 **Plugins / 插件**，然后点击 **Manage / 管理**。

- 禁用插件会保留安装文件，但 Agent 不再使用其中的能力。
- 需要再次使用时，重新启用即可。
- 打开插件并点击 **Uninstall / 卸载**，可以从当前工作区移除插件。

如果插件带有 App，**App Settings / 应用设置** 管理账号连接，会话中的 App 选择器决定当前会话能否使用它。详见 [Connected Apps](./connected-apps)。

## 从本地安装

开发插件或收到插件文件夹时，可以直接从磁盘安装：

此选项仅适用于本地工作区。

1. 打开 **Plugins / 插件**。
2. 打开 **Create / 创建** 旁的菜单，然后选择 **Install from disk / 从磁盘安装**。
3. 选择插件文件夹。
4. 检查插件，然后通过 **Try in chat / 在对话中试用** 完成验证。

DotCraft 会把插件复制到当前工作区。卸载时会删除这份已安装副本。

## 创建插件

从内置的 `$plugin-creator` skill 开始：

```text
$plugin-creator 创建一个插件，用来打包我的项目审查工作流。
```

这个 skill 会创建插件结构，并引导你完成本地测试。需要分发可复用能力时使用插件。只服务于一个项目的工作流，优先使用普通 skill。

市场打包和分发方式见[插件市场](../../developing/integrations/plugin-market)。

## 连接 MCP server

打开 **Settings → MCP Servers**，然后添加一种连接：

- **STDIO**：通过本地命令启动 server。
- **Streamable HTTP**：连接远程 MCP endpoint。

Token 和其他 secret 应通过环境变量提供。正式使用前，先点击 **Test connection** 检查连接。

完整 MCP 字段见[配置](../../developing/configuration#plugins-mcp-与-lsp)。

## 安装前检查信任边界

只安装你信任的插件，只连接你信任的 server。安装前检查发布者、所需能力、来源链接和账号权限。

插件可能启动本地进程、连接远程服务、添加 hooks，或加载 Desktop 面板。相关调用仍会经过 DotCraft 的审批与工作区安全设置。插件 hooks 在你通过 **Settings → Hooks** 检查并信任前不会运行。

## 相关文档

- [插件市场](./plugin-marketplaces)
- [Dynamic Workflows](./dynamic-workflows)
- [Connected Apps](./connected-apps)
- [生命周期 Hooks](./hooks)
- [安全与沙箱](../self-hosted/security)
- [DotCraft App](../../developing/integrations/app-binding)
