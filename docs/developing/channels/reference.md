# Channel configuration reference

This page documents TypeScript channel registration and adapter config files for Desktop-managed and standalone deployments.

## Registration modes

### Desktop-managed built-in modules

Use this shape when DotCraft owns the bundled module process:

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

Desktop writes this registration for bundled modules and injects the AppServer endpoint into the adapter config at runtime.

### Standalone adapters

Use this shape when you run the adapter process yourself:

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

Standalone adapters read `dotcraft.wsUrl` and `dotcraft.token` from their adapter config file.

## Built-in module names

| Channel | Built-in module | Adapter config |
|---|---|---|
| **QQ** | `channel-qq` | `.craft/qq.json` |
| **WeCom** | `channel-wecom` | `.craft/wecom.json` |
| **Feishu / Lark** | `channel-feishu` | `.craft/feishu.json` |
| **Telegram** | `channel-telegram` | `.craft/telegram.json` |
| **Weixin** | `channel-weixin` | `.craft/weixin.json` |

## Adapter config files

Every TypeScript channel config has a `dotcraft` section and a platform section. Desktop hides `dotcraft.*` fields in the UI and supplies them automatically for managed modules.

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

| Field | Description | Default |
|---|---|---|
| `dotcraft.wsUrl` | AppServer WebSocket endpoint for standalone adapters. Desktop-managed runtime overrides this value. | `ws://127.0.0.1:9100/ws` |
| `dotcraft.token` | AppServer WebSocket token for standalone adapters. Desktop-managed runtime overrides this value. | Empty |
| `qq.host` | OneBot reverse WebSocket listen host. | `127.0.0.1` |
| `qq.port` | OneBot reverse WebSocket listen port. | `6700` |
| `qq.accessToken` | Optional OneBot access token. Must match NapCat when set. | Empty |
| `qq.adminUsers` | QQ user IDs with admin permission. | `[]` |
| `qq.whitelistedUsers` | QQ user IDs allowed to chat with DotCraft. | `[]` |
| `qq.whitelistedGroups` | QQ group IDs allowed to chat with DotCraft. | `[]` |
| `qq.approvalTimeoutMs` | Approval timeout in milliseconds. | `60000` |
| `qq.requireMentionInGroups` | Require @mention in QQ groups. | `true` |

### WeCom

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

| Field | Description | Default |
|---|---|---|
| `dotcraft.wsUrl` | AppServer WebSocket endpoint for standalone adapters. Desktop-managed runtime overrides this value. | `ws://127.0.0.1:9100/ws` |
| `dotcraft.token` | AppServer WebSocket token for standalone adapters. Desktop-managed runtime overrides this value. | Empty |
| `wecom.host` | Callback server listen host. | `0.0.0.0` |
| `wecom.port` | Callback server listen port. | `9000` |
| `wecom.scheme` | Callback server scheme. Use `https` only when TLS cert and key paths are configured. | `http` |
| `wecom.tls.certPath` | TLS certificate path for direct HTTPS callback serving. | Empty |
| `wecom.tls.keyPath` | TLS private key path for direct HTTPS callback serving. | Empty |
| `wecom.robots` | Robot callback credentials: `path`, `token`, and `aesKey`. | `[]` |
| `wecom.defaultRobot` | Default Token and EncodingAESKey used when a robot-specific entry omits them. | Empty |
| `wecom.adminUsers` | WeCom UserIds with admin permission. | `[]` |
| `wecom.whitelistedUsers` | WeCom UserIds allowed to chat with DotCraft. | `[]` |
| `wecom.whitelistedChats` | WeCom ChatIds allowed to chat with DotCraft. | `[]` |
| `wecom.approvalTimeoutMs` | Approval timeout in milliseconds. | `60000` |

### Feishu

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
    "tools": {
      "docs": {
        "enabled": false
      }
    }
  }
}
```

| Field | Description | Default |
|---|---|---|
| `dotcraft.wsUrl` | AppServer WebSocket endpoint for standalone adapters. Desktop-managed runtime overrides this value. | `ws://127.0.0.1:9100/ws` |
| `dotcraft.token` | AppServer WebSocket token for standalone adapters. Desktop-managed runtime overrides this value. | Empty |
| `feishu.appId` | Feishu / Lark app ID. | Required |
| `feishu.appSecret` | Feishu / Lark app secret. | Required |
| `feishu.verificationToken` | Optional verification token for event payloads that require it. | Empty |
| `feishu.encryptKey` | Optional encrypt key for encrypted event payloads. | Empty |
| `feishu.brand` | Service environment: `feishu` or `lark`. | `feishu` |
| `feishu.cardTitle` | Title shown on reply, progress, and approval cards. | `DotCraft` |
| `feishu.approvalTimeoutMs` | Approval timeout in milliseconds. | `120000` |
| `feishu.groupMentionRequired` | Require @mention in Feishu groups. | `true` |
| `feishu.ackReactionEmoji` | Emoji type used to acknowledge handled messages. | `GLANCE` |
| `feishu.downloadDir` | Local directory for downloaded attachments. | Workspace temp directory |
| `feishu.tools.docs.enabled` | Register Feishu docx and wiki tools. | `false` |
| `feishu.debug.adapterStream` | Enable adapter stream debug logs. | `false` |
| `feishu.debug.textMerge` | Enable text merge debug logs. | `false` |

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

| Field | Description | Default |
|---|---|---|
| `dotcraft.wsUrl` | AppServer WebSocket endpoint for standalone adapters. Desktop-managed runtime overrides this value. | `ws://127.0.0.1:9100/ws` |
| `dotcraft.token` | AppServer WebSocket token for standalone adapters. Desktop-managed runtime overrides this value. | Empty |
| `telegram.botToken` | Bot token issued by BotFather. | Required |
| `telegram.httpsProxy` | Optional HTTPS proxy for Telegram Bot API requests. | Empty |
| `telegram.approvalTimeoutMs` | Approval timeout in milliseconds. | `120000` |
| `telegram.pollTimeoutMs` | Telegram long-poll timeout in milliseconds. | `30000` |

### Weixin

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

| Field | Description | Default |
|---|---|---|
| `dotcraft.wsUrl` | AppServer WebSocket endpoint for standalone adapters. Desktop-managed runtime overrides this value. | `ws://127.0.0.1:9100/ws` |
| `dotcraft.token` | AppServer WebSocket token for standalone adapters. Desktop-managed runtime overrides this value. | Empty |
| `weixin.apiBaseUrl` | Tencent iLink API base URL. | `https://ilinkai.weixin.qq.com` |
| `weixin.pollIntervalMs` | Delay between polling cycles in milliseconds. | `3000` |
| `weixin.pollTimeoutMs` | Long-poll timeout in milliseconds. | `30000` |
| `weixin.approvalTimeoutMs` | Approval timeout in milliseconds. | `120000` |
| `weixin.botType` | Bot type value passed to the QR login API. | `3` |

## Related docs

- [Channels & Bots](../../features/entry-points/channels)
- [Configuration Reference](../configuration)
- [Channel adapters](../sdks/channels)
- [Channel Module integration](../integrations/typescript-module)
