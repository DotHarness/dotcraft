<div align="center">

[![Release](https://img.shields.io/github/v/release/DotHarness/dotcraft)](https://github.com/DotHarness/dotcraft/releases)
[![License](https://img.shields.io/badge/license-Apache--2.0-blue)](./LICENSE)
[![Platforms](https://img.shields.io/badge/platform-Windows%20%7C%20macOS%20%7C%20Linux-lightgrey)](https://github.com/DotHarness/dotcraft/releases)
[![Discussions](https://img.shields.io/badge/community-Discussions-brightgreen)](https://github.com/DotHarness/dotcraft/discussions)

![DotCraft — 面向真实项目的 Agent Runtime](https://github.com/DotHarness/resources/raw/master/dotcraft/intro.png)

[English](./README.md) · [官方文档](https://www.dotcraft.net/zh/) · [快速开始](https://www.dotcraft.net/zh/getting-started) · [下载 Release](https://github.com/DotHarness/dotcraft/releases) · [开发者文档](https://www.dotcraft.net/zh/developing/architecture/overview) · [License](./LICENSE)

### 让 Agent 属于项目，而不是属于某个应用。

DotCraft 是一个开源、自托管、**面向项目的 Agent Runtime**，为真实项目提供持久会话、记忆、后台工作、自动化与应用连接能力。

</div>

---

## 让一个真实项目开始工作

先在 Desktop 或 CLI 中打开一个项目。需要时，再为它加入专用 Agent、后台工作、社交渠道或应用集成。

### 一个项目，一套 Runtime

会话、记忆、Agent、技能、工具、Plugin、Automation 与策略都留在项目里。无论从 Desktop、CLI、社交渠道还是业务应用进入，都可以从同一份项目上下文继续。

![在 DotCraft Desktop 中切换不同项目工作区](https://github.com/DotHarness/resources/raw/master/dotcraft/whats-new/multi-workspace.gif)

[了解项目优先的工作区 →](https://www.dotcraft.net/zh/features/project-first)

### 使用一个 Agent，也可以组成团队

- **创建可复用的 Agent。** Agent Profile 保存它的指令、模型、工具、技能和权限；Agent Builder 通过对话帮助你完成配置。

- **让多个 Agent 协作。** Agent Teams 把复杂工作交给多个专用 Agent 分工处理。

- **让工作持续运行。** Goal 与 Automation 承接长期、定时或需要反复执行的工作。

![通过对话定制一个专用 Agent](https://github.com/DotHarness/resources/raw/master/dotcraft/whats-new/agent-builder.gif)

[为一项工作创建 Agent →](https://www.dotcraft.net/zh/features/agent-system/agent-profiles) · [让多个 Agent 协作 →](https://www.dotcraft.net/zh/features/agent-system/teams) · [设置 Goal 或 Automation →](https://www.dotcraft.net/zh/features/agent-system/automations)

### 把 DotCraft 连接到一个应用

通过 API、App Binding，或 TypeScript、.NET 和 Python SDK，可以把项目 Runtime 接入其他产品。

应用保留自己的数据与流程。DotCraft 负责会话、工具、审批、模型、记忆与 Trace。

![Oratorio 项目工作板作为完整视图嵌入 DotCraft Desktop](https://github.com/DotHarness/resources/raw/master/dotcraft/whats-new/desktop-extensions.gif)

[连接一个应用 →](https://www.dotcraft.net/zh/developing/integrations/app-binding) · [使用 SDK →](https://www.dotcraft.net/zh/developing/sdks/) · [看看 DotCraft 能驱动什么 →](#基于-dotcraft-构建)

### 离开 Desktop，工作也能继续

- **Automation** 负责反复执行的项目工作。

- **后台 Channels** 在 Desktop 关闭后仍然保持连接。

- **Channel Handoff** 把正在进行的会话接续到社交渠道，无需重新开始。

| 后台保持连接 | 随时接续同一个会话 |
|---|---|
| ![DotCraft 在后台保持社交渠道连接](https://github.com/DotHarness/resources/raw/master/dotcraft/whats-new/channels.gif) | ![把正在进行的 DotCraft 会话接续到社交渠道](https://github.com/DotHarness/resources/raw/master/dotcraft/whats-new/channel-handoff.gif) |

[在社交渠道中继续工作 →](https://www.dotcraft.net/zh/features/entry-points/channels) · [设置重复执行的工作 →](https://www.dotcraft.net/zh/features/agent-system/automations)

---

## 为什么选择 DotCraft？

多数 AI Coding 工具主要优化一个开发者与一个客户端之间的交互。

DotCraft 关注的是另一个层次：

> **项目，而不是客户端，才是 Agent 状态和执行的归属单元。**

这让 Agent 可以成为不同形态的长期能力。

| 设计目标 | 在 DotCraft 中意味着什么 |
|---|---|
| **状态随项目持久化** | 会话、记忆、配置、技能、插件与 Runtime 状态都属于工作区。 |
| **多个客户端共享** | Desktop、CLI、编辑器、聊天渠道和连接的应用可以使用同一个项目 Runtime。 |
| **工作由 Runtime 管理** | 运行中的工作、Goal、Automation、Agent Teams 和后台记忆不依赖某个 UI 窗口。 |
| **执行受到治理** | 审批、Hook、Worktree 边界、Sandbox 和 Trace 让 Agent 的工作保持可见、可控。 |
| **可以服务业务应用** | API、SDK、App Binding、Plugin 与 Desktop Extension 让其他产品复用 Runtime。 |
| **自托管且自由选择模型** | 可运行在本地或自己的服务器，并连接 OpenAI 或 Anthropic 兼容 Provider。 |

### 它处于什么位置？

| 你的主要需求 | 常见选择 |
|---|---|
| 在一个 IDE 或终端里获得最快的交互式 Coding 体验 | IDE / Terminal Coding Agent |
| 在应用代码中直接嵌入 Agent 原语 | Agent Framework / SDK |
| 构建预定义的可视化业务流程 | Workflow Builder |
| 让一个项目拥有持久 Runtime，并被多个客户端或业务应用共同复用 | **DotCraft** |

DotCraft 不试图替代所有 Coding 界面。它提供项目 Agent、自动化和应用背后的持久运行层。

---

## 快速开始

### Desktop

1. 从 [GitHub Releases](https://github.com/DotHarness/dotcraft/releases) 下载最新版本。
2. 选择一个真实项目目录作为工作区。
3. 配置支持的 Provider，或使用受支持的 ChatGPT 计划登录。
4. 从一个具体问题开始：

```text
梳理这个仓库的架构，解释主要执行路径，并告诉新贡献者应该从哪里开始。
```

### CLI

macOS / Linux：

```bash
curl -fsSL https://www.dotcraft.net/install.sh | bash
```

Windows PowerShell：

```powershell
irm https://www.dotcraft.net/install.ps1 | iex
```

运行一次性项目任务：

```bash
dotcraft exec "检查这个仓库，并指出风险最高的三个改动。"
```

完整的 Provider、Desktop、CLI 和服务器部署说明请查看[快速开始](https://www.dotcraft.net/zh/getting-started)。

---

## 一个 Runtime，三个 Loop

一个 DotCraft 工作区把三种连续性放在一起：同一个 Runtime 既能与你持续对话，也能推进工作，还能在不同会话之间记住真正重要的内容。

| Loop | 它为项目带来什么 |
|---|---|
| **Conversation** | 持久会话、审批、排队输入，以及从另一个客户端继续工作的能力。 |
| **Work** | Goal、Automation、Agent Teams 与隔离 Worktree，让较长任务在人类控制下持续推进。 |
| **Memory** | 可审阅的项目记忆与历史，把有用的上下文带入未来会话。 |

最终，项目 Agent 的对话、持续工作和记忆共享同一个边界，而不是散落在彼此割裂的工具里。

---

## 基于 DotCraft 构建

### Oratorio

![Oratorio — 基于 DotCraft 构建的项目工作板](https://github.com/DotHarness/resources/raw/master/oratorio/banner-1280x640.png)

Oratorio 是一个面向本地任务、GitHub 和 GitLab Issue、Review 与实现工作的项目板。DotCraft 为看板中的 Agent 提供运行能力。

[了解 Oratorio →](https://github.com/DotHarness/oratorio)

---

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
