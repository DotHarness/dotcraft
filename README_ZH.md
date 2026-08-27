<div align="center">

[![Release](https://img.shields.io/github/v/release/DotHarness/dotcraft)](https://github.com/DotHarness/dotcraft/releases)
[![NuGet](https://img.shields.io/nuget/v/DotCraft.Harness?logo=nuget&label=NuGet)](https://www.nuget.org/profiles/DotHarness)
[![npm](https://img.shields.io/npm/v/%40dotcraft%2Fsdk?logo=npm&label=npm)](https://www.npmjs.com/org/dotcraft)
[![License](https://img.shields.io/badge/license-Apache--2.0-blue)](./LICENSE)
[![Platforms](https://img.shields.io/badge/platform-Windows%20%7C%20macOS%20%7C%20Linux-lightgrey)](https://github.com/DotHarness/dotcraft/releases)
[![Discussions](https://img.shields.io/badge/community-Discussions-brightgreen)](https://github.com/DotHarness/dotcraft/discussions)

![DotCraft — 面向真实项目的 Agent Runtime](https://github.com/DotHarness/resources/raw/master/dotcraft/intro.png)

[English](./README.md) · [官方文档](https://www.dotcraft.net/zh/) · [快速开始](https://www.dotcraft.net/zh/getting-started) · [下载 Release](https://github.com/DotHarness/dotcraft/releases) · [License](./LICENSE)

DotCraft 是一个基于 C#/.NET 构建的开源、自托管、可嵌入的**项目原生 AI Agent Runtime**。
</div>

---

## 为什么选择 DotCraft？

DotCraft 将项目转变为 **AI Agent 的可扩展运行环境**。

![同一个 Agent Runtime，三种接入方式 —— 桌面应用、AppServer + SDK、Harness 包](https://github.com/DotHarness/resources/raw/master/dotcraft/entry.png)

- **现代 Agent 能力开箱即用：** 原生的 Plan、Subagents、Agent Teams、Automations、Goals、Dreams、Dynamic Workflows 等能力，开箱即用。
- **项目走到哪，工作就跟到哪：** 会话、记忆、Agent、Skills 和 Plugins 随项目迁移，换个入口也能继续。
- **更少的 Token 消耗：** DotCraft 会最大化利用前缀缓存减少开销，Subagents 也能延续父会话已有的缓存。
- **部署和模型都由你决定：** 可在本地或自己的服务器运行，并自由选择兼容的模型服务。
- **面向 .NET 的完整 Agent Harness：** 把完整的 Agent 能力带进你正在构建的 .NET 应用，从桌面工具到服务与自动化流程。
- **轻松接入现有产品：** API、SDK、App Binding 和 Plugins，让 DotCraft 直接集成到现有应用中。

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
