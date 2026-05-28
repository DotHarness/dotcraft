# DotCraft Weixin Channel Adapter

`@dotcraft/channel-weixin` connects Tencent iLink (WeChat bot API) to DotCraft via the external channel protocol over WebSocket.

## Feature Summary

- WebSocket transport to DotCraft AppServer
- QR-based interactive setup for first login
- Session persistence under module-scoped state directory
- Streaming turn delivery and approval handling
- `/new` command support to start a fresh conversation
- Real file/image tools: `WeixinSendFileToCurrentChat`, `WeixinSendImageToCurrentChat`
- Structured `file` / `image` delivery with local path or base64 sources
- Weixin iLink does not expose a Markdown rendering entry, so regular replies and media captions are flattened to plain text

## Installation

```bash
cd sdk/typescript
npm install
npm run build:all
```

## 1) Workspace Config (`.craft/config.json`)

Merge `config.example.json` into workspace `.craft/config.json` to enable the `weixin` external channel:

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
    "weixin": {
      "enabled": true,
      "transport": "websocket"
    }
  }
}
```

## 2) Adapter Config (`.craft/weixin.json`)

Create `.craft/weixin.json` in your target workspace:

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

Field notes:

- `dotcraft.wsUrl`: DotCraft AppServer WebSocket endpoint
- `dotcraft.token`: optional AppServer token
- `weixin.apiBaseUrl`: iLink API base URL
- `weixin.pollIntervalMs`: optional interval between polling cycles
- `weixin.pollTimeoutMs`: optional long-poll timeout
- `weixin.approvalTimeoutMs`: optional approval timeout
- `weixin.botType`: optional bot type for QR login API

## 3) CLI Usage

Primary mode:

```bash
npx dotcraft-channel-weixin --workspace /path/to/workspace
```

Optional config override:

```bash
npx dotcraft-channel-weixin --workspace /path/to/workspace --config /custom/weixin.json
```

## 4) Interactive Setup (QR Login)

- First run with no saved session transitions to `authRequired`
- In terminal mode, QR is rendered in the terminal
- After scan confirmation, adapter transitions to `ready`
- When session expires, lifecycle transitions `authExpired -> authRequired`, then QR login is required again

## 5) State and Temp Layout

The module stores data under workspace `.craft/`:

- Persistent state: `.craft/state/weixin-standard/`
  - credentials, sync cursor, context tokens
- Temporary artifacts: `.craft/tmp/weixin-standard/`
  - QR URL and QR image artifacts

## 6) Host Integration

Hosts should import module contract exports and observe lifecycle:

```typescript
import { manifest, createModule } from "@dotcraft/channel-weixin";

const instance = createModule({
  workspaceRoot: "/path/to/workspace",
  craftPath: "/path/to/workspace/.craft",
  channelName: "weixin",
  moduleId: "weixin-standard",
});

instance.onStatusChange((status, error) => {
  // status: configMissing | configInvalid | starting | authRequired | authExpired | ready | stopped
});

await instance.start();
```

## Development Notes

- Build all TypeScript packages:
  - `cd sdk/typescript && npm run build:all`
- Run all tests:
  - `cd sdk/typescript && npm run test:all`
- Run this package tests only:
  - `npm run test --workspace @dotcraft/channel-weixin`
- Dry-run package contents:
  - `cd sdk/typescript/packages/channel-weixin && npm pack --dry-run`

## Credits

[@tencent-weixin/openclaw-weixin](https://www.npmjs.com/package/@tencent-weixin/openclaw-weixin)
