# DotCraft WeCom Channel Adapter

`@dotcraft/channel-wecom` connects WeCom to DotCraft through the WebSocket external channel protocol. The adapter connects to DotCraft AppServer and starts a local WeCom bot HTTP(S) callback service.

Personal Weixin `@dotcraft/channel-weixin` and WeCom `@dotcraft/channel-wecom` are separate channels.

## Feature Summary

- Connects to DotCraft AppServer over WebSocket
- Starts a WeCom HTTP(S) callback service
- Supports XML message push APIs and JSON smart bot callback format
- Supports text, images, speech-to-text, files, attachments, mixed image/text messages, and events
- Downloads image and mixed image/text message images as temporary local images for multimodal input
- Isolates conversations by `userId + chatId`
- Supports approval inside the WeCom conversation
- Provides voice and file runtime tools

## Prerequisites

| Requirement | Notes |
|-------------|-------|
| WeCom bot | Self-built app or smart bot with callback URL, Token, and EncodingAESKey |
| DotCraft AppServer | WebSocket mode must be enabled |
| WeCom channel package | In releases: `resources/modules/channel-wecom`; in development: `sdk/typescript/packages/channel-wecom` |
| Public callback URL | WeCom must reach the adapter callback URL; production deployments should usually expose HTTPS through a reverse proxy |

## 1) Workspace Config (`.craft/config.json`)

Enable AppServer WebSocket and register the WeCom external channel in `.craft/config.json`:

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
    "wecom": {
      "enabled": true,
      "transport": "websocket"
    }
  }
}
```

## 2) Adapter Config (`.craft/wecom.json`)

Create `.craft/wecom.json` in the target workspace:

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
    "adminUsers": ["zhangsan"],
    "whitelistedUsers": [],
    "whitelistedChats": [],
    "approvalTimeoutMs": 60000,
    "robots": [
      {
        "path": "/dotcraft",
        "token": "your_token_here",
        "aesKey": "your_43_char_aeskey"
      }
    ]
  }
}
```

Field reference:

| Field | Description | Default |
|-------|-------------|---------|
| `dotcraft.wsUrl` | DotCraft AppServer WebSocket URL | `ws://127.0.0.1:9100/ws` |
| `dotcraft.token` | AppServer WebSocket token | Empty |
| `wecom.host` | Callback service listen host | `0.0.0.0` |
| `wecom.port` | Callback service listen port | `9000` |
| `wecom.scheme` | Local callback protocol, `http` or `https` | `http` |
| `wecom.tls.certPath` / `keyPath` | Certificate and private key paths for `https` mode | Empty |
| `wecom.adminUsers` | WeCom UserIds with admin permission | `[]` |
| `wecom.whitelistedUsers` | WeCom UserIds allowed to chat with DotCraft | `[]` |
| `wecom.whitelistedChats` | ChatIds allowed to chat with DotCraft | `[]` |
| `wecom.approvalTimeoutMs` | Approval timeout in milliseconds | `60000` |
| `wecom.robots` | Bot callback credential list | `[]` |

Single robot config:

| Field | Required | Description |
|-------|----------|-------------|
| `path` | Yes | Callback path, such as `/dotcraft` |
| `token` | Yes | Token configured in the WeCom console |
| `aesKey` | Yes | EncodingAESKey, usually 43 characters without `=` |

## 3) WeCom Console Setup

Configure message receiving in the WeCom admin console:

- URL: `http://your-server:9000/dotcraft`
- Token: same as `robots[].token` in `.craft/wecom.json`
- EncodingAESKey: same as `robots[].aesKey` in `.craft/wecom.json`

If HTTPS is exposed through Nginx, Caddy, or another reverse proxy, the adapter can still listen on local HTTP.

## 4) Start

Desktop discovers the packaged `wecom-standard` external channel and can start it from the channel management UI.

For development:

```bash
cd sdk/typescript
npm run build --workspace @dotcraft/channel-wecom
npx dotcraft-channel-wecom --workspace F:\dotcraft
```

Or from the package directory:

```bash
cd sdk/typescript/packages/channel-wecom
npm run build
npm start -- --workspace F:\dotcraft
```

## Usage

- Files and attachments return diagnostic messages and are not submitted directly to the agent.
- Conversations are isolated by `userId + chatId`; `channelContext` is `chat:<ChatId>`.
- `/new`, `/help`, Heartbeat, Cron, and common delivery flows are supported.
- Approval keywords include `同意`, `同意全部`, `拒绝`, `yes`, `yes all`, and `no`.

## Runtime Tools

| Tool | Purpose | Notes |
|------|---------|-------|
| `WeComSendVoice(filePath)` | Send voice to the current WeCom chat | AMR only; local absolute path; uploads temporary media first |
| `WeComSendFile(filePath)` | Send a file to the current WeCom chat | Local absolute path; uploads temporary media first |

These tools are exposed by the adapter through external channel capabilities. Tool names stay PascalCase.

## Cron and Heartbeat

| `channel` | `to` | Delivery target | Prerequisite |
|-----------|------|-----------------|--------------|
| `"wecom"` | `"chat:<ChatId>"` or `"<ChatId>"` | WeCom conversation | That ChatId has received a message and the adapter has cached its webhook |

It is recommended to create Cron tasks from inside the WeCom conversation so the task automatically binds to the current ChatId. If the target chatId has no cached webhook yet, delivery fails with `No WeCom webhook is available for target ...`.
