<div align="center">

<img alt="DotCraft：可嵌入、可扩展的 Agent Runtime，开源的 Agent 桌面应用及其背后的运行时。桌面应用打开 orbit 项目，两个子智能体已完成，Agent 正在运行对比度检查。" src="https://github.com/DotHarness/resources/raw/master/dotcraft/readme/hero.webp" width="830">

[![Release](https://img.shields.io/github/v/release/DotHarness/dotcraft)](https://github.com/DotHarness/dotcraft/releases)
[![NuGet](https://img.shields.io/nuget/v/DotCraft.Harness?logo=nuget&label=NuGet)](https://www.nuget.org/profiles/DotHarness)
[![npm](https://img.shields.io/npm/v/%40dotcraft%2Fsdk?logo=npm&label=npm)](https://www.npmjs.com/org/dotcraft)
[![License](https://img.shields.io/badge/license-Apache--2.0-blue)](./LICENSE)
[![Platforms](https://img.shields.io/badge/platform-Windows%20%7C%20macOS%20%7C%20Linux%20%7C%20Android-lightgrey)](https://github.com/DotHarness/dotcraft/releases)

[English](./README.md) · [官方文档](https://www.dotcraft.net/zh/) · [在线体验](https://www.dotcraft.net/demo/index.html?lang=zh) · [快速开始](https://www.dotcraft.net/zh/start/)

</div>

DotCraft 是一个开源的 Agent Runtime，运行在你自己的机器上。用桌面应用写代码、处理日常工作，在手机上随时跟进；也可以把同一个 Agent 装进你自己的应用，再用插件扩展它。

## 为什么选择 DotCraft？

- **一个 Agent，多个入口：** Desktop、CLI、IDE、手机和聊天机器人共用同一个工作区，在一处开始的任务可以在另一处接着做。你还可以通过 SSH 连接服务器上的 DotCraft，或借助卫星让 Agent 在另一台电脑上工作。
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

完整说明请查看[快速开始](https://www.dotcraft.net/zh/start/)。

## Desktop

<p align="center"><a href="https://www.dotcraft.net/zh/start/desktop"><img src="https://github.com/DotHarness/resources/raw/master/dotcraft/readme/desktop.webp" width="830" alt="DotCraft Desktop 上的「Plan: ship dark mode」：Theme toggle 和 Snapshot key screens 两个子智能体已完成，Agent 正在运行 npm run test:contrast，跑完才算完成。"></a></p>

一个工作台：Agent 在你的项目里做计划，把任务拆给子智能体，确认结果没问题后才告诉你完成了。

**开箱即用**

- **Plan** 先列出步骤，等你确认再动手。
- **子智能体** 把任务拆开，并行处理各个部分。
- **应用内浏览器** 打开你的应用，让 Agent 自己检查改动；你也可以直接指着页面上的任何地方提修改。
- **电脑操控** 让 Agent 操作你允许的 Windows 应用。
- **Agent Builder** 把一段描述变成可复用的 Agent。
- **Automations**、**Goals**、**Dreams** 和 **Dynamic Workflows** 在你离开时也让工作继续推进。
- **你的宠物** 待在输入框上，在 Agent 工作时找到可以穿戴的道具。

[Desktop 指南](https://www.dotcraft.net/zh/start/desktop) · [子智能体](https://www.dotcraft.net/zh/guide/subagents) · [Agent Profiles](https://www.dotcraft.net/zh/guide/agent-profiles) · [Automations](https://www.dotcraft.net/zh/guide/automations)

## 手机 App

<p align="center"><a href="https://www.dotcraft.net/zh/start/mobile"><img src="https://github.com/DotHarness/resources/raw/master/dotcraft/readme/mobile.webp" width="830" alt="DotCraft 手机 App 询问是否在 orbit 项目中运行 npm run test:contrast，可选 Allow once、Allow for session 或 Reject。"></a></p>

Agent 在你的电脑上干活时，你可以在手机上跟进：看哪些聊天在运行，处理审批和提问，追加消息或停止聊天，还能在任意项目里开新聊天。

**获取方式：** [下载](https://github.com/DotHarness/dotcraft/releases) Android App，然后在 Desktop 的**设置 → 连接 → 手机**里扫码配对。

[手机 App 指南](https://www.dotcraft.net/zh/start/mobile) · [随处访问](https://www.dotcraft.net/zh/contribute/lifecycle/hub#随处访问)

## CLI

<p align="center"><a href="https://www.dotcraft.net/zh/start/entry-points"><img src="https://github.com/DotHarness/resources/raw/master/dotcraft/readme/cli.webp" width="830" alt="终端里运行 dotcraft exec &quot;Which colors in src/components are still hard-coded?&quot;：回答列出 Card.css 和 Chart.css 里仍然写死的颜色，并说明 Button.css 已经使用主题 token。"></a></p>

一条命令运行任务，答案直接回到终端，在笔记本、服务器或 CI 里都能用。

```bash
dotcraft exec "src/components 里还有哪些颜色是写死的？"
```

[入口概览](https://www.dotcraft.net/zh/start/entry-points) · [IDE](https://www.dotcraft.net/zh/start/editors)

## 装进你的产品

<p align="center"><a href="https://www.dotcraft.net/zh/extend/harness/"><img src="https://github.com/DotHarness/resources/raw/master/dotcraft/readme/harness.webp" width="830" alt="Program.cs 用 ISessionService 创建线程，并流式输出 Agent 对 Check contrast in dark mode 的回复。"></a></p>

**Harness** 把 DotCraft Desktop 背后的完整 Agent 运行时装进你自己的 .NET 应用：会话、工具、审批和模型提供商，都跟随宿主的生命周期。

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

已经有自己的应用？用 **SDK** 让它成为 DotCraft 客户端：连接工作区、启动线程、运行一轮对话，支持 TypeScript 和 .NET。

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

[Harness 指南](https://www.dotcraft.net/zh/extend/harness/) · [.NET 插件](https://www.dotcraft.net/zh/extend/dotnet-plugins) · [SDK 快速开始](https://www.dotcraft.net/zh/extend/sdks/quickstart) · [App Binding](https://www.dotcraft.net/zh/extend/app-binding) · [Desktop 插件](https://www.dotcraft.net/zh/extend/desktop-plugins)

## 更多

<p align="center"><img src="https://github.com/DotHarness/resources/raw/master/dotcraft/readme/more.webp" width="830" alt="Satellite 请求运行 npm run test:contrast 的权限、Oratorio 看板上进行中的 Chart labels fail contrast、已连接的聊天机器人，以及三个 Avatar 形象。"></p>

- [**卫星**](https://www.dotcraft.net/zh/guide/satellite)：让你的 Agent 在另一台 Windows 电脑上工作。共享电脑的人可以选择完全访问或只开放一个文件夹，在屏幕顶部看到每个请求，并随时暂停共享。在 Desktop 打开 **设置 → 连接 → 卫星 → 邀请**，把链接发给对方，对方在那个页面下载 DotCraft Satellite。
- [**Oratorio**](https://www.dotcraft.net/zh/guide/oratorio/)：内置在 Desktop 里的项目看板。本地任务、GitHub 的 issue 和 pull request、GitLab 的 issue 和 merge request 都汇集在同一块看板上；把卡片交给 Agent，跟进执行过程，评审结果。在 Desktop 侧边栏选择 **Oratorio**。[工作流](https://www.dotcraft.net/zh/guide/oratorio/workflow)
- [**聊天机器人**](https://www.dotcraft.net/zh/guide/channels/)：把 DotCraft 拉进群聊，团队成员不用打开 Desktop 也能问项目的事。支持飞书、QQ、Telegram、企业微信和微信，这些对话和其他入口共用同一个工作区。在 Desktop 打开 **渠道**，选择平台并填写凭据。
- [**Avatar**](https://www.dotcraft.net/zh/extend/sdks/typescript#avatar-包)：DotCraft Desktop 内置桌宠和 Agent Profile 个性化形象背后的角色，开放为 React 组件 npm 包，方便你在自己的应用里集成展示。形象会随 Agent 的状态变化而动起来，160 多件收藏道具可以换装，同一个名字永远画出同一个形象。`npm install @dotcraft/avatar`

## 贡献代码

开始前请阅读 [CONTRIBUTING.md](./CONTRIBUTING.md)。

## 致谢

DotCraft 受以下项目启发，并向它们致谢：

- [HKUDS/nanobot](https://github.com/HKUDS/nanobot)
- [openai/codex](https://github.com/openai/codex)
- [microsoft/agent-framework](https://github.com/microsoft/agent-framework)
- [openai/symphony](https://github.com/openai/symphony)

## License

Apache License 2.0
