<div align="center">

![intro](https://github.com/DotHarness/resources/raw/master/dotcraft/intro.png)

[English](./README.md) · [官方文档](https://www.dotcraft.net/zh/) · [快速开始](https://www.dotcraft.net/zh/getting-started) · [下载 Release](https://github.com/DotHarness/dotcraft/releases) · [DeepWiki](https://deepwiki.com/DotHarness/dotcraft) · [License](./LICENSE)

最适合您项目的 AI Agent，所有能力尽在工作区内。

</div>

## 简介

DotCraft 是一个 .NET 10 / C# Agent Harness。它围绕真实项目目录组织 AI 工作流，让多个入口共享同一套会话核心、配置、技能、工具、任务和可观测能力。

- 项目为先：插件、技能、会话与记忆随项目走，Agent 更能理解你的项目。
- 统一会话模型：CLI、Desktop、TUI 聊天机器人等等，所有应用复用同一执行引擎。
- 可观测与治理：审批、Trace、Dashboard、Hooks 和沙箱配置让 agent 工作流更容易检查和约束。
- 扩展与集成：AppServer、SDK 与插件体系支持自定义入口和业务工作流。
- 自由换模：兼容所有 OpenAI 与 Anthropic 协议的 Provider，可直接复用 ChatGPT 订阅，并原生适配 DeepSeek V4、MiMo V2.5 等推理模型。

## 亮点功能

DotCraft 的能力，按它帮你做什么来归类。

### 项目为先的工作区

*每个项目都是独立的工作区——既能跑在本地，也能跑在你自己的服务器上。*

#### 多工作区 — 每个项目都有自己的 Agent

![DotCraft 多工作区](https://github.com/DotHarness/resources/raw/master/dotcraft/whats-new/multi-workspace.gif)

多数 Agent 把同一套配置摊到所有项目上；DotCraft 反过来：每个项目都有自己的 Agent——自己的会话、记忆、技能、插件和设置——因而真正懂你手上的项目。

[了解更多 →](https://www.dotcraft.net/zh/features/project-first)

#### 远程服务器 — 在 Desktop 里驱动服务器上的 Agent

![DotCraft 远程服务器](https://github.com/DotHarness/resources/raw/master/dotcraft/whats-new/servers.gif)

把 DotCraft 跑在你自己的服务器上，随时从 Desktop 连接进行远程操控。

[了解更多 →](https://www.dotcraft.net/zh/features/self-hosted/server-deployment)

### 持续工作的 Agent

*交给它一个目标或一项任务，DotCraft 会持续推进。*

#### Teams — 多 Agent 的 Mission 协作板

![DotCraft Teams](https://github.com/DotHarness/resources/raw/master/dotcraft/whats-new/teams.gif)

面对一项复杂请求，DotCraft 会组建一支小队拆解任务、并行分派并汇总结果。你只交代一次需求，剩下的交给团队完成。

[了解更多 →](https://www.dotcraft.net/zh/features/agent-system/teams)

#### Goal — 持久化的会话目标

![DotCraft Goals](https://github.com/DotHarness/resources/raw/master/dotcraft/whats-new/goal.gif)

把一项长期目标钉在某次会话上，给它设个时间或用量预算——只要会话闲下来，DotCraft 就会在后台默默继续推进。何时暂停、继续或收尾，始终由你说了算。

[了解更多 →](https://www.dotcraft.net/zh/features/agent-system/automations#goals)

#### 会话工具 — Agent 自己打理会话

![DotCraft 会话工具](https://github.com/DotHarness/resources/raw/master/dotcraft/whats-new/thread-tools.gif)

在一段对话里，DotCraft 就能照看其余的会话——在后台新开一个、翻找查看以前的、把某个任务转交出去，或是重命名、置顶、归档收拾。这些杂活它自己就办了，也绝不会把你从当前对话里拽走。

[了解更多 →](https://www.dotcraft.net/zh/features/entry-points/desktop)

#### Dreams — 后台被动记忆整理

![Dreams review flow](https://github.com/DotHarness/resources/raw/master/dotcraft/whats-new/dreams.gif)

在你工作时，Dreams 会在后台默默梳理近期的活动，整理出值得保留的记忆草稿。你按自己的节奏审阅与批准——Agent 只会记住你真正认可的内容。

[了解更多 →](https://www.dotcraft.net/zh/features/agent-system/memory)

### 扩展与集成

*把你自己的工具、界面和服务带进对话。*

#### Desktop 扩展 — 插件在 Desktop 内提供完整界面

![DotCraft Desktop 扩展](https://github.com/DotHarness/resources/raw/master/dotcraft/whats-new/desktop-extensions.gif)

插件不止于添加工具——它还能把自己完整的界面直接搬进 Desktop。

[了解更多 →](https://www.dotcraft.net/zh/developing/integrations/desktop-extensions)

#### 交互式工具卡片 — 工具结果可直接动手

![DotCraft 交互式工具卡片](https://github.com/DotHarness/resources/raw/master/dotcraft/whats-new/dynamic-tools-card.gif)

有些工具结果是用来动手的，而不只是看——一块看板、一个可操作的条目、一项可刷新的状态。它们会在对话里直接渲染成可交互的实时卡片，你的点击和输入会直接驱动 Agent。

[了解更多 →](https://www.dotcraft.net/zh/developing/integrations/interactive-tool-ui)

#### App — 用 SDK 自建 App Binding

![DotCraft App Binding](https://github.com/DotHarness/resources/raw/master/dotcraft/whats-new/app.gif)

有自己的服务？借助 DotCraft SDK，你可以把它做成一个 App，直接接入 Agent——它的工具、数据和流程就成了对话的一部分，中间不用再搭别的东西。

[了解更多 →](https://www.dotcraft.net/zh/developing/integrations/app-binding)

### 随你接入

*把 Agent 带进你常用的聊天软件，并复用你已有的模型订阅。*

#### Cross Channels — 同一个 Agent，进驻你的各个聊天软件

![DotCraft Channels configuration and conversations](https://github.com/DotHarness/resources/raw/master/dotcraft/whats-new/channels.gif)

让同一个 Agent 作为机器人进驻 QQ、企业微信、飞书、Telegram、微信——共用同一套记忆、技能与审批策略。

[了解更多 →](https://www.dotcraft.net/zh/features/entry-points/channels)

#### ChatGPT Plan — 复用已有的 ChatGPT 订阅

![复用 ChatGPT 订阅](https://github.com/DotHarness/resources/raw/master/dotcraft/whats-new/chatgpt.gif)

已经在为 ChatGPT Plus、Pro、Team、Business 或 Enterprise 付费？只需用 ChatGPT 账号登录，DotCraft 就会复用这份订阅——无需额外的 API Key，也不产生额外计费。

[了解更多 →](https://www.dotcraft.net/zh/getting-started)

## 快速开始

![Setup](https://github.com/DotHarness/resources/raw/master/dotcraft/setup.gif)

1. 前往 [GitHub Releases](https://github.com/DotHarness/dotcraft/releases) 下载桌面应用。
2. 选择一个真实项目目录作为工作区。
3. 跟随初始化引导配置工作区和模型提供商。
4. 新建会话，发送你的第一次仓库理解请求。

完整流程见 [快速开始](https://www.dotcraft.net/zh/getting-started)。

## 文档

| 目标 | 文档 |
|------|------|
| 第一次安装、配置和运行 | [快速开始](https://www.dotcraft.net/zh/getting-started) |
| 使用图形化桌面客户端 | [Desktop](https://www.dotcraft.net/zh/features/entry-points/desktop) |
| 在终端里使用完整界面 | [TUI](https://www.dotcraft.net/zh/features/entry-points/tui) |
| 运行本地自动化任务 | [Automations 与 Hooks](https://www.dotcraft.net/zh/features/agent-system/automations) |
| 接入外部客户端、机器人或自定义适配器 | [Channels 与 Bots](https://www.dotcraft.net/zh/features/entry-points/channels) |
| 在服务器上一键部署 DotCraft 与渠道机器人 | [服务器部署](https://www.dotcraft.net/zh/developing/lifecycle/server-deployment) |
| 架构、SDK 与协议 | [架构总览](https://www.dotcraft.net/zh/developing/architecture/overview) |

## 贡献代码

欢迎提交代码、文档与集成相关贡献。开始前请阅读 [CONTRIBUTING.md](./CONTRIBUTING.md)。

## 致谢

本项目受 [nanobot](https://github.com/HKUDS/nanobot) 与 [codex](https://github.com/openai/codex) 启发，并构建在 [agent-framework](https://github.com/microsoft/agent-framework) 之上。

特别感谢：

- [HKUDS/nanobot](https://github.com/HKUDS/nanobot)
- [openai/codex](https://github.com/openai/codex)
- [microsoft/agent-framework](https://github.com/microsoft/agent-framework)
- [alibaba/OpenSandbox](https://github.com/alibaba/OpenSandbox)
- [modelcontextprotocol/csharp-sdk](https://github.com/modelcontextprotocol/csharp-sdk)
- [openai/symphony](https://github.com/openai/symphony)

# 文章

dotcraft 相关技术博客.

[为什么你的 Agent 这么贵：Prompt Cache 命中率为 0 的排查记录](https://zhuanlan.zhihu.com/p/2044201072466588522)

## License

Apache License 2.0
