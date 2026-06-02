# DotCraft QQ Channel Adapter

`@dotcraft/channel-qq` connects QQ to DotCraft through the WebSocket external channel protocol. The adapter connects to DotCraft AppServer and exposes a local OneBot v11 reverse WebSocket endpoint for NapCat or another OneBot implementation.

## Feature Summary

- Connects to DotCraft AppServer over WebSocket
- Exposes a OneBot v11 reverse WebSocket server
- Supports QQ private chats and group chats
- Responds in groups only when the bot is mentioned by default
- Maps each QQ private chat user or group chat to a separate DotCraft thread
- Supports approval inside the QQ conversation
- Provides voice, video, and file runtime tools

> [!CAUTION]
> Third-party QQ protocol frameworks may create account risk. Evaluate that risk before using them.

## Prerequisites

| Requirement | Notes |
|-------------|-------|
| QQ account | Used as the bot account; a secondary account is recommended |
| OneBot v11 implementation | NapCat is recommended for QQ login and reverse WebSocket connection |
| DotCraft AppServer | WebSocket mode must be enabled |
| QQ channel package | In releases: `resources/modules/channel-qq`; in development: `sdk/typescript/packages/channel-qq` |

## 1) Workspace Config (`.craft/config.json`)

Enable AppServer WebSocket and register the QQ external channel in `.craft/config.json`:

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

## 2) Adapter Config (`.craft/qq.json`)

Create `.craft/qq.json` in the target workspace:

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

Field reference:

| Field | Description | Default |
|-------|-------------|---------|
| `dotcraft.wsUrl` | DotCraft AppServer WebSocket URL | `ws://127.0.0.1:9100/ws` |
| `dotcraft.token` | AppServer WebSocket token | Empty |
| `qq.host` | OneBot reverse WebSocket listen host | `127.0.0.1` |
| `qq.port` | OneBot reverse WebSocket listen port | `6700` |
| `qq.accessToken` | OneBot access token; must match NapCat | Empty |
| `qq.adminUsers` | QQ user IDs with admin permission | `[]` |
| `qq.whitelistedUsers` | QQ user IDs allowed to chat with DotCraft | `[]` |
| `qq.whitelistedGroups` | QQ group IDs allowed to chat with DotCraft | `[]` |
| `qq.approvalTimeoutMs` | Approval timeout in milliseconds | `60000` |
| `qq.requireMentionInGroups` | Whether group chats require mentioning the bot | `true` |

If `adminUsers`, `whitelistedUsers`, and `whitelistedGroups` are all empty, the adapter will not respond to any QQ user. Configure at least one admin user.

## 3) Configure NapCat

Create a WebSocket client (reverse WS) in the NapCat WebUI:

1. Set URL to `ws://127.0.0.1:6700/`.
2. Set Token to the same value as `qq.accessToken`.
3. Set message format to `array`.

If NapCat runs in Docker or on another machine, replace `127.0.0.1` with an address that can reach the QQ adapter.

## 4) Start

Desktop discovers the packaged `qq-standard` external channel and can start it from the channel management UI.

For development:

```bash
cd sdk/typescript
npm run build --workspace @dotcraft/channel-qq
npx dotcraft-channel-qq --workspace F:\examples\workspace
```

Or from the package directory:

```bash
cd sdk/typescript/packages/channel-qq
npm run build
npm start -- --workspace F:\examples\workspace
```

## Usage

- Group chats respond only when the bot is mentioned by default.
- Private chats use a separate thread per user.
- Each QQ group maps to one shared DotCraft thread, with each sender recorded on the individual turn.
- When an admin triggers an action that needs approval, the adapter sends an approval prompt in the QQ conversation.
- Reply `同意`, `允许`, `yes`, or `approve` to approve; reply `拒绝`, `no`, `reject`, or `deny` to reject.

## Runtime Tools

The QQ channel provides these tools for explicit cross-target delivery:

| Tool | Target | Source |
|------|--------|--------|
| `QQSendGroupVoice` | QQ group | Local path, HTTP URL, `base64://...` |
| `QQSendPrivateVoice` | QQ user | Local path, HTTP URL, `base64://...` |
| `QQSendGroupVideo` | QQ group | Local path, HTTP URL |
| `QQSendPrivateVideo` | QQ user | Local path, HTTP URL |
| `QQUploadGroupFile` | QQ group | Local absolute path |
| `QQUploadPrivateFile` | QQ user | Local absolute path |

These tools are exposed by the adapter through external channel capabilities. Tool names stay PascalCase.
