# DotCraft 快速开始

这条路径面向第一次使用 DotCraft 的用户：先安装 Desktop，选择项目目录初始化工作区，配置模型提供商，然后发起第一次对话。完成后再按需要进入 CLI、AppServer、API、SDK 或 Automations。

## 快速开始

### 1. 下载 Desktop

前往 [GitHub Releases](https://github.com/DotHarness/dotcraft/releases) 下载桌面应用：

| 平台 | 推荐文件 |
|------|----------|
| Windows x64 | `DotCraft-vX.Y.Z-win-x64-Setup.exe` |
| Windows ARM64 | `DotCraft-vX.Y.Z-win-arm64-Setup.exe` |
| macOS Apple Silicon | `DotCraft-vX.Y.Z-macos-arm64.dmg` |
| macOS Intel | `DotCraft-vX.Y.Z-macos-x64.dmg` |

Desktop 是推荐的第一入口，因为它把工作区选择、模型配置、会话、Diff、计划、自动化审核和运行状态放在同一个界面里。

如果你只需要 CLI，可以直接安装：

```bash
curl -fsSL https://www.dotcraft.net/install.sh | bash
```

Windows PowerShell：

```powershell
irm https://www.dotcraft.net/install.ps1 | iex
```

Windows PowerShell 安装脚本会自动选择 x64 或 ARM64 的 CLI 归档。macOS/Linux shell 安装脚本会在 macOS 上自动选择 x64 或 ARM64；Linux 的 CLI Release 归档目前仍只提供 x64。

![DotCraft](https://github.com/DotHarness/resources/raw/master/dotcraft/desktop.png)

如果你更喜欢从源码构建，请先安装 [.NET 10 SDK](https://dotnet.microsoft.com/download) 和 Node.js，然后在仓库根目录运行：

```bash
build.bat
```

Linux / macOS 可运行：

```bash
bash build_linux.bat
```

### 2. 初始化工作区

首次打开 Desktop 后选择一个真实项目目录作为工作区。DotCraft 会把这个项目的配置、会话、任务、技能和附件跟随项目保存，之后从 Desktop、终端或自动化入口进入时都能继续使用同一份上下文。

如果你想从终端完成首次初始化，也可以在还没有 `.craft/` 的项目目录中运行：

```bash
dotcraft
```

建议从真实项目目录开始，而不是空目录。这样 Agent 可以直接读取仓库结构、现有文档和构建脚本。

如果 Desktop 在首次初始化时发现项目级 `AGENTS.md` 或 `CLAUDE.md`，会提供一次性导入选项，把其中一个文件复制到 `.craft/AGENTS.md`。原文件不会被修改，之后 DotCraft 只以 `.craft/AGENTS.md` 作为项目指令来源。

如果项目还没有 `.craft/AGENTS.md`，可以在 Desktop、CLI 或其他支持命令的客户端中输入 `/init`。DotCraft 会调研仓库的真实结构、命令和约定，然后生成一份简洁的英文项目指令。`/init` 不会替换已有的 `.craft/AGENTS.md`。

![Setup](https://github.com/DotHarness/resources/raw/master/dotcraft/setup.gif)

### 3. 配置模型

在 Desktop 中，初始化向导会全程引导你完成：选择提供商、填入 API Key、选择模型，无需手动编辑文件。DotCraft 支持三种常见方式：

| 方式 | 适合场景 |
|------|----------|
| Anthropic | 使用原生 Anthropic client 调用 Claude 模型 |
| OpenAI / OpenAI-compatible | 使用 OpenAI API、OpenRouter、DeepSeek、MiMo 等兼容接口 |
| ChatGPT 订阅 | 直接复用 ChatGPT Plus / Pro / Team / Business / Enterprise 订阅，无需单独 API Key |

已有 ChatGPT Plus / Pro / Team / Business / Enterprise 订阅时，可在向导的 OpenAI 模板里选「使用 ChatGPT 登录」，或在初始化后运行 `dotcraft auth openai login`，无需 API Key 即可复用订阅。

更喜欢直接编辑配置？最小配置就是一份提供商清单，加上当前选择的 `ProviderId` 及其 `ProviderModels` 条目：

```json
{
  "ProviderId": "anthropic",
  "ProviderModels": {
    "anthropic": "claude-sonnet-4-5"
  },
  "Providers": {
    "anthropic": {
      "DisplayName": "Anthropic",
      "Protocol": "anthropic",
      "ApiKey": "${ANTHROPIC_API_KEY}"
    },
    "openrouter": {
      "DisplayName": "OpenRouter",
      "Protocol": "openai-chat-completions",
      "ApiKey": "${OPENROUTER_API_KEY}",
      "EndPoint": "https://openrouter.ai/api/v1"
    }
  }
}
```

API Key 和端点放在全局文件 `~/.craft/config.json`；工作区通常只覆盖 `ProviderId` 和 `ProviderModels`。协议、端点，以及 OpenAI 兼容服务的 `/v1` 规则等完整字段，见 [配置参考](./developing/configuration)。

### 4. 第一次运行

在 Desktop 中打开工作区后，新建会话并发送一个轻量请求，例如：

```text
请阅读这个仓库的 README 和 docs/index.md，告诉我这个项目怎么启动。
```

如果你更喜欢脚本友好的命令行入口，可以在项目目录运行一次性任务：

```bash
dotcraft exec "请阅读这个仓库的 README 和 docs/index.md，告诉我这个项目怎么启动。"
```

直接运行 `dotcraft` 只负责首次初始化——在已经有 `.craft/` 的工作区里，它不会进入交互式聊天。一次性终端任务请使用 `dotcraft exec`。

## 一个工作区，连接所有入口

DotCraft 把 Desktop、终端、IDE、机器人与自动化都连接到同一个项目工作区。在一个入口开始的对话，可以在另一个入口继续，共享同一份会话、记忆和工具。

![统一入口模型](https://github.com/DotHarness/resources/raw/master/dotcraft/entry.png)

想了解一个引擎如何支撑所有入口？见 [架构总览](./developing/architecture/overview)。

## 配置

第一次使用只需要关心这些配置：

| 配置 | 用途 | 推荐位置 |
|------|------|----------|
| `Providers` | 模型提供商注册表，包含 API Key 和 Endpoint | 全局配置 |
| `ProviderId` | 当前使用的模型提供商 id | 全局或工作区配置 |
| `ProviderModels` | 按 provider id 保存的 MainAgent 模型名称 | 全局或工作区配置 |
| `DashBoard.Enabled` | 启用 Web 调试与可视化配置 | 工作区配置 |

如果你不确定应该把配置放在哪里：Provider 放全局，工作区只覆盖 `ProviderId` 和 `ProviderModels`。

## 下一步按场景选择

| 我想做什么 | 下一步 |
|------------|--------|
| 使用图形界面持续协作 | [Desktop](./features/entry-points/desktop) |
| 在终端或 CI 中运行一次性任务 | [使用 `dotcraft exec`](#4-第一次运行) |
| 远程或多客户端共享工作区 | [AppServer 模式](./developing/lifecycle/appserver) |
| 接入 IDE 或编辑器 | [IDE / 编辑器（ACP）](./features/entry-points/editors) |
| 构建机器人或外部适配器 | [Channels 与 Bots](./features/entry-points/channels) |
| 运行本地自动化任务 | [Automations 与 Goals](./features/agent-system/automations) |
| 查看 Trace、工具调用和配置合并结果 | [可观测性](./features/self-hosted/observability) |

## 继续探索

### 社交渠道

DotCraft 通过 SDK 扩展集成 Telegram、微信、飞书、QQ、企业微信等社交渠道。详见 [Channels 与 Bots](./features/entry-points/channels)、[Python SDK](./developing/sdks/python) 与 [TypeScript SDK](./developing/sdks/typescript)。

| Telegram（Python SDK） | 微信（TypeScript SDK） |
|:---:|:---:|
| ![Telegram 渠道示例](https://github.com/DotHarness/resources/raw/master/dotcraft/telegram.jpg) | ![微信渠道示例](https://github.com/DotHarness/resources/raw/master/dotcraft/wechat.jpg) |

### Automations

Automations 适合运行本地工作区任务。更多调度、线程绑定、模板、Goals 和重试流程见 [Automations 与 Goals](./features/agent-system/automations)。

| Desktop 自动化面板 |
|:---:|
| ![Desktop 自动化面板](https://github.com/DotHarness/resources/raw/master/dotcraft/desktop_automations.png) |

### Dashboard

Dashboard 是 DotCraft 的可视化观察与配置入口，用于查看会话、追踪调用和编辑工作区设置。详见 [可观测性](./features/self-hosted/observability)。

| 用量与会话概览 | 会话追踪 |
|:---:|:---:|
| ![Dashboard 用量概览](https://github.com/DotHarness/resources/raw/master/dotcraft/dashboard.png) | ![Dashboard 会话追踪](https://github.com/DotHarness/resources/raw/master/dotcraft/trace.png) |

## 进阶

- 使用 [生命周期 Hooks](./features/agent-system/hooks) 在生命周期事件中执行脚本。
- 使用 [安全与沙箱](./features/self-hosted/security) 限制文件、Shell 和网络能力。
- 使用 [插件与工具](./features/agent-system/plugins-tools) 安装可复用能力包。
- 想从架构角度了解，跳到 [架构总览](./developing/architecture/overview)。
