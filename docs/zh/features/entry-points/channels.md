# Channels 与 Bots

DotCraft 可以在你团队已经使用的聊天工具中回复：QQ、企业微信、飞书 / Lark、Telegram 和微信。

![DotCraft Channels 配置与会话](https://github.com/DotHarness/resources/raw/master/dotcraft/whats-new/channels.gif)

## 接入渠道

1. 在 DotCraft Desktop 打开一个 workspace。
2. 打开 **Channels**。
3. 选择要接入的平台。
4. 填写表单中显示的平台凭据。
5. 在对应平台后台或 Bot 工具里完成设置。
6. 启用渠道。
7. 给 Bot 发送一条测试消息。

Desktop 会为你管理内置 TypeScript 渠道进程。只有在想自行运行适配器时，才使用独立适配器路径。

## 内置渠道

| 平台 | 接入方式 | 主要能力 | 设置 |
|---|---|---|---|
| **QQ** | NapCat 或 OneBot v11 反向 WebSocket | 私聊、群聊、审批关键词、媒体投递 | [QQ 设置](../../developing/channels/qq) |
| **企业微信 / WeCom** | 群机器人回调 URL、Token、EncodingAESKey | 企业微信群聊、审批、文件和图片投递 | [企业微信设置](../../developing/channels/wecom) |
| **飞书 / Lark** | 启用 Bot 和 WebSocket 事件订阅的自建应用 | 卡片回复、审批、reaction、可选官方 CLI | [飞书设置](../../developing/channels/feishu) |
| **Telegram** | BotFather token 和 long polling | 私聊、群聊、`/new`、`/help`、inline 审批 | [Telegram 设置](../../developing/channels/telegram) |
| **微信 / Weixin** | 腾讯 iLink 二维码登录 | 微信聊天、保存登录会话、纯文本回复、文件和图片投递 | [微信设置](../../developing/channels/weixin) |
| **Telegram（Python）** | Python 独立适配器 | 自定义 Python 渠道工作的参考适配器 | [Python Telegram 设置](../../developing/channels/python-telegram) |

## 渠道会话如何工作

![DotCraft channel adapter topology](/channel-adapter-topology.svg)

- 发给已连接 Bot 的消息会成为 DotCraft 会话回合。
- 回复会自动投递回同一个聊天。
- 平台支持时，审批和用户输入请求会出现在聊天里。
- 支持斜杠命令的渠道中，`/new` 会开启新会话。
- Desktop 可以打开同一个 workspace，用来查看历史或继续会话。

底层模型见 [Unified Session Core](../../developing/architecture/session-core)。

## 交接会话

![DotCraft 社交渠道接续](https://github.com/DotHarness/resources/raw/master/dotcraft/whats-new/channel-handoff.gif)

在会话的 Apps 菜单中，可以把已有 Desktop 会话绑定到已连接的社交渠道。DotCraft 会显示一个 `/bind 123456` 命令；在目标聊天里发送该命令后，就能在那里继续同一条会话。

绑定只作用于该聊天。其他聊天仍使用各自正常的渠道会话。

## 安全清单

把 Bot 暴露到群聊或公开聊天前：

- 让文件和 Shell 操作保持审批。
- 平台支持时，将渠道限制到可信用户、群或会话。
- 独立适配器使用强随机 AppServer WebSocket token。
- 平台需要回调 DotCraft 时，生产部署使用 HTTPS。
- 需要更强工具隔离时，使用 [OpenSandbox](../self-hosted/security#沙箱-opensandbox)。

完整清单和准确字段见 [安全与沙箱](../self-hosted/security) 与 [配置完整参考](../../developing/configuration#tools-security-与-sandbox)。

## 构建自定义渠道

当内置渠道覆盖你的平台时，优先使用内置渠道。需要接入新平台或自定义部署时：

- Channel Module：[Channel Module 集成](../../developing/integrations/typescript-module)
- 渠道适配器基类：[Channel adapters](../../developing/sdks/channels)
- Python SDK：[Python SDK](../../developing/sdks/python)
- Wire 协议：[AppServer Protocol](../../developing/protocols/appserver-protocol)

## 相关文档

- [渠道配置参考](../../developing/channels/reference)
- [Channel adapters](../../developing/sdks/channels)
- [安全与沙箱](../self-hosted/security)
- [服务器部署](../self-hosted/server-deployment)
