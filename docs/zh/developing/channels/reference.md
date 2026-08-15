# 渠道配置参考

本文档记录 TypeScript 渠道在 Desktop 托管和独立部署中的注册方式，以及各适配器配置文件。

## 注册模式

### Desktop 托管的内置模块

当 DotCraft 负责托管内置模块进程时，使用下面的形态：

```json
{
  "ExternalChannels": {
    "qq": {
      "enabled": true,
      "transport": "managedWebsocket",
      "builtinModule": "channel-qq"
    }
  }
}
```

Desktop 会为内置模块写入该注册信息，并在运行时把 AppServer 端点注入适配器配置。

### 独立适配器

当你自己运行适配器进程时，使用下面的形态：

```json
{
  "AppServer": {
    "Mode": "WebSocket",
    "WebSocket": {
      "Host": "127.0.0.1",
      "Port": 9100,
      "Token": ""
    }
  },
  "ExternalChannels": {
    "qq": {
      "enabled": true,
      "transport": "websocket"
    }
  }
}
```

独立适配器会从自身适配器配置文件读取 `dotcraft.wsUrl` 和 `dotcraft.token`。

## 内置模块名称

| 渠道 | 内置模块 | 适配器配置 |
|---|---|---|
| **QQ** | `channel-qq` | `.craft/qq.json` |
| **企业微信 / WeCom** | `channel-wecom` | `.craft/wecom.json` |
| **飞书 / Lark** | `channel-feishu` | `.craft/feishu.json` |
| **Telegram** | `channel-telegram` | `.craft/telegram.json` |
| **微信 / Weixin** | `channel-weixin` | `.craft/weixin.json` |

## 适配器配置文件

每个 TypeScript 渠道配置都有一个 `dotcraft` 段和一个平台段。Desktop UI 会隐藏 `dotcraft.*` 字段，并在托管模块运行时自动提供这些值。

### QQ

```json
{
  "dotcraft": {
    "wsUrl": "ws://127.0.0.1:9100/ws",
    "token": ""
  },
  "qq": {
    "host": "127.0.0.1",
    "port": 6700,
    "accessToken": "",
    "adminUsers": [123456789],
    "whitelistedUsers": [],
    "whitelistedGroups": [],
    "approvalTimeoutMs": 60000,
    "requireMentionInGroups": true
  }
}
```

| 字段 | 说明 | 默认值 |
|---|---|---|
| `dotcraft.wsUrl` | 独立适配器使用的 AppServer WebSocket 端点。Desktop 托管运行时会覆盖该值。 | `ws://127.0.0.1:9100/ws` |
| `dotcraft.token` | 独立适配器使用的 AppServer WebSocket token。Desktop 托管运行时会覆盖该值。 | 空 |
| `qq.host` | OneBot 反向 WebSocket 监听 host。 | `127.0.0.1` |
| `qq.port` | OneBot 反向 WebSocket 监听端口。 | `6700` |
| `qq.accessToken` | 可选 OneBot access token。设置后必须与 NapCat 一致。 | 空 |
| `qq.adminUsers` | 拥有管理员权限的 QQ 用户 ID。 | `[]` |
| `qq.whitelistedUsers` | 允许与 DotCraft 对话的 QQ 用户 ID。 | `[]` |
| `qq.whitelistedGroups` | 允许与 DotCraft 对话的 QQ 群 ID。 | `[]` |
| `qq.approvalTimeoutMs` | 审批超时时间，单位毫秒。 | `60000` |
| `qq.requireMentionInGroups` | QQ 群聊是否需要 @ 机器人。 | `true` |

### 企业微信 / WeCom

```json
{
  "dotcraft": {
    "wsUrl": "ws://127.0.0.1:9100/ws",
    "token": ""
  },
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
| `dotcraft.wsUrl` | 独立适配器使用的 AppServer WebSocket 端点。Desktop 托管运行时会覆盖该值。 | `ws://127.0.0.1:9100/ws` |
| `dotcraft.token` | 独立适配器使用的 AppServer WebSocket token。Desktop 托管运行时会覆盖该值。 | 空 |
| `wecom.host` | 回调服务监听 host。 | `0.0.0.0` |
| `wecom.port` | 回调服务监听端口。 | `9000` |
| `wecom.scheme` | 回调服务协议。只有配置 TLS 证书和 key 路径时才直接使用 `https`。 | `http` |
| `wecom.tls.certPath` | 直接提供 HTTPS 回调服务时使用的 TLS 证书路径。 | 空 |
| `wecom.tls.keyPath` | 直接提供 HTTPS 回调服务时使用的 TLS 私钥路径。 | 空 |
| `wecom.robots` | 机器人回调凭据：`path`、`token` 和 `aesKey`。 | `[]` |
| `wecom.defaultRobot` | 某个机器人条目省略 token 或 AES key 时使用的默认值。 | 空 |
| `wecom.adminUsers` | 拥有管理员权限的企业微信 UserId。 | `[]` |
| `wecom.whitelistedUsers` | 允许与 DotCraft 对话的企业微信 UserId。 | `[]` |
| `wecom.whitelistedChats` | 允许与 DotCraft 对话的企业微信 ChatId。 | `[]` |
| `wecom.approvalTimeoutMs` | 审批超时时间，单位毫秒。 | `60000` |

### 飞书 / Lark

