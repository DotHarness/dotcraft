<div align="center">

![intro](https://github.com/DotHarness/resources/raw/master/dotcraft/intro.png)

[English](./README.md) · [官方文档](https://dotharness.github.io/dotcraft/zh/) · [快速开始](https://dotharness.github.io/dotcraft/zh/getting-started) · [下载 Release](https://github.com/DotHarness/dotcraft/releases) · [DeepWiki](https://deepwiki.com/DotHarness/dotcraft) · [License](./LICENSE)

最适合您项目的 AI Agent，所有能力尽在工作区内。

</div>

## 简介

DotCraft 是一个 .NET 10 / C# Agent Harness。它围绕真实项目目录组织 AI 工作流，让多个入口共享同一套会话核心、配置、技能、工具、任务和可观测能力。

- 项目为先：插件、技能、会话与记忆随项目走，Agent 更能理解你的项目。
- 统一会话模型：CLI、Desktop、TUI 聊天机器人等等，所有应用复用同一执行引擎。
- 可观测与治理：审批、Trace、Dashboard、Hooks 和沙箱配置让 agent 工作流更容易检查和约束。
- 扩展与集成：AppServer、SDK 与插件体系支持自定义入口和业务工作流。
- 自由换模：兼容所有 OpenAI 与 Anthropic 协议的 Provider，可直接复用 ChatGPT（Codex）订阅，并原生适配 DeepSeek V4、MiMo V2.5 等推理模型。

## 亮点功能

### Goal — 持久化的会话目标

![DotCraft Goals](https://github.com/DotHarness/resources/raw/master/dotcraft/whats-new/goal.gif)

把一项长期目标钉在某次会话上，给它设定 token 或时间预算——只要会话空闲，DotCraft 就会在后台自动继续推进。何时暂停、继续或完成，始终由你说了算。

[了解更多 →](https://dotharness.github.io/dotcraft/zh/features/agent-system/automations#goals)

### Teams — 多 Agent 的 Mission 协作板

![DotCraft Teams](https://github.com/DotHarness/resources/raw/master/dotcraft/whats-new/teams.gif)

面对一项复杂请求，DotCraft 会组建一支小队（Explorer、Builder、Reviewer、Operator），由 Team Leader 拆解任务、并行分派并汇总结果。你只交代一次需求，剩下的交给团队完成。

[了解更多 →](https://dotharness.github.io/dotcraft/zh/features/agent-system/teams)

### Dreams — 后台被动记忆整理

![Dreams review flow](https://github.com/DotHarness/resources/raw/master/dotcraft/whats-new/dreams.gif)

在你工作时，Dreams 会在后台默默梳理近期的活动，整理出值得保留的记忆草稿。你按自己的节奏审阅与批准——Agent 只会记住你真正认可的内容。

[了解更多 →](https://dotharness.github.io/dotcraft/zh/features/agent-system/memory)

### Cross Channels — 一段对话，多端共享

![DotCraft Channels configuration and conversations](https://github.com/DotHarness/resources/raw/master/dotcraft/whats-new/channels.gif)

在 Desktop 开一段对话，在 TUI 继续，再到 QQ、企业微信、飞书、Telegram、微信上接着聊——始终是同一段会话，工具审批也会以各平台原生形式呈现。

[了解更多 →](https://dotharness.github.io/dotcraft/zh/features/entry-points/channels)

### App — 用 SDK 自建 App Binding

![DotCraft App Binding](https://github.com/DotHarness/resources/raw/master/dotcraft/whats-new/app.gif)

借助 DotCraft SDK，你可以把自己的服务封装成一个 App，直接接入 Agent——把自定义的工具、数据和流程带进对话，无需任何中间层。单工具审批和完整审计链路开箱即用。

[了解更多 →](https://dotharness.github.io/dotcraft/zh/developing/integrations/app-binding)

### ChatGPT Plan — 复用已有的 ChatGPT 订阅

![复用 ChatGPT 订阅](https://github.com/DotHarness/resources/raw/master/dotcraft/whats-new/chatgpt.gif)

已经在为 ChatGPT Plus、Pro、Team、Business 或 Enterprise 付费？只需用 ChatGPT 账号登录，DotCraft 就会复用这份订阅——无需额外的 API Key，也不产生额外计费。

[了解更多 →](https://dotharness.github.io/dotcraft/zh/getting-started)

## 快速开始

![Setup](https://github.com/DotHarness/resources/raw/master/dotcraft/setup.gif)

1. 前往 [GitHub Releases](https://github.com/DotHarness/dotcraft/releases) 下载桌面应用。
2. 选择一个真实项目目录作为工作区。
3. 跟随初始化引导配置工作区和模型提供商。
4. 新建会话，发送你的第一次仓库理解请求。

完整流程见 [快速开始](https://dotharness.github.io/dotcraft/zh/getting-started)。

## 文档

| 目标 | 文档 |
|------|------|
| 第一次安装、配置和运行 | [快速开始](https://dotharness.github.io/dotcraft/zh/getting-started) |
| 使用图形化桌面客户端 | [Desktop](https://dotharness.github.io/dotcraft/zh/features/entry-points/desktop) |
| 在终端里使用完整界面 | [TUI](https://dotharness.github.io/dotcraft/zh/features/entry-points/tui) |
| 运行本地自动化任务 | [Automations 与 Hooks](https://dotharness.github.io/dotcraft/zh/features/agent-system/automations) |
| 接入外部客户端、机器人或自定义适配器 | [Channels 与 Bots](https://dotharness.github.io/dotcraft/zh/features/entry-points/channels) |
| 在服务器上一键部署 DotCraft 与渠道机器人 | [服务器部署](https://dotharness.github.io/dotcraft/zh/developing/lifecycle/server-deployment) |
| 架构、SDK 与协议 | [架构总览](https://dotharness.github.io/dotcraft/zh/developing/architecture/overview) |

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
