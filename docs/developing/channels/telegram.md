# DotCraft Telegram Channel Adapter

`@dotcraft/channel-telegram` connects a Telegram bot to DotCraft via the external channel adapter protocol over WebSocket.

## Feature Summary

- WebSocket transport to DotCraft AppServer
- Telegram long polling via grammY
- Streaming turn delivery and approval handling
- `/new` and `/help` command support, plus server-declared command registration
- Structured delivery for documents and voice messages
- Telegram channel tools:
  - `TelegramSendDocumentToCurrentChat`
  - `TelegramSendVoiceToCurrentChat`

## Installation

```bash
cd sdk/typescript
npm install
npm run build:all
```

## 1) Workspace Config (`.craft/config.json`)

Merge this into workspace `.craft/config.json` to enable the `telegram` external channel:

```json
{
  "AppServer": {
    "Mode": "stdioAndWebSocket",
    "WebSocket": {
      "Host": "127.0.0.1",
      "Port": 9100,
      "Token": ""
    }
  },
  "ExternalChannels": {
    "telegram": {
      "enabled": true,
      "transport": "websocket"
    }
  }
}
```

## 2) Adapter Config (`.craft/telegram.json`)

Create `.craft/telegram.json` in your target workspace:

```json
{
  "dotcraft": {
    "wsUrl": "ws://127.0.0.1:9100/ws",
    "token": ""
  },
  "telegram": {
    "botToken": "123456789:AAExampleToken",
    "httpsProxy": "",
    "approvalTimeoutMs": 120000,
    "pollTimeoutMs": 30000
  }
}
```

Field notes:

- `dotcraft.wsUrl`: DotCraft AppServer WebSocket endpoint
- `dotcraft.token`: optional AppServer token
- `telegram.botToken`: Telegram bot token from BotFather
- `telegram.httpsProxy`: optional HTTPS proxy URL used by Telegram API calls
- `telegram.approvalTimeoutMs`: optional approval timeout
- `telegram.pollTimeoutMs`: optional long-poll timeout in milliseconds

## 3) CLI Usage

Primary mode:

```bash
npx dotcraft-channel-telegram --workspace /path/to/workspace
```

Optional config override:

```bash
npx dotcraft-channel-telegram --workspace /path/to/workspace --config /custom/telegram.json
```

## 4) Host Integration

Hosts should import module contract exports and observe lifecycle:

```typescript
import { manifest, createModule } from "@dotcraft/channel-telegram";

const instance = createModule({
  workspaceRoot: "/path/to/workspace",
  craftPath: "/path/to/workspace/.craft",
  channelName: "telegram",
  moduleId: "telegram-standard",
});

instance.onStatusChange((status, error) => {
  // status: configMissing | configInvalid | starting | ready | stopped
});

await instance.start();
```

## 5) Development Notes

- Build all TypeScript packages:
  - `cd sdk/typescript && npm run build:all`
- Run all tests:
  - `cd sdk/typescript && npm run test:all`
- Run this package tests only:
  - `npm run test --workspace @dotcraft/channel-telegram`
- Dry-run package contents:
  - `cd sdk/typescript/packages/channel-telegram && npm pack --dry-run`