```json
{
  "dotcraft": {
    "wsUrl": "ws://127.0.0.1:9100/ws",
    "token": ""
  },
  "feishu": {
    "appId": "cli_your_app_id",
    "appSecret": "your_app_secret",
    "brand": "feishu",
    "cardTitle": "DotCraft",
    "approvalTimeoutMs": 120000,
    "groupMentionRequired": true,
    "ackReactionEmoji": "GLANCE",
    "streaming": {
      "enabled": true
    },
    "cli": {
      "enabled": false
    }
  }
}
```

| 字段 | 说明 | 默认值 |
|---|---|---|
| `dotcraft.wsUrl` | 独立适配器使用的 AppServer WebSocket 端点。Desktop 托管运行时会覆盖该值。 | `ws://127.0.0.1:9100/ws` |
| `dotcraft.token` | 独立适配器使用的 AppServer WebSocket token。Desktop 托管运行时会覆盖该值。 | 空 |
| `feishu.appId` | 飞书 / Lark 应用 ID。 | 必填 |
| `feishu.appSecret` | 飞书 / Lark 应用 Secret。 | 必填 |
| `feishu.verificationToken` | 需要校验事件 payload 时使用的可选 verification token。 | 空 |
| `feishu.encryptKey` | 加密事件 payload 使用的可选 encrypt key。 | 空 |
| `feishu.brand` | 服务环境：`feishu` 或 `lark`。 | `feishu` |
| `feishu.cardTitle` | 回复、进度和审批卡片上显示的标题。 | `DotCraft` |
| `feishu.approvalTimeoutMs` | 审批超时时间，单位毫秒。 | `120000` |
| `feishu.groupMentionRequired` | 飞书群聊是否需要 @ 机器人。 | `true` |
| `feishu.ackReactionEmoji` | 标记已处理消息的 emoji 类型。 | `GLANCE` |
| `feishu.downloadDir` | 下载附件使用的本地目录。 | workspace 临时目录 |
| `feishu.streaming.enabled` | 使用原生 CardKit 流式卡片，能力不可用时自动回退为普通卡片。需要 `cardkit:card:write`。 | `true` |
| `feishu.cli.enabled` | 是否向此 Channel 创建的 Thread 提供内置官方飞书 CLI。 | `false` |
| `feishu.debug.adapterStream` | 是否启用 adapter stream 调试日志。 | `false` |
| `feishu.debug.textMerge` | 是否启用文本合并调试日志。 | `false` |

启用 `feishu.cli.enabled` 后，飞书来源的 Thread 会获得 `FeishuCli` 工具，每次调用都需要审批。该工具使用当前应用凭据和 Bot 身份运行内置 CLI，不使用个人飞书授权。

调用业务命令前，先使用 CLI 内置的官方 Skill：依次调用 `skills list` 和对应 Skill 的 `skills read`。CLI 的文件参数必须位于 workspace 内。系统会拒绝 raw `api`、身份或配置命令、调用方传入的 `--yes`、运行时安装和自更新。

### Telegram

```json
{
  "dotcraft": {
    "wsUrl": "ws://127.0.0.1:9100/ws",
    "token": ""
  },
  "telegram": {
    "botToken": "123456:telegram-bot-token",
    "httpsProxy": "",
    "approvalTimeoutMs": 120000,
    "pollTimeoutMs": 30000
  }
}
```

| 字段 | 说明 | 默认值 |
|---|---|---|
| `dotcraft.wsUrl` | 独立适配器使用的 AppServer WebSocket 端点。Desktop 托管运行时会覆盖该值。 | `ws://127.0.0.1:9100/ws` |
| `dotcraft.token` | 独立适配器使用的 AppServer WebSocket token。Desktop 托管运行时会覆盖该值。 | 空 |
| `telegram.botToken` | BotFather 签发的 bot token。 | 必填 |
| `telegram.httpsProxy` | Telegram Bot API 请求使用的可选 HTTPS proxy。 | 空 |
| `telegram.approvalTimeoutMs` | 审批超时时间，单位毫秒。 | `120000` |
| `telegram.pollTimeoutMs` | Telegram long-poll 超时时间，单位毫秒。 | `30000` |

### 微信 / Weixin

```json
{
  "dotcraft": {
    "wsUrl": "ws://127.0.0.1:9100/ws",
    "token": ""
  },
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
| `dotcraft.wsUrl` | 独立适配器使用的 AppServer WebSocket 端点。Desktop 托管运行时会覆盖该值。 | `ws://127.0.0.1:9100/ws` |
| `dotcraft.token` | 独立适配器使用的 AppServer WebSocket token。Desktop 托管运行时会覆盖该值。 | 空 |
| `weixin.apiBaseUrl` | 腾讯 iLink API 基础地址。 | `https://ilinkai.weixin.qq.com` |
| `weixin.pollIntervalMs` | 轮询周期之间的等待时间，单位毫秒。 | `3000` |
| `weixin.pollTimeoutMs` | long-poll 超时时间，单位毫秒。 | `30000` |
| `weixin.approvalTimeoutMs` | 审批超时时间，单位毫秒。 | `120000` |
| `weixin.botType` | 传给二维码登录 API 的 Bot 类型值。 | `3` |

## 相关文档

- [Channels 与 Bots](../../features/entry-points/channels)
- [配置完整参考](../configuration)
- [Channel adapters](../sdks/channels)
- [Channel Module 集成](../integrations/typescript-module)
