# 入口总览

DotCraft 提供多种"打开同一个工作区"的方式。所有入口都连同一个 [统一会话核心](../../developing/architecture/session-core)、读同一份 `.craft/`、共享同一份长期记忆——区别只是**在哪种界面里和 Agent 说话**。

## 入口形态

![DotCraft entry points topology](/entry-points-topology.svg)

| 入口 | 界面形态 | 适合 |
|---|---|---|
| [Desktop](./desktop) | 图形化桌面应用 | 第一次使用 / 长期协作 / 复杂 diff & approval |
| [TUI](./tui) | Rust 终端富界面 | 终端原生、SSH 远程、轻量化 |
| [IDE / Editor (ACP)](./editors) | IDE 内嵌（JetBrains / Obsidian / Unity 等） | 在编辑器里直接读写未保存文件、用编辑器原生终端 |
| [Channels / Bots](./channels) | QQ / 企业微信 / 飞书 / Telegram / 微信 | 团队群聊、知识库 bot、客服 bot |

## 决策表

| 我想… | 推荐入口 |
|---|---|
| 第一次使用 DotCraft | [Desktop](./desktop) |
| 在远程服务器上用 SSH 工作 | [TUI](./tui) 或 [AppServer + 远程 TUI/Desktop](../../developing/lifecycle/appserver) |
| 在 IDE 里让 Agent 直接读未保存文件 | [ACP](./editors) |
| 让一个 Discord/QQ 群获得 Agent 助手 | [Channels](./channels) |
| 多个客户端共享同一个工作区 | [AppServer 模式](../../developing/lifecycle/appserver) + 任意上面入口 |
| 写一个机器人或自定义客户端 | [SDK 总览](../../developing/sdks/python) + [AppServer 协议](../../developing/protocols/appserver-protocol) |
| 做定时任务或 CI 自动化 | [Automations](../agent-system/automations) + 任意入口接收审批 |

## 跨入口共享原则

- **同一工作区只跑一个 AppServer**：本机由 [Hub](../../developing/lifecycle/hub) 协调，不需要手动管理。
- **会话可跨入口接管**：在 Desktop 开的 Thread，可以在 TUI / ACP 继续；审批 UI 用每个平台原生的方式呈现。
- **配置单源真相**：模型、安全、自动化都在 `.craft/config.json` 与 `~/.craft/config.json`，所有入口读同一份。
- **入口开关在工作区配置**：ACP、Dashboard、Automations 与外部渠道按需启用。

## 第一次使用怎么挑

如果你看到这里还没开始用：

1. 先按 [快速开始](../../getting-started) 装 Desktop。
2. 在 Desktop 里跑通"选工作区 + 配模型 + 第一次对话"。
3. 再按真实需求决定要不要打开第二种入口（TUI / ACP / 频道）。

## 相关入口

- [统一会话核心](../../developing/architecture/session-core) — 跨入口共享背后的 Thread / Turn / Item 模型
- [AppServer 模式](../../developing/lifecycle/appserver) — 远程、多客户端、自定义集成
- [Hub 本地协调](../../developing/lifecycle/hub) — 本机为何能"打开就连"
