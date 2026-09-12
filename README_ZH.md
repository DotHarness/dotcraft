<div align="center">

[![Release](https://img.shields.io/github/v/release/DotHarness/dotcraft)](https://github.com/DotHarness/dotcraft/releases)
[![NuGet](https://img.shields.io/nuget/v/DotCraft.Harness?logo=nuget&label=NuGet)](https://www.nuget.org/profiles/DotHarness)
[![npm](https://img.shields.io/npm/v/%40dotcraft%2Fsdk?logo=npm&label=npm)](https://www.npmjs.com/org/dotcraft)
[![License](https://img.shields.io/badge/license-Apache--2.0-blue)](./LICENSE)
[![Platforms](https://img.shields.io/badge/platform-Windows%20%7C%20macOS%20%7C%20Linux-lightgrey)](https://github.com/DotHarness/dotcraft/releases)
[![Discussions](https://img.shields.io/badge/community-Discussions-brightgreen)](https://github.com/DotHarness/dotcraft/discussions)

![DotCraft —— 可嵌入、可扩展的 Agent Runtime](https://github.com/DotHarness/resources/raw/master/dotcraft/intro.png)

[English](./README.md) · [官方文档](https://www.dotcraft.net/zh/) · [快速开始](https://www.dotcraft.net/zh/getting-started) · [下载 Release](https://github.com/DotHarness/dotcraft/releases) · [License](./LICENSE)

DotCraft 是一个基于 C#/.NET 构建的开源、自托管 **AI Agent Runtime**。它可以作为桌面应用直接运行，也可以引入你自己的 .NET 应用，两者都能用插件扩展。

</div>


## 为什么选择 DotCraft？

DotCraft 将你的项目转变为 **AI Agent 的可扩展运行环境**。

![同一个 Agent Runtime，三种接入方式 —— 桌面应用、AppServer + SDK、Harness 包](https://github.com/DotHarness/resources/raw/master/dotcraft/entry.png)

- **完整的 Agent 能力，开箱即用：** Plan、subagents、Automations、Goals、Dreams 和 Dynamic Workflows 都已内置，不必从零搭建 Agent 基础设施。
- **Agent 定制与远程协作：** Agent Builder 和 Agent Profiles 支持创建、复用专用 Agent，远程工具调用让 Agent 跨机器工作。
- **项目走到哪，工作就跟到哪：** 会话、记忆、Agent、Skills 和 Plugins 随工作区保存。切换到 Desktop、CLI、编辑器或聊天机器人，依然能接着做。
- **把完整 Agent Runtime 带进你的产品：** 将 Desktop 背后的同一套运行时嵌入 .NET 工具、服务和自动化流程，或通过 AppServer、SDK 和 App Binding 接入现有产品。
- **让 Agent 扩展自己的运行时：** 直接让 Agent 在工作区里创建并构建 .NET 插件。插件使用与内置功能相同的扩展机制，能够添加工具、提示词、命令和生命周期逻辑。更新插件时，宿主无需停止运行。
- **让 Desktop 适应你的工作方式：** TypeScript 和 React 插件使用原生 UI 组件扩展 Desktop 的界面与交互。
- **部署、模型和成本都由你掌控：** 在本地或自己的服务器上运行，并选择兼容的模型服务或使用 ChatGPT 订阅。DotCraft 让可复用的提示词前缀保持逐字节稳定，提高模型服务的缓存复用率，降低重复输入成本。

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
