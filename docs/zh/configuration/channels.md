# 渠道配置

查看各渠道插件在工作区中的设置。

## 连接渠道

在**插件 → 渠道**安装并设置插件，然后点**连接**。

使用官方 Docker 镜像时，在 `.env` 中设置 `ENABLED_CHANNELS`，例如 `qq,feishu`。
平台凭据填在 `.env`，白名单填在对应的工作区文件。

## 官方渠道插件

| 渠道 | 插件 ID | 适配器配置 |
|---|---|---|
| **[QQ](../guide/channels/qq)** | `channel-qq` | `.craft/qq.json` |
| **[企业微信 / WeCom](../guide/channels/wecom)** | `channel-wecom` | `.craft/wecom.json` |
| **[飞书 / Lark](../guide/channels/feishu)** | `channel-feishu` | `.craft/feishu.json` |
| **[Telegram](../guide/channels/telegram)** | `channel-telegram` | `.craft/telegram.json` |
| **[微信 / Weixin](../guide/channels/weixin)** | `channel-weixin` | `.craft/weixin.json` |

## 适配器配置文件

通过 `adminUsers` 授予 `/model`、`/automate` 等管理员命令权限。QQ 和企业微信通过白名单放行普通用户。微信机器人的所有者可执行管理员命令。

### QQ

```json
{
  "qq": {
    "host": "127.0.0.1",
    "port": 6700,
    "accessToken": "",
    "adminUsers": [123456789],
    "whitelistedUsers": [],
    "whitelistedGroups": [],
    "approvalTimeoutMs": 60000,
    "requireMentionInGroups": true,
    "ackReactionEmoji": "128064"
  }
}
```

| 字段 | 说明 | 默认值 |
|---|---|---|
| `qq.host` | OneBot 反向 WebSocket 监听 host。 | `127.0.0.1` |
| `qq.port` | OneBot 反向 WebSocket 监听端口。 | `6700` |
| `qq.accessToken` | 可选 OneBot access token。设置后必须与 NapCat 一致。 | 空 |
| `qq.adminUsers` | 可以执行管理员命令的 QQ 用户 ID。管理员总能与 DotCraft 对话。 | `[]` |
| `qq.whitelistedUsers` | 允许与 DotCraft 对话的 QQ 用户 ID。 | `[]` |
| `qq.whitelistedGroups` | 允许群成员与 DotCraft 对话的 QQ 群 ID。 | `[]` |
| `qq.approvalTimeoutMs` | 审批超时时间，单位毫秒。 | `60000` |
| `qq.requireMentionInGroups` | QQ 群聊是否需要 @ 机器人。 | `true` |
| `qq.ackReactionEmoji` | 标记已处理消息的 QQ 表情 ID，例如 `128064`（👀）或 `128076`（👌）。 | `128064` |

### 企业微信 / WeCom

```json
{
  "wecom": {
    "host": "0.0.0.0",
    "port": 9000,
    "scheme": "http",
    "robots": [
      {
        "path": "/dotcraft",
        "token": "wecom-token",
        "aesKey": "wecom-encoding-aes-key"
      }
    ],
    "adminUsers": ["zhangsan"],
    "whitelistedUsers": [],
    "whitelistedChats": [],
    "approvalTimeoutMs": 60000
  }
}
```

| 字段 | 说明 | 默认值 |
|---|---|---|
| `wecom.host` | 回调服务监听 host。 | `0.0.0.0` |
| `wecom.port` | 回调服务监听端口。 | `9000` |
| `wecom.scheme` | 回调服务协议。只有配置 TLS 证书和 key 路径时才直接使用 `https`。 | `http` |
| `wecom.tls.certPath` | 直接提供 HTTPS 回调服务时使用的 TLS 证书路径。 | 空 |
| `wecom.tls.keyPath` | 直接提供 HTTPS 回调服务时使用的 TLS 私钥路径。 | 空 |
| `wecom.robots` | 机器人回调凭据：`path`、`token` 和 `aesKey`。 | `[]` |
| `wecom.defaultRobot` | 某个机器人条目省略 token 或 AES key 时使用的默认值。 | 空 |
| `wecom.adminUsers` | 可以执行管理员命令的企业微信 UserId。管理员总能与 DotCraft 对话。 | `[]` |
| `wecom.whitelistedUsers` | 允许与 DotCraft 对话的企业微信 UserId。 | `[]` |
| `wecom.whitelistedChats` | 允许与 DotCraft 对话的企业微信 ChatId。 | `[]` |
| `wecom.approvalTimeoutMs` | 审批超时时间，单位毫秒。 | `60000` |

