# DotCraft 快速开始

这条路径面向第一次使用 DotCraft 的用户：先安装 Desktop，选择项目目录初始化工作区，配置模型提供商，然后发起第一次对话。完成后再按需要进入 TUI、AppServer、API、SDK 或 Automations。

## 快速开始

### 1. 下载 Desktop

前往 [GitHub Releases](https://github.com/DotHarness/dotcraft/releases) 下载桌面应用：

| 平台 | 推荐文件 |
|------|----------|
| Windows | `DotCraft-vX.Y.Z-win-x64-Setup.exe` |
| macOS | `DotCraft-vX.Y.Z-macos-x64.dmg` |

Desktop 是推荐的第一入口，因为它把工作区选择、模型配置、会话、Diff、计划、自动化审核和运行状态放在同一个界面里。

![DotCraft](https://github.com/DotHarness/resources/raw/master/dotcraft/desktop.png)

如果你更喜欢从源码构建，请先安装 [.NET 10 SDK](https://dotnet.microsoft.com/download)、Rust toolchain 和 Node.js，然后在仓库根目录运行：

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

![Setup](https://github.com/DotHarness/resources/raw/master/dotcraft/setup.gif)

### 3. 配置模型

DotCraft 使用 Provider 注册表管理模型服务。常见方式包括：

| 方式 | 适合场景 |
|------|----------|
| Anthropic | 使用原生 Anthropic client 调用 Claude 模型 |
| OpenAI / OpenAI-compatible | 使用 OpenAI API、OpenRouter、DeepSeek、MiMo 等兼容接口 |
| ChatGPT 订阅 | 直接复用 ChatGPT Plus / Pro / Team / Business / Enterprise 订阅，无需单独 API Key |

最小配置通常只需要一个 `Providers` 注册表，以及当前选择的 `ProviderId` 和 `Model`：

```json
{
  "ProviderId": "anthropic",
  "Model": "claude-sonnet-4-5",
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

`Protocol: "anthropic"` 使用 Anthropic 原生接口，省略 `EndPoint` 时默认连接 `https://api.anthropic.com`。OpenAI-compatible Chat Completions 服务使用 `Protocol: "openai-chat-completions"`，第三方兼容端点通常需要填写以 `/v1` 结尾的 `EndPoint`。支持 OpenAI Responses API 的端点使用 `openai-responses`。DeepSeek V4 与 MiMo V2.5 在两种 OpenAI 协议下开箱可用，并自动启用思考控制。

已有 ChatGPT Plus / Pro / Team / Business / Enterprise 订阅时，可在初始化向导的 OpenAI 模板里选「使用 ChatGPT 登录」，或在终端运行 `dotcraft auth openai login`，无需 API Key 即可复用订阅。

敏感信息和端点放在全局 `~/.craft/config.json` 的 `Providers` 中；工作区通常只保存 `ProviderId` 和 `Model` 覆盖。需要手动编辑文件时，对应路径是全局 `~/.craft/config.json` 和工作区 `<workspace>/.craft/config.json`。完整字段见 [配置参考](./developing/configuration.md)。

### 4. 第一次运行

在 Desktop 中打开工作区后，新建会话并发送一个轻量请求，例如：

```text
请阅读这个仓库的 README 和 docs/index.md，告诉我这个项目怎么启动。
```

如果你更喜欢脚本友好的命令行入口，可以在项目目录运行一次性任务：

```bash
dotcraft exec "请阅读这个仓库的 README 和 docs/index.md，告诉我这个项目怎么启动。"
```

已经初始化的工作区中，`dotcraft` 不会进入交互式聊天；终端里的交互体验请使用 TUI。

如果你想使用终端富界面，请继续阅读 [TUI 指南](./features/entry-points/tui.md)。

## 理解入口模型

DotCraft 围绕 **统一会话核心（Unified Session Core）** 组织不同入口：命令行、Desktop、IDE、机器人与自动化并不是各自维护一套 agent 流程，而是复用同一个执行引擎与会话模型。

| 维度 | Gateway | Unified Session Core |
|------|---------|----------------------|
| 客户端定制 | 消息总线丢失难以定制 | 灵活自由的客户端 |
| 审批 / HITL | 无法表达平台原生的审批交互 | 以平台原生 UI 呈现 |
| 跨渠道恢复 | 不支持 | 会话可跨渠道恢复 |
| 工作区持久化 | 不支持 | 围绕工作区设计 |

![统一入口模型](https://github.com/DotHarness/resources/raw/master/dotcraft/entry.png)

DotCraft 将不同入口连接到同一个项目级工作空间，由统一会话核心负责承接执行、状态与编排。

## 配置

第一次使用只需要关心这些配置：

| 配置 | 用途 | 推荐位置 |
|------|------|----------|
| `Providers` | 模型提供商注册表，包含 API Key 和 Endpoint | 全局配置 |
| `ProviderId` | 当前使用的模型提供商 id | 全局或工作区配置 |
| `Model` | 默认模型名称 | 全局或工作区配置 |
| `Language` | 界面语言：`Chinese` / `English` | 全局配置 |
| `DashBoard.Enabled` | 启用 Web 调试与可视化配置 | 工作区配置 |

如果你不确定应该把配置放在哪里：Provider 放全局，工作区只覆盖 `ProviderId` 和 `Model`。

## 下一步按场景选择

| 我想做什么 | 下一步 |
|------------|--------|
| 使用图形界面持续协作 | [Desktop](./features/entry-points/desktop.md) |
| 在终端里使用完整界面 | [TUI](./features/entry-points/tui.md) |
| 远程或多客户端共享工作区 | [AppServer 模式](./developing/appserver.md) |
| 接入 IDE 或编辑器 | [IDE / 编辑器（ACP）](./features/entry-points/editors.md) |
| 构建机器人或外部适配器 | [Channels 与 Bots](./features/entry-points/channels.md) |
| 运行本地自动化任务 | [Automations 与 Goals](./features/automations.md) |
| 查看 Trace、工具调用和配置合并结果 | [可观测性](./features/observability.md) |

## 继续探索

### 社交渠道

DotCraft 通过 SDK 扩展集成 Telegram、微信、飞书、QQ、企业微信等社交渠道。详见 [Channels 与 Bots](./features/entry-points/channels.md)、[Python SDK](./developing/sdk-python.md) 与 [TypeScript SDK](./developing/sdk-typescript.md)。

| Telegram（Python SDK） | 微信（TypeScript SDK） |
|:---:|:---:|
| ![Telegram 渠道示例](https://github.com/DotHarness/resources/raw/master/dotcraft/telegram.jpg) | ![微信渠道示例](https://github.com/DotHarness/resources/raw/master/dotcraft/wechat.jpg) |

### Automations

Automations 适合运行本地工作区任务。更多调度、线程绑定、模板、Goals 和重试流程见 [Automations 与 Goals](./features/automations.md)。

| Desktop 自动化面板 |
|:---:|
| ![Desktop 自动化面板](https://github.com/DotHarness/resources/raw/master/dotcraft/desktop_automations.png) |

### Dashboard

Dashboard 是 DotCraft 的可视化观察与配置入口，用于查看会话、追踪调用和编辑工作区设置。详见 [可观测性](./features/observability.md)。

| 用量与会话概览 | 会话追踪 |
|:---:|:---:|
| ![Dashboard 用量概览](https://github.com/DotHarness/resources/raw/master/dotcraft/dashboard.png) | ![Dashboard 会话追踪](https://github.com/DotHarness/resources/raw/master/dotcraft/trace.png) |

## 进阶

- 使用 [Hooks](./features/security.md#hooks) 在生命周期事件中执行脚本。
- 使用 [安全与沙箱](./features/security.md) 限制文件、Shell 和网络能力。
- 使用 [示例与模板](./resources/samples.md) 验证完整工作区模板。
- 想从架构角度了解，跳到 [架构总览](./developing/architecture.md)。

## 故障排查

### Desktop 找不到 `dotcraft`

确认 DotCraft CLI 已在 `PATH` 中，或在 Desktop 设置中手动指定 AppServer / `dotcraft` 二进制路径。源码构建用户可先运行仓库根目录的 `build.bat`。

### 模型请求失败

检查当前 `ProviderId` 是否指向一个已配置的 `Providers[id]`，并确认该 Provider 的 `Protocol`、`ApiKey`、`EndPoint` 和 `Model` 匹配同一个服务。`Protocol: "openai-chat-completions"` 的兼容端点通常以 `/v1` 结尾；`Protocol: "anthropic"` 省略 `EndPoint` 时使用 Anthropic 官方默认地址。

### 工作区配置没有生效

确认配置写在当前工作区的 `.craft/config.json`，并重启 Desktop 或相关 Host。部分 AppServer 和入口配置只在启动时读取。
