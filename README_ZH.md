<div align="center">

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="https://github.com/DotHarness/resources/raw/master/dotcraft/readme/hero-dark.webp">
  <img alt="DotCraft：可嵌入、可扩展的 Agent Runtime。把任务交给桌面应用，它会拆好计划并逐项完成。" src="https://github.com/DotHarness/resources/raw/master/dotcraft/readme/hero-light.webp" width="830">
</picture>

[![Release](https://img.shields.io/github/v/release/DotHarness/dotcraft)](https://github.com/DotHarness/dotcraft/releases)
[![NuGet](https://img.shields.io/nuget/v/DotCraft.Harness?logo=nuget&label=NuGet)](https://www.nuget.org/profiles/DotHarness)
[![npm](https://img.shields.io/npm/v/%40dotcraft%2Fsdk?logo=npm&label=npm)](https://www.npmjs.com/org/dotcraft)
[![License](https://img.shields.io/badge/license-Apache--2.0-blue)](./LICENSE)
[![Platforms](https://img.shields.io/badge/platform-Windows%20%7C%20macOS%20%7C%20Linux-lightgrey)](https://github.com/DotHarness/dotcraft/releases)
[![Discussions](https://img.shields.io/badge/community-Discussions-brightgreen)](https://github.com/DotHarness/dotcraft/discussions)

[English](./README.md) · [官方文档](https://www.dotcraft.net/zh/) · [快速开始](https://www.dotcraft.net/zh/getting-started) · [下载 Release](https://github.com/DotHarness/dotcraft/releases) · [License](./LICENSE)

</div>

DotCraft 是一个开源的 AI Agent，运行在你自己的机器上。用桌面应用写代码、处理日常工作；也可以把同一个 Agent 装进你自己的应用，再用插件扩展它。

## 都有什么

<p align="center"><b>直接用</b></p>
<p align="center"><a href="#desktop"><img src="https://github.com/DotHarness/resources/raw/master/dotcraft/readme/badge-desktop.webp" width="264" alt="Desktop：在你的项目里规划、动手、自己验收。下载安装。"></a> <a href="#cli"><img src="https://github.com/DotHarness/resources/raw/master/dotcraft/readme/badge-cli.webp" width="264" alt="CLI：一条命令，答案直接回到终端。安装脚本。"></a></p>

<p align="center"><b>接到更多地方</b></p>
<p align="center"><a href="#卫星"><img src="https://github.com/DotHarness/resources/raw/master/dotcraft/readme/badge-satellite.webp" width="264" alt="卫星：你的 Agent 在另一台 Windows 电脑上工作。下载安装。"></a> <a href="#oratorio"><img src="https://github.com/DotHarness/resources/raw/master/dotcraft/readme/badge-oratorio.webp" width="264" alt="Oratorio：每个任务从派发到评审，都在一块看板上。已内置。"></a> <a href="#聊天机器人"><img src="https://github.com/DotHarness/resources/raw/master/dotcraft/readme/badge-chat-bots.webp" width="264" alt="聊天机器人：在群聊里直接问项目的事。已内置。"></a></p>

<p align="center"><b>拿来开发</b></p>
<p align="center"><a href="#harness"><img src="https://github.com/DotHarness/resources/raw/master/dotcraft/readme/badge-harness.webp" width="264" alt="Harness：整套 Agent 运行时，装进你的 .NET 应用。NuGet。"></a> <a href="#sdk"><img src="https://github.com/DotHarness/resources/raw/master/dotcraft/readme/badge-sdks.webp" width="264" alt="SDK：让你的应用成为 DotCraft 客户端。npm 与 NuGet。"></a> <a href="#avatar"><img src="https://github.com/DotHarness/resources/raw/master/dotcraft/readme/badge-avatar.webp" width="264" alt="Avatar：给你的 Agent 一张会动、能换装的脸。npm。"></a></p>

## 为什么选择 DotCraft？

- **一个 Agent，多个入口：** Desktop、CLI、IDE 和聊天机器人共用同一个工作区，在一处开始的任务可以在另一处接着做。你还可以通过 SSH 连接服务器上的 DotCraft，或借助卫星让 Agent 在另一台电脑上工作。
- **可以在它之上构建：** 把 DotCraft Desktop 背后的运行时嵌入你的 .NET 应用，或通过 SDK 和 App Binding 接入现有产品。.NET 插件可以添加工具、命令和生命周期逻辑，Agent 能自己编写插件，并在宿主运行时直接替换。React 插件可以改造 Desktop 的界面。
- **部署和成本由你掌控：** 在本地或自己的服务器上运行，选用兼容的模型提供商或 ChatGPT 订阅。提示词前缀保持逐字节稳定，提高缓存复用率。
- **团队共享模型：** 在一台机器上集中保存 API key 和登录，团队其他机器上的 DotCraft 都通过它调用模型，无需各自保存密钥或直连模型提供商。

## 快速开始

**Desktop**

1. 从 [GitHub Releases](https://github.com/DotHarness/dotcraft/releases) 下载适用于 Windows、macOS 或 Linux 的安装包。
2. 选择一个项目目录作为工作区。
3. 接入模型：任意兼容 OpenAI 或 Anthropic 协议、使用 API key 的模型提供商，或者用你的 ChatGPT 订阅登录。

**CLI**

```bash
# macOS / Linux
curl -fsSL https://www.dotcraft.net/install.sh | bash
```

```powershell
# Windows PowerShell
irm https://www.dotcraft.net/install.ps1 | iex
```

完整说明请查看[快速开始](https://www.dotcraft.net/zh/getting-started)。

## Desktop

<p align="center"><picture>
  <source media="(prefers-color-scheme: dark)" srcset="https://github.com/DotHarness/resources/raw/master/dotcraft/readme/header-desktop-dark.png">
  <a href="https://www.dotcraft.net/zh/features/entry-points/desktop"><img src="https://github.com/DotHarness/resources/raw/master/dotcraft/readme/header-desktop-light.png" width="830" alt="DotCraft Desktop：「Ship dark mode」计划已完成两步，子智能体面板里有两个子智能体正在处理后续步骤。"></a>
</picture></p>

一个工作台：Agent 在你的项目里做计划，把任务拆给子智能体，确认结果没问题后才告诉你完成了。

**获取方式：** [下载](https://github.com/DotHarness/dotcraft/releases)，支持 Windows、macOS 和 Linux。

<p align="center"><picture>
  <source media="(prefers-color-scheme: dark)" srcset="https://github.com/DotHarness/resources/raw/master/dotcraft/readme/desktop-strip-dark.webp">
  <img src="https://github.com/DotHarness/resources/raw/master/dotcraft/readme/desktop-strip-light.webp" width="830" alt="依次展示 Desktop 的四项能力：应用内浏览器检查你的应用设置页、电脑操控在另一个 Windows 应用里工作、Agent Builder 配置新 Agent、宠物发现可穿戴的龙之翼。">
</picture></p>

**开箱即用**

- **Plan** 先列出步骤，等你确认再动手。
- **子智能体** 把任务拆开，并行处理各个部分。
- **应用内浏览器** 打开你的应用，让 Agent 自己检查改动；你也可以直接指着页面上的任何地方提修改。
- **电脑操控** 让 Agent 操作你允许的 Windows 应用。
- **Agent Builder** 把一段描述变成可复用的 Agent。
- **Automations**、**Goals**、**Dreams** 和 **Dynamic Workflows** 在你离开时也让工作继续推进。
- **你的宠物** 待在输入框上，在 Agent 工作时找到可以穿戴的道具。

**你会得到：** 打开一个项目，输入「先规划一下怎么给应用加上深色模式，然后把它做完。」，确认计划，看着步骤逐项完成。

[Desktop 指南](https://www.dotcraft.net/zh/features/entry-points/desktop) · [子智能体](https://www.dotcraft.net/zh/features/agent-system/subagents) · [Agent Profiles](https://www.dotcraft.net/zh/features/agent-system/agent-profiles) · [Automations](https://www.dotcraft.net/zh/features/agent-system/automations)

## CLI

<p align="center"><a href="https://www.dotcraft.net/zh/features/entry-points/"><img src="https://github.com/DotHarness/resources/raw/master/dotcraft/readme/header-cli.png" width="830" alt="终端里运行 dotcraft exec：Agent 列出 Card.css 和 Chart.css 里仍然写死的颜色。"></a></p>

一条命令运行任务，答案直接回到终端，在笔记本、服务器或 CI 里都能用。

```bash
dotcraft exec "src/components 里还有哪些颜色是写死的？"
```

**你会得到：** 终端里的一段纯文本回答，并保存在 Desktop 使用的同一个工作区里。IDE 也连接到这个工作区。

[入口概览](https://www.dotcraft.net/zh/features/entry-points/) · [IDE](https://www.dotcraft.net/zh/features/entry-points/editors)

## 卫星

<p align="center"><a href="https://www.dotcraft.net/zh/features/agent-system/satellite"><img src="https://github.com/DotHarness/resources/raw/master/dotcraft/readme/header-satellite.png" width="830" alt="另一台电脑的屏幕顶部显示卫星胶囊：Ann 请求运行 npm run test:contrast，可选择拒绝或允许一次。"></a></p>

让你的 Agent 在另一台 Windows 电脑上工作。共享电脑的人可以选择完全访问或只开放一个文件夹，在屏幕顶部看到每个请求，并随时暂停共享。

**获取方式：** 在 Desktop 打开 **设置 → 连接 → 卫星 → 邀请**，把链接发给对方。对方在那个页面下载 DotCraft Satellite。

**你会得到：** 在输入框下方的 **执行位置** 里选中那台电脑，下一个任务就在那里运行。

[卫星指南](https://www.dotcraft.net/zh/features/agent-system/satellite)

## Oratorio

<p align="center"><picture>
  <source media="(prefers-color-scheme: dark)" srcset="https://github.com/DotHarness/resources/raw/master/dotcraft/readme/header-oratorio-dark.png">
  <a href="https://www.dotcraft.net/zh/features/oratorio"><img src="https://github.com/DotHarness/resources/raw/master/dotcraft/readme/header-oratorio-light.png" width="830" alt="Oratorio 看板：一个 issue 正在由 Agent 处理，一个 pull request 等待评审，检查已通过。"></a>
</picture></p>

内置在 Desktop 里的项目看板。本地任务、GitHub 的 issue 和 pull request、GitLab 的 issue 和 merge request 都汇集在同一块看板上；把卡片交给 Agent，跟进执行过程，评审结果。

**获取方式：** 已内置。在 Desktop 侧边栏选择 **Oratorio**。

**你会得到：** 一张由 Agent 在独立 worktree 里处理的任务卡，处理完后停在评审列等你。

[Oratorio 指南](https://www.dotcraft.net/zh/features/oratorio) · [工作流](https://www.dotcraft.net/zh/features/oratorio/workflow)

## 聊天机器人

<p align="center"><picture>
  <source media="(prefers-color-scheme: dark)" srcset="https://github.com/DotHarness/resources/raw/master/dotcraft/readme/header-chat-bots-dark.png">
  <a href="https://www.dotcraft.net/zh/features/channels/"><img src="https://github.com/DotHarness/resources/raw/master/dotcraft/readme/header-chat-bots-light.png" width="830" alt="Desktop 的渠道页面，已连接飞书、QQ、Telegram 和企业微信。"></a>
</picture></p>

把 DotCraft 拉进群聊，团队成员不用打开 Desktop 也能问项目的事。支持飞书 / Lark、QQ、Telegram、企业微信和微信，这些对话和其他入口共用同一个工作区。

**获取方式：** 已内置。在 Desktop 打开 **渠道**，选择平台并填写凭据。

**你会得到：** 机器人在你的群聊里回复一条测试消息。

[渠道与机器人](https://www.dotcraft.net/zh/features/channels/)

## Harness

<p align="center"><a href="https://www.dotcraft.net/zh/developing/harness/"><img src="https://github.com/DotHarness/resources/raw/master/dotcraft/readme/header-harness.png" width="830" alt="Program.cs 用 ISessionService 创建线程，并流式输出 Agent 的回复。"></a></p>

DotCraft Desktop 背后的完整 Agent 运行时，装进你自己的 .NET 应用：会话、工具、审批和模型提供商，都跟随宿主的生命周期。

```bash
dotnet add package DotCraft.Harness
```

```csharp
var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddDotCraftHarness(appConfig, options =>
    options.WorkspacePath = workspacePath);

using var host = builder.Build();
await host.StartAsync();
```

**你会得到：** 一个运行中的宿主，它的 `ISessionService` 能创建线程，并把每一轮的事件流式推送到你的界面。

[Harness 指南](https://www.dotcraft.net/zh/developing/harness/) · [.NET 插件](https://www.dotcraft.net/zh/developing/integrations/dotnet-plugins)

## SDK

<p align="center"><a href="https://www.dotcraft.net/zh/developing/sdks/quickstart"><img src="https://github.com/DotHarness/resources/raw/master/dotcraft/readme/header-sdks.png" width="830" alt="app.ts 用 TypeScript SDK 启动线程，并流式输出 Agent 的回复。"></a></p>

让你的应用成为 DotCraft 客户端：连接工作区、启动线程、运行一轮对话，支持 TypeScript 和 .NET。

```bash
npm install @dotcraft/sdk        # 或：dotnet add package DotCraft.Sdk
```

```ts
import { DotCraft } from "@dotcraft/sdk";

const dotcraft = await DotCraft.local({ workspacePath: "/path/to/workspace" });
const thread = await dotcraft.threads.start({ userId: "me" });
const result = await thread.run("Check contrast in dark mode.");
console.log(result.text);
```

**你会得到：** 你自己的代码打印出 Agent 的回复，这个线程在 Desktop 里也能打开。

[SDK 快速开始](https://www.dotcraft.net/zh/developing/sdks/quickstart) · [App Binding](https://www.dotcraft.net/zh/developing/integrations/app-binding) · [Desktop 插件](https://www.dotcraft.net/zh/developing/integrations/desktop-plugins)

## Avatar

<p align="center"><a href="https://www.dotcraft.net/zh/developing/sdks/typescript#avatar-包"><img src="https://github.com/DotHarness/resources/raw/master/dotcraft/readme/header-avatar.webp" width="830" alt="一群穿着不同装扮的 DotCraft 机器人，最前面的一个戴着云龙和星星眼镜。"></a></p>

就是你在 Desktop 里看到的那个机器人，做成了 React 组件。它会随 Agent 的状态变化而动起来，160 多件收藏道具可以给它换装，同一个名字永远画出同一个形象。

```bash
npm install @dotcraft/avatar
```

[Avatar 包](https://www.dotcraft.net/zh/developing/sdks/typescript#avatar-包)

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
