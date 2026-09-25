<div align="center">

[![Release](https://img.shields.io/github/v/release/DotHarness/dotcraft)](https://github.com/DotHarness/dotcraft/releases)
[![NuGet](https://img.shields.io/nuget/v/DotCraft.Harness?logo=nuget&label=NuGet)](https://www.nuget.org/profiles/DotHarness)
[![npm](https://img.shields.io/npm/v/%40dotcraft%2Fsdk?logo=npm&label=npm)](https://www.npmjs.com/org/dotcraft)
[![License](https://img.shields.io/badge/license-Apache--2.0-blue)](./LICENSE)
[![Platforms](https://img.shields.io/badge/platform-Windows%20%7C%20macOS%20%7C%20Linux-lightgrey)](https://github.com/DotHarness/dotcraft/releases)
[![Discussions](https://img.shields.io/badge/community-Discussions-brightgreen)](https://github.com/DotHarness/dotcraft/discussions)

![DotCraft —— 可嵌入、可扩展的 Agent Runtime](https://github.com/DotHarness/resources/raw/master/dotcraft/intro.webp)

[English](./README.md) · [官方文档](https://www.dotcraft.net/zh/) · [快速开始](https://www.dotcraft.net/zh/getting-started) · [下载 Release](https://github.com/DotHarness/dotcraft/releases) · [License](./LICENSE)

DotCraft 是一个基于 C#/.NET 构建的开源、自托管 **AI Agent Runtime**。它可以作为桌面应用直接运行，也可以引入你自己的 .NET 应用，两者都能用插件扩展。

</div>


## 为什么选择 DotCraft？

![同一个 Agent Runtime，三种接入方式 —— 桌面应用、AppServer + SDK、Harness 包](https://github.com/DotHarness/resources/raw/master/dotcraft/entry.png)

**直接运行**

- **开箱即用：** Plan、subagents、Automations、Goals、Dreams 和 Dynamic Workflows 都已内置。Agent Builder 能把你的描述变成可复用的 Agent。
- **操作你的应用：** 借助电脑操控，Agent 可以在你允许的 Windows 应用里工作。
- **随处接着做：** Desktop、CLI、编辑器和聊天机器人共用同一个工作区。你还可以通过 SSH 连接服务器上的 DotCraft，或借助卫星让 Agent 在另一台电脑上工作。
- **部署和成本由你掌控：** 在本地或自己的服务器上运行，选用兼容的模型服务或 ChatGPT 订阅。提示词前缀保持逐字节稳定，提高缓存复用率。

**嵌入与扩展**

- **装进你的产品：** 把 DotCraft Desktop 背后的运行时嵌入你的 .NET 应用，或通过 AppServer、SDK 和 App Binding 接入现有产品。
- **用插件扩展：** .NET 插件可以添加工具、命令和生命周期逻辑。Agent 能自己编写插件，并在宿主运行时直接替换。React 插件可以改造 Desktop 的界面。

## 探索 DotCraft

![DotCraft Desktop、DotCraft.Harness、Oratorio、DotCraft Satellite 和 @dotcraft/avatar](https://github.com/DotHarness/resources/raw/master/dotcraft/products.webp)

- **[Desktop](https://www.dotcraft.net/zh/features/entry-points/desktop)：** 在一个桌面应用中与 Agent 一起处理项目。
- **[Harness](https://www.dotcraft.net/zh/developing/harness/)：** 将完整的 Agent 运行时嵌入你的 .NET 应用。
- **[Oratorio](https://www.dotcraft.net/zh/features/oratorio)：** 在同一看板上管理 Agent 任务，从分配到审阅。
- **[卫星](https://www.dotcraft.net/zh/features/agent-system/satellite)：** 让你的 Agent 在另一台电脑获准共享的文件夹中工作。
- **[Avatar](https://www.dotcraft.net/zh/developing/sdks/typescript#avatar-包)：** 用表情丰富、可自由搭配的头像，为你的 Agent 赋予鲜明个性。

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
- [modelcontextprotocol/csharp-sdk](https://github.com/modelcontextprotocol/csharp-sdk)
- [openai/symphony](https://github.com/openai/symphony)

## License

Apache License 2.0
