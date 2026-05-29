# Channels 与 Bots

DotCraft 通过 SDK 扩展把同一个工作区接入到主流社交平台：QQ、企业微信、飞书 / Lark、Telegram、微信。这些渠道复用同一份会话核心、记忆、技能、安全策略——你在 Desktop 看到的对话上下文，机器人也看得到。

![DotCraft Channels 配置与会话](https://github.com/DotHarness/resources/raw/master/dotcraft/whats-new/channels.gif)

## 提供形式

| 渠道 | SDK 语言 | 文档 |
|---|---|---|
| QQ | TypeScript | [channel-qq](../../developing/channels/qq.md) |
| 企业微信 / WeCom | TypeScript | [channel-wecom](../../developing/channels/wecom.md) |
| 飞书 / Lark | TypeScript | [channel-feishu](../../developing/channels/feishu.md) |
| Telegram（TypeScript） | TypeScript | [channel-telegram](../../developing/channels/telegram.md) |
| 微信 | TypeScript | [channel-weixin](../../developing/channels/weixin.md) |
| Telegram（Python） | Python | [python-telegram](../../developing/channels/python-telegram.md) |

TypeScript 频道模块统一遵循 [TypeScript Module 集成契约](../../developing/typescript-module.md)，有标准的 `manifest`、`createModule`、`configDescriptors`、生命周期状态。

## 接入路径

![DotCraft channel adapter topology](/channel-adapter-topology.svg)

两种接入方式：

- **Desktop 内嵌渠道**：Desktop 把渠道作为 subprocess 启动，使用 `transport: "subprocess"` 与 `builtinModule`。在 Desktop **Channels** 页面填写平台 token、回调地址、白名单或扫码认证后一键启用。
- **服务器 Compose 部署**：使用 [服务器部署](../../developing/server-deployment.md)，通过 Docker Compose 启动 AppServer、内置 TypeScript 渠道和可选 OpenSandbox。
- **独立运行的适配器**：通过 `transport: "websocket"` 让外部进程以 WebSocket 方式连接 AppServer，适合需要自行运维适配器进程的场景。

AppServer 与渠道注册字段见 [入口与服务](../../developing/configuration.md#entry-points-and-services)。平台连接、权限白名单和审批超时等渠道专属设置分别放在 `.craft/qq.json`、`.craft/wecom.json` 等适配器配置文件中。

## 渠道与统一会话核心

- 一条群聊 = 一个 Thread；同一个用户多次发言 = Thread 内追加 Turn / Item。
- 渠道收到的消息会带上群组、用户、消息类型等元信息，DotCraft 用这些元信息判断 Thread 归属。
- Agent 想发起审批（写文件、Shell 命令）时，渠道会把审批请求渲染成平台原生消息（按钮 / 引用），用户点同意才会执行。
- Desktop / TUI 可同时连接同一个 AppServer，看到机器人会话历史、接管会话、修正回复。

详细机制见 [统一会话核心](../session-core.md)。

## 适用场景

| 场景 | 推荐 |
|---|---|
| 团队内部知识库 bot | 飞书 / WeCom，企业内已有 IT 流程 |
| 开源社区答疑 | Telegram / QQ |
| 项目客服 / 售后 | 微信 / WeCom |
| 想在群里调 Agent 跑 CI 报告 | 任意渠道 + [Automations](../automations.md) |
| 想在 Desktop 里看群聊历史并接管回复 | 任意渠道 + Desktop 同工作区 |

## 安全建议

接入外部渠道相当于把 Agent 暴露给可信度未知的用户输入，建议同时配置：

- 工作区外文件和 Shell 操作需要审批
- 收紧到必要工具表面积
- 使用强随机 AppServer WebSocket token
- 必要时启用 [OpenSandbox](../security.md#沙箱opensandbox)

完整建议和准确字段见 [安全与沙箱](../security.md) 与 [配置完整参考](../../developing/configuration.md#tools-security-与-sandbox)。

## 何时直接用 SDK 写自定义渠道

DotCraft 内置 5 个常用渠道。需要接入其他平台（Slack、Discord、Lark 私有部署、企业 IM 等）时：

- TypeScript：参考 [TypeScript SDK](../../developing/sdk-typescript.md) 与 [Module 集成契约](../../developing/typescript-module.md)
- Python：参考 [Python SDK](../../developing/sdk-python.md)
- 任何语言：直接对接 [AppServer Protocol](../../developing/appserver-protocol.md)

## 故障排查

### 适配器连不上 AppServer

确认 AppServer 已以 WebSocket 模式启动，URL 包含 `/ws`，token 与客户端配置一致。

### 消息收得到但 Agent 不回复

检查适配器是否在初始化握手中声明了投递能力；模型 Provider 在 Dashboard Settings 页是否有合并结果；工具是否被审批拒绝。

### Desktop 看不到机器人会话

确认 Desktop 与机器人连接的是**同一个**工作区 / AppServer。

## 相关入口

- [统一会话核心](../session-core.md)
- [安全与沙箱](../security.md)
- [服务器部署](../../developing/server-deployment.md)
- [TypeScript SDK](../../developing/sdk-typescript.md) · [Python SDK](../../developing/sdk-python.md)
- [TypeScript Module 集成契约](../../developing/typescript-module.md)
