# 社交渠道

把 DotCraft 接进团队已经在用的聊天工具，同事在群里问一句就能拿到答案，不必打开 Desktop。QQ、企业微信、飞书 / Lark、Telegram 和微信都可以接，聊出来的会话和记忆与工作区的其他入口完全共用。

![DotCraft Desktop 中可用的渠道](https://github.com/DotHarness/resources/raw/master/dotcraft/channels/catalog-light.png)

## 接入渠道

1. 在 DotCraft Desktop 打开工作区，进入**渠道**。
2. 选择要接入的平台，按表单填写该平台的凭据。
3. 到平台后台或 Bot 工具里完成对应设置。
4. 启用渠道，给 Bot 发一条测试消息。

渠道进程由 Desktop 托管，不用另外部署。每个平台需要哪些凭据，见下表对应的设置页。所有渠道共用的配置项见[渠道配置参考](./reference)。

## 内置渠道

| 平台 | 接入方式 | 主要能力 | 设置 |
|---|---|---|---|
| **QQ** | NapCat 或 OneBot v11 反向 WebSocket | 私聊、群聊、审批关键词、媒体投递 | [QQ 设置](./qq) |
| **企业微信 / WeCom** | 群机器人回调 URL、Token、EncodingAESKey | 企业微信群聊、审批、文件和图片投递 | [企业微信设置](./wecom) |
| **飞书 / Lark** | 启用 Bot 和 WebSocket 事件订阅的自建应用 | 卡片回复、审批、reaction、可选官方 CLI | [飞书设置](./feishu) |
| **Telegram** | BotFather token | 私聊、群聊、`/new`、`/help`、inline 审批 | [Telegram 设置](./telegram) |
| **微信 / Weixin** | 腾讯 iLink 二维码登录 | 微信聊天、保存登录状态、纯文本回复、文件和图片投递 | [微信设置](./weixin) |

## 渠道会话如何工作

![DotCraft 渠道适配器拓扑](/channel-adapter-topology.svg)

- 发给 Bot 的消息都在同一个会话里，回复送回同一个聊天。
- 平台支持时，审批和补充提问会直接出现在聊天里。
- 支持斜杠命令的渠道里，`/new` 开一段新会话。
- Desktop 打开同一个工作区，就能查看这些会话的历史或接着聊。

底层模型见[统一会话核心](../../developing/architecture/session-core)。

## 把 Desktop 对话接到聊天里

![DotCraft 社交渠道接续](https://github.com/DotHarness/resources/raw/master/dotcraft/whats-new/channel-handoff.gif)

在 Desktop 对话的**应用**菜单里，可以把当前这条对话绑定到已连接的渠道。DotCraft 会给出一条 `/bind 123456` 命令，在目标聊天里发送它，就能在那边接着聊同一条对话。

绑定只对那个聊天生效，其他聊天照旧各聊各的。

## 开放到群聊之前

把 Bot 放进群聊或公开聊天之前：

- 文件和 Shell 操作保持需要审批。
- 平台支持时，把渠道限制到可信的用户、群或聊天。
- 自己运行适配器时，给 AppServer WebSocket 设一个强随机 token。
- 平台需要回调 DotCraft 时，生产部署走 HTTPS。
- 需要更强的工具隔离时，开启 [OpenSandbox](../self-hosted/security#沙箱-opensandbox)。

对应的准确字段名见[配置完整参考](../../developing/configuration#tools-security-与-sandbox)。

## 接入自己的平台

内置渠道覆盖不了你要接的平台时，可以自己写一个适配器。[渠道适配器](../../developing/sdks/channels)讲基类和消息流转，[渠道模块集成](../../developing/integrations/typescript-module)讲怎么把写好的模块挂进 DotCraft。底层的消息格式见 [AppServer 协议](../../developing/protocols/appserver-protocol)。

## 相关文档

- [安全与沙箱](../self-hosted/security) — Bot 面向群聊之前，先把工具权限和沙箱收紧
- [服务器部署](../self-hosted/server-deployment) — 让渠道常驻服务器，不依赖你的电脑开着
