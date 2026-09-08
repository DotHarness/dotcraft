<div align="center">

[![Release](https://img.shields.io/github/v/release/DotHarness/dotcraft)](https://github.com/DotHarness/dotcraft/releases)
[![NuGet](https://img.shields.io/nuget/v/DotCraft.Harness?logo=nuget&label=NuGet)](https://www.nuget.org/profiles/DotHarness)
[![npm](https://img.shields.io/npm/v/%40dotcraft%2Fsdk?logo=npm&label=npm)](https://www.npmjs.com/org/dotcraft)
[![License](https://img.shields.io/badge/license-Apache--2.0-blue)](./LICENSE)
[![Platforms](https://img.shields.io/badge/platform-Windows%20%7C%20macOS%20%7C%20Linux-lightgrey)](https://github.com/DotHarness/dotcraft/releases)
[![Discussions](https://img.shields.io/badge/community-Discussions-brightgreen)](https://github.com/DotHarness/dotcraft/discussions)

![DotCraft — 面向真实项目的 Agent Runtime](https://github.com/DotHarness/resources/raw/master/dotcraft/intro.png)

[English](./README.md) · [官方文档](https://www.dotcraft.net/zh/) · [快速开始](https://www.dotcraft.net/zh/getting-started) · [下载 Release](https://github.com/DotHarness/dotcraft/releases) · [License](./LICENSE)

DotCraft 是一个基于 C#/.NET 构建的开源、自托管、可嵌入的**AI Agent Runtime**。

</div>


## 为什么选择 DotCraft？

DotCraft 将你的项目转变为 **AI Agent 的可扩展运行环境**。

![同一个 Agent Runtime，三种接入方式 —— 桌面应用、AppServer + SDK、Harness 包](https://github.com/DotHarness/resources/raw/master/dotcraft/entry.png)

- **现代 Agent 能力开箱即用：** 原生的 Plan、Agent Builder、Agent Profiles、Subagents、Automations、Goals、Dreams、Dynamic Workflows 等能力，开箱即用。
- **项目走到哪，工作就跟到哪：** 会话、记忆、Agent、Skills 和 Plugins 随项目迁移，换个入口也能继续。
- **更少的 Token 消耗：** DotCraft 内置轨迹跟踪，跨会话最大化利用前缀缓存减少 Token 开销。
- **部署和模型都由你决定：** 可在本地或自己的服务器运行，并自由选择兼容的模型服务以及你的 ChatGPT 订阅。
- **面向 .NET 的完整 Agent Harness：** 把完整的 Agent 能力带进你正在构建的 .NET 应用，从桌面工具到服务与自动化流程。
- **轻松接入现有产品：** API、SDK、App Binding 和 Plugins，让 DotCraft 直接集成到你的应用中。

## 探索 DotCraft

### [Desktop](https://www.dotcraft.net/zh/features/entry-points/desktop)

[![你的项目，你的 Agent，同一个桌面。](https://github.com/DotHarness/resources/raw/master/dotcraft/product-desktop.png)](https://www.dotcraft.net/zh/features/entry-points/desktop)

在一个桌面应用中与 Agent 一起处理项目。

### [DotCraft.Harness](https://www.dotcraft.net/zh/developing/harness/)

[![把 Agent 能力嵌入你的 .NET 应用。](https://github.com/DotHarness/resources/raw/master/dotcraft/product-harness.png)](https://www.dotcraft.net/zh/developing/harness/)

将完整的 Agent 运行时嵌入你的 .NET 应用。

### [Oratorio](https://www.dotcraft.net/zh/features/oratorio)

[![从任务到审阅与交付。](https://github.com/DotHarness/resources/raw/master/dotcraft/product-oratorio.png)](https://www.dotcraft.net/zh/features/oratorio)

在同一看板上管理 Agent 任务，从分配到审阅。

### [卫星](https://www.dotcraft.net/zh/features/agent-system/satellite)

[![让你的 Agent 到另一台电脑上工作。](https://github.com/DotHarness/resources/raw/master/dotcraft/product-satellite.png)](https://www.dotcraft.net/zh/features/agent-system/satellite)

让你的 Agent 在另一台电脑获准共享的文件夹中工作。

### [Avatar](https://www.dotcraft.net/zh/developing/sdks/typescript#avatar-包)

[![让你的 Agent 拥有鲜明个性。](https://github.com/DotHarness/resources/raw/master/dotcraft/product-avatar.png)](./sdk/typescript/packages/avatar/README.md)

用表情丰富、可自由搭配的头像，为你的 Agent 赋予鲜明个性。

## 快速开始

DotCraft 支持 OpenAI、Anthropic 模型提供商，或使用你的 ChatGPT 订阅登录。

![每个模型都有一条接入之路 —— OpenAI 协议、Anthropic 协议、ChatGPT 订阅](https://github.com/DotHarness/resources/raw/master/dotcraft/providers.png)

### Desktop

1. 从 [GitHub Releases](https://github.com/DotHarness/dotcraft/releases) 下载最新版本。
2. 选择一个真实项目目录作为工作区。
3. 配置模型提供商和偏好。

### CLI

macOS / Linux：

```bash
curl -fsSL https://www.dotcraft.net/install.sh | bash
```

Windows PowerShell：

```powershell
irm https://www.dotcraft.net/install.ps1 | iex
```

完整说明请查看[快速开始](https://www.dotcraft.net/zh/getting-started)。

## 贡献代码

开始前请阅读 [CONTRIBUTING.md](./CONTRIBUTING.md)。

## 致谢

本项目受 [nanobot](https://github.com/HKUDS/nanobot) 、 [codex](https://github.com/openai/codex) 与 [agent-framework](https://github.com/microsoft/agent-framework) 启发。

特别感谢：

- [HKUDS/nanobot](https://github.com/HKUDS/nanobot)
- [openai/codex](https://github.com/openai/codex)
- [microsoft/agent-framework](https://github.com/microsoft/agent-framework)
- [alibaba/OpenSandbox](https://github.com/alibaba/OpenSandbox)
- [modelcontextprotocol/csharp-sdk](https://github.com/modelcontextprotocol/csharp-sdk)
- [openai/symphony](https://github.com/openai/symphony)

## License

Apache License 2.0