### 飞书 / Lark

```json
{
  "feishu": {
    "appId": "cli_your_app_id",
    "appSecret": "your_app_secret",
    "brand": "feishu",
    "cardTitle": "DotCraft",
    "approvalTimeoutMs": 120000,
    "groupMentionRequired": true,
    "ackReactionEmoji": "GLANCE",
    "adminUsers": ["ou_your_open_id"],
    "cli": {
      "enabled": false
    }
  }
}
```

| 字段 | 说明 | 默认值 |
|---|---|---|
| `feishu.appId` | 飞书 / Lark 应用 ID。 | 必填 |
| `feishu.appSecret` | 飞书 / Lark 应用 Secret。 | 必填 |
| `feishu.verificationToken` | 需要校验事件 payload 时使用的可选 verification token。 | 空 |
| `feishu.encryptKey` | 加密事件 payload 使用的可选 encrypt key。 | 空 |
| `feishu.brand` | 服务环境：`feishu` 或 `lark`。 | `feishu` |
| `feishu.cardTitle` | 消息、状态和审批卡片上显示的标题。 | `DotCraft` |
| `feishu.approvalTimeoutMs` | 审批超时时间，单位毫秒。 | `120000` |
| `feishu.groupMentionRequired` | 飞书群聊是否需要 @ 机器人。 | `true` |
| `feishu.ackReactionEmoji` | 标记已处理消息的 emoji 类型。 | `GLANCE` |
| `feishu.adminUsers` | 可以执行管理员命令的飞书用户 open_id。 | `[]` |
| `feishu.cli.enabled` | 是否在此渠道发起的对话中提供官方飞书 CLI。 | `false` |
| `feishu.cli.userScopes` | 授权账号只读访问个人资源时请求的飞书 scope。留空则所有命令都以机器人身份运行。 | 空 |

启用 `feishu.cli.enabled` 后，可在飞书对话中使用 `FeishuCli`，每次调用都需要审批。
工具默认使用机器人身份。要只读访问个人资源，设置 `feishu.cli.userScopes` 并[授权飞书账号](../guide/channels/feishu#个人资源访问)。
CLI 的文件参数须指向工作区内的文件。

### Telegram

```json
{
  "telegram": {
    "botToken": "123456:telegram-bot-token",
    "adminUsers": [123456789],
    "httpsProxy": "",
    "approvalTimeoutMs": 120000,
    "pollTimeoutMs": 30000
  }
}
```

| 字段 | 说明 | 默认值 |
|---|---|---|
| `telegram.botToken` | BotFather 签发的 bot token。 | 必填 |
| `telegram.adminUsers` | 可以执行管理员命令的 Telegram 数字用户 ID。这些用户也会在 Bot 命令菜单里看到管理员命令。 | `[]` |
| `telegram.httpsProxy` | Telegram Bot API 请求使用的可选 HTTPS proxy。 | 空 |
| `telegram.approvalTimeoutMs` | 审批超时时间，单位毫秒。 | `120000` |
| `telegram.pollTimeoutMs` | Telegram long-poll 超时时间，单位毫秒。 | `30000` |

### 微信 / Weixin

```json
{
  "weixin": {
    "apiBaseUrl": "https://ilinkai.weixin.qq.com",
    "pollIntervalMs": 3000,
    "pollTimeoutMs": 30000,
    "approvalTimeoutMs": 120000,
    "botType": "3"
  }
}
```

| 字段 | 说明 | 默认值 |
|---|---|---|
| `weixin.apiBaseUrl` | 腾讯 iLink API 基础地址。 | `https://ilinkai.weixin.qq.com` |
| `weixin.pollIntervalMs` | 轮询周期之间的等待时间，单位毫秒。 | `3000` |
| `weixin.pollTimeoutMs` | long-poll 超时时间，单位毫秒。 | `30000` |
| `weixin.approvalTimeoutMs` | 审批超时时间，单位毫秒。 | `120000` |
| `weixin.botType` | 传给二维码登录 API 的 Bot 类型值。 | `3` |

## 相关文档

- [渠道概览](../guide/channels/)——渠道能做什么，以及如何接入一个渠道。
