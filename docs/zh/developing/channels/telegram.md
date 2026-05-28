# DotCraft Telegram 渠道适配器

`@dotcraft/channel-telegram` 通过 WebSocket 外部渠道协议，将 Telegram Bot 接入 DotCraft。

## 功能概览

- 通过 WebSocket 连接 DotCraft AppServer
- 基于 grammY 的 Telegram 长轮询
- 支持流式回复与审批交互
- 支持 `/new`、`/help`，并动态注册服务端声明的命令
- 支持文档与语音消息的结构化发送
- 提供 Telegram 渠道工具：
  - `TelegramSendDocumentToCurrentChat`
  - `TelegramSendVoiceToCurrentChat`

## 安装

```bash
cd sdk/typescript
npm install
npm run build:all
```

## 1）工作区配置（`.craft/config.json`）

将以下配置合并到工作区 `.craft/config.json`，启用 `telegram` 外部渠道：

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

## 2）适配器配置（`.craft/telegram.json`）

在目标工作区创建 `.craft/telegram.json`：

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

字段说明：

- `dotcraft.wsUrl`：DotCraft AppServer 的 WebSocket 地址
- `dotcraft.token`：可选的 AppServer 鉴权令牌
- `telegram.botToken`：从 BotFather 获取的 Telegram Bot Token
- `telegram.httpsProxy`：可选的 Telegram API HTTPS 代理地址
- `telegram.approvalTimeoutMs`：可选的审批超时时间
- `telegram.pollTimeoutMs`：可选的长轮询超时时间（毫秒）

## 3）CLI 用法

基础模式：

```bash
npx dotcraft-channel-telegram --workspace /path/to/workspace
```

可选配置覆盖：

```bash
npx dotcraft-channel-telegram --workspace /path/to/workspace --config /custom/telegram.json
```

## 4）宿主集成

宿主应导入模块契约导出并监听生命周期：

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

## 5）开发说明

- 构建全部 TypeScript 包：
  - `cd sdk/typescript && npm run build:all`
- 运行全部测试：
  - `cd sdk/typescript && npm run test:all`
- 仅运行当前包测试：
  - `npm run test --workspace @dotcraft/channel-telegram`
- 预览打包内容：
  - `cd sdk/typescript/packages/channel-telegram && npm pack --dry-run`
