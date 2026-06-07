# Channels 与 Bots

DotCraft 可以把同一个 Agent 接进你团队本来就在用的聊天平台——QQ、企业微信、飞书 / Lark、Telegram、微信。渠道机器人和其他入口共用同一份会话核心、记忆、技能和安全策略，你在 Desktop 看到的上下文，就是机器人干活时用的同一份。

![DotCraft Channels 配置与会话](https://github.com/DotHarness/resources/raw/master/dotcraft/whats-new/channels.gif)

## 提供形式

| 渠道 | SDK 语言 | 文档 |
|---|---|---|
| QQ | TypeScript | [channel-qq](../../developing/channels/qq) |
| 企业微信 / WeCom | TypeScript | [channel-wecom](../../developing/channels/wecom) |
| 飞书 / Lark | TypeScript | [channel-feishu](../../developing/channels/feishu) |
| Telegram（TypeScript） | TypeScript | [channel-telegram](../../developing/channels/telegram) |
| 微信 | TypeScript | [channel-weixin](../../developing/channels/weixin) |
| Telegram（Python） | Python | [python-telegram](../../developing/channels/python-telegram) |

TypeScript 频道模块统一遵循 [TypeScript Module 集成契约](../../developing/integrations/typescript-module)，有标准的 `manifest`、`createModule`、`configDescriptors`、生命周期状态。

## 接入路径

![DotCraft channel adapter topology](/channel-adapter-topology.svg)

三种接入方式：

- **Desktop 内嵌渠道**：Desktop 替你启动渠道。打开 Desktop **Channels** 页面，填写平台 token、回调地址、白名单或扫码认证后一键启用。
- **服务器 Compose 部署**：使用 [服务器部署](../self-hosted/server-deployment)，通过 Docker Compose 启动 AppServer、内置 TypeScript 渠道和可选 OpenSandbox。
- **独立运行的适配器**：把渠道作为你自己的进程运行，并以 WebSocket 连接 AppServer，适合需要自行运维适配器的场景。

这几种方式背后的渠道注册与传输字段，见 [入口与服务](../../developing/configuration#entry-points-and-services)。各平台的连接、权限白名单和审批超时在 [配置完整参考](../../developing/configuration) 中设置。

## 渠道与统一会话核心

- 一条群聊 = 一个 Thread；同一个用户多次发言 = Thread 内追加 Turn / Item。
- 渠道收到的消息会带上群组、用户、消息类型等元信息，DotCraft 用这些元信息判断 Thread 归属。
- Agent 想发起审批（写文件、Shell 命令）时，渠道会把审批请求渲染成平台原生消息（按钮 / 引用），用户点同意才会执行。
- Desktop / TUI 可同时连接同一个 AppServer，看到机器人会话历史、接管会话、修正回复。

详细机制见 [统一会话核心](../../developing/architecture/session-core)。

## 适用场景

| 场景 | 推荐 |
|---|---|
| 团队内部知识库 bot | 飞书 / WeCom，企业内已有 IT 流程 |
| 开源社区答疑 | Telegram / QQ |
| 项目客服 / 售后 | 微信 / WeCom |
| 想在群里调 Agent 跑 CI 报告 | 任意渠道 + [Automations](../agent-system/automations) |
| 想在 Desktop 里看群聊历史并接管回复 | 任意渠道 + Desktop 同工作区 |

## 安全建议

接入外部渠道相当于把 Agent 暴露给可信度未知的用户输入，建议同时配置：

- 工作区外文件和 Shell 操作需要审批
- 收紧到必要工具表面积
- 使用强随机 AppServer WebSocket token
- 必要时启用 [OpenSandbox](../self-hosted/security#沙箱opensandbox)

完整建议和准确字段见 [安全与沙箱](../self-hosted/security) 与 [配置完整参考](../../developing/configuration#tools-security-与-sandbox)。

## 何时直接用 SDK 写自定义渠道

DotCraft 内置 5 个常用渠道。需要接入其他平台（Slack、Discord、Lark 私有部署、企业 IM 等）时：

- TypeScript：参考 [TypeScript SDK](../../developing/sdks/typescript) 与 [Module 集成契约](../../developing/integrations/typescript-module)
- Python：参考 [Python SDK](../../developing/sdks/python)
- 任何语言：直接对接 [AppServer Protocol](../../developing/protocols/appserver-protocol)

## 相关文档

- [统一会话核心](../../developing/architecture/session-core)
- [安全与沙箱](../self-hosted/security)
- [服务器部署](../self-hosted/server-deployment)
- [TypeScript SDK](../../developing/sdks/typescript) · [Python SDK](../../developing/sdks/python)
- [TypeScript Module 集成契约](../../developing/integrations/typescript-module)
