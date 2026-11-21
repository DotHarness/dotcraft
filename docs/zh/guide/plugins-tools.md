# 插件与工具

通过插件和 MCP server 给 Agent 添加工具、连接账号和共享工作流。

![内置工具、插件和 MCP server 汇聚成 Agent 的一份能力清单，插件和 MCP server 需要先检查并信任](/capability-sources-overview.svg)

## 能力来自哪里

| 来源 | 提供什么 |
|---|---|
| **内置工具** | 文件编辑、Shell、Web、搜索、规划这些核心操作 |
| **插件** | 打包分发的技能、工具、工作流、App、面板和[生命周期 Hooks](./hooks) |
| **MCP server** | 由本地进程或远程服务提供的工具 |

## 安装插件

![浏览插件、查看详情并在 DotCraft 对话中试用](https://github.com/DotHarness/resources/raw/master/dotcraft/docs/plugin-registry.webp)

1. 在 DotCraft Desktop 中打开**插件**页面。
2. 搜索或浏览插件目录。
3. 打开一个插件，看清楚发布方、它提供的能力和相关链接。
4. 点击**安装**。
5. 确认信息后，点击**添加到 DotCraft**。
6. 按安装对话框的提示完成需要的 App 配置。
7. 点击**在对话中试用**，或者新建一段对话直接说明你要做的事。

想从其他目录安装插件，见[插件市场](./plugin-marketplaces)。要接入 Telegram、微信等平台，请打开[渠道页签](./channels/#接入渠道)。

## 管理已安装的插件

安装的插件可在该宿主上的各个工作区使用。在**管理**里关闭插件只影响当前工作区。点击**卸载**会移除共享安装。

项目 `.craft/plugins` 文件夹里的插件随该项目提供，显示在“来自”加项目名的分组里。

在[应用设置](./connected-apps)管理插件的应用账号，在对话的应用选择器里选择可用的应用。

## 远端插件界面

插件安装在远端宿主。要在本机运行插件界面，请授权该工作区使用桌面界面。
授权覆盖该工作区后续安装和更新的插件。在插件页可停止界面或重新加载。

## 从磁盘安装

1. 在本地工作区打开**插件**页面。
2. 打开**创建**旁边的菜单，选择**从磁盘安装**。
3. 选择插件文件夹。
4. 检查插件，再用**在对话中试用**验证一遍。

## 创建插件

从内置的 `$plugin-creator` 技能开始：

```text
$plugin-creator 创建一个插件，用来打包我的项目审查工作流。
```

这个技能会创建插件，并引导你完成本地测试。

开发可执行模块请看[开发桌面扩展](../extend/desktop-extensions)或[开发 .NET 扩展](../extend/dotnet-extensions)，打包和分发方式见[插件市场打包指南](../extend/plugin-market)。

## 连接 MCP server

打开 **设置 → MCP Servers**，添加一种连接：

- **STDIO**：由本地命令启动 server。
- **Streamable HTTP**：连接远程 MCP endpoint。

Token 和其他密钥用环境变量传入。正式在对话里用之前，先点**测试连接**确认能连上。完整字段见[MCP 服务配置](../configuration/plugins#mcp-服务)。

## 安装前先看清信任边界

安装插件或连接 server 前，请核对发布方、来源和账号权限。

桌面扩展在本机运行。.NET 扩展需要授权，并以 DotCraft 宿主的权限运行。运行插件提供的 Hooks 前，请在**设置 → Hooks** 检查并授权。

## 相关文档

- [插件市场](./plugin-marketplaces) — 添加你信任的插件目录，从中安装插件
- [应用连接](./connected-apps) — 把插件自带的 App 接到你的账号上
- [安全](../self-hosting/security) — 用工作区边界、审批和命令规则约束工具调用
