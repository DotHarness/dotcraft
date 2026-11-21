# Channel configuration

Find the settings for each channel plugin in your workspace.

## Connect a channel

In **Plugins → Channels**, install and set up the plugin, then select **Connect**.

With the official Docker image, set `ENABLED_CHANNELS` in `.env` (for example, `qq,feishu`). Set credentials in
`.env` and allowlists in the channel's workspace file.

## Official channel plugins

| Channel | Plugin ID | Adapter config |
|---|---|---|
| **[QQ](../guide/channels/qq)** | `channel-qq` | `.craft/qq.json` |
| **[WeCom](../guide/channels/wecom)** | `channel-wecom` | `.craft/wecom.json` |
| **[Feishu / Lark](../guide/channels/feishu)** | `channel-feishu` | `.craft/feishu.json` |
| **[Telegram](../guide/channels/telegram)** | `channel-telegram` | `.craft/telegram.json` |
| **[Weixin](../guide/channels/weixin)** | `channel-weixin` | `.craft/weixin.json` |

## Adapter config files

Set `adminUsers` to grant access to commands such as `/model` and `/automate`. QQ and WeCom
also use allowlists to admit regular users. The Weixin bot owner can run admin commands.

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

| Field | Description | Default |
|---|---|---|
| `qq.host` | OneBot reverse WebSocket listen host. | `127.0.0.1` |
| `qq.port` | OneBot reverse WebSocket listen port. | `6700` |
| `qq.accessToken` | Optional OneBot access token. Must match NapCat when set. | Empty |
| `qq.adminUsers` | QQ user IDs that can run admin commands. Admins can always chat with DotCraft. | `[]` |
| `qq.whitelistedUsers` | QQ user IDs allowed to chat with DotCraft. | `[]` |
| `qq.whitelistedGroups` | QQ group IDs whose members may chat with DotCraft. | `[]` |
| `qq.approvalTimeoutMs` | Approval timeout in milliseconds. | `60000` |
| `qq.requireMentionInGroups` | Require @mention in QQ groups. | `true` |
| `qq.ackReactionEmoji` | QQ emoji ID used to acknowledge handled messages, such as `128064` (👀) or `128076` (👌). | `128064` |

### WeCom

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

| Field | Description | Default |
|---|---|---|
| `wecom.host` | Callback server listen host. | `0.0.0.0` |
| `wecom.port` | Callback server listen port. | `9000` |
| `wecom.scheme` | Callback server scheme. For `https`, set the TLS certificate and key paths. | `http` |
| `wecom.tls.certPath` | TLS certificate path for direct HTTPS callback serving. | Empty |
| `wecom.tls.keyPath` | TLS private key path for direct HTTPS callback serving. | Empty |
| `wecom.robots` | Robot callback credentials: `path`, `token`, and `aesKey`. | `[]` |
| `wecom.defaultRobot` | Default Token and EncodingAESKey used when a robot-specific entry omits them. | Empty |
| `wecom.adminUsers` | WeCom UserIds that can run admin commands. Admins can always chat with DotCraft. | `[]` |
| `wecom.whitelistedUsers` | WeCom UserIds allowed to chat with DotCraft. | `[]` |
| `wecom.whitelistedChats` | WeCom ChatIds allowed to chat with DotCraft. | `[]` |
| `wecom.approvalTimeoutMs` | Approval timeout in milliseconds. | `60000` |

### Feishu

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

| Field | Description | Default |
|---|---|---|
| `feishu.appId` | Feishu / Lark app ID. | Required |
| `feishu.appSecret` | Feishu / Lark app secret. | Required |
| `feishu.verificationToken` | Optional verification token for event payloads that require it. | Empty |
| `feishu.encryptKey` | Optional encrypt key for encrypted event payloads. | Empty |
| `feishu.brand` | Service environment: `feishu` or `lark`. | `feishu` |
| `feishu.cardTitle` | Title shown on message, status, and approval cards. | `DotCraft` |
| `feishu.approvalTimeoutMs` | Approval timeout in milliseconds. | `120000` |
| `feishu.groupMentionRequired` | Require @mention in Feishu groups. | `true` |
| `feishu.ackReactionEmoji` | Emoji type used to acknowledge handled messages. | `GLANCE` |
| `feishu.adminUsers` | Feishu user open_ids that can run admin commands. | `[]` |
| `feishu.cli.enabled` | Offer the official Feishu CLI in chats from this channel. | `false` |
| `feishu.cli.userScopes` | Feishu user scopes requested when authorizing an account for read-only personal access. Empty keeps every command on the bot identity. | Empty |

Enable `feishu.cli.enabled` to use `FeishuCli` in Feishu chats. Each call requires approval.
The tool uses the bot account by default. For read-only personal access, set
`feishu.cli.userScopes` and [authorize a Feishu account](../guide/channels/feishu#personal-access).
Keep CLI file arguments inside the workspace.

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

| Field | Description | Default |
|---|---|---|
| `telegram.botToken` | Bot token issued by BotFather. | Required |
| `telegram.adminUsers` | Numeric Telegram user IDs that can run admin commands. They also see admin commands in the bot's command menu. | `[]` |
| `telegram.httpsProxy` | Optional HTTPS proxy for Telegram Bot API requests. | Empty |
| `telegram.approvalTimeoutMs` | Approval timeout in milliseconds. | `120000` |
| `telegram.pollTimeoutMs` | Telegram long-poll timeout in milliseconds. | `30000` |

### Weixin

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

| Field | Description | Default |
|---|---|---|
| `weixin.apiBaseUrl` | Tencent iLink API base URL. | `https://ilinkai.weixin.qq.com` |
| `weixin.pollIntervalMs` | Delay between polling cycles in milliseconds. | `3000` |
| `weixin.pollTimeoutMs` | Long-poll timeout in milliseconds. | `30000` |
| `weixin.approvalTimeoutMs` | Approval timeout in milliseconds. | `120000` |
| `weixin.botType` | Bot type value passed to the QR login API. | `3` |

## Related docs

- [Channels overview](../guide/channels/) — what channels do and how to connect one.
