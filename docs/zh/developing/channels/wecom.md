# DotCraft 企业微信渠道适配器

`@dotcraft/channel-wecom` 通过 WebSocket 外部渠道协议将企业微信接入 DotCraft。适配器连接 DotCraft AppServer，并在本地启动企业微信机器人 HTTP(S) 回调服务。

个人微信 `@dotcraft/channel-weixin` 与企业微信 `@dotcraft/channel-wecom` 是两个独立渠道。

## 功能概览

- 通过 WebSocket 连接 DotCraft AppServer
- 启动企业微信 HTTP(S) 回调服务
- 支持 XML 消息推送 API 和 JSON 智能机器人 API 回调格式
- 支持文本、图片、语音转文本、文件、附件、图文混排和事件消息
- 图片和图文混排图片会下载为临时本地图片并作为多模态输入提交
- 会话按 `userId + chatId` 隔离
- 支持企业微信会话内审批
- 提供语音和文件 Runtime 工具

## 前置条件

| 需求 | 说明 |
|------|------|
| 企业微信机器人 | 自建应用或智能机器人，需配置回调 URL、Token、EncodingAESKey |
| DotCraft AppServer | 需要启用 WebSocket 模式 |
| WeCom 渠道包 | 发布包中位于 `resources/modules/channel-wecom`，开发环境中位于 `sdk/typescript/packages/channel-wecom` |
| 公网回调地址 | 企业微信必须能访问适配器回调 URL；生产环境建议通过反向代理提供 HTTPS |

## 1）工作区配置（`.craft/config.json`）

在工作区 `.craft/config.json` 中启用 AppServer WebSocket，并注册 WeCom 外部渠道：

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

## 2）适配器配置（`.craft/wecom.json`）

在目标工作区创建 `.craft/wecom.json`：

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

字段说明：

| 配置项 | 说明 | 默认值 |
|--------|------|--------|
| `dotcraft.wsUrl` | DotCraft AppServer WebSocket 地址 | `ws://127.0.0.1:9100/ws` |
| `dotcraft.token` | AppServer WebSocket Token | 空 |
| `wecom.host` | 回调服务监听地址 | `0.0.0.0` |
| `wecom.port` | 回调服务监听端口 | `9000` |
| `wecom.scheme` | 本地回调服务协议，`http` 或 `https` | `http` |
| `wecom.tls.certPath` / `keyPath` | `https` 模式证书和私钥路径 | 空 |
| `wecom.adminUsers` | 管理员企业微信 UserId 列表 | `[]` |
| `wecom.whitelistedUsers` | 白名单企业微信 UserId 列表 | `[]` |
| `wecom.whitelistedChats` | 白名单 ChatId 列表 | `[]` |
| `wecom.approvalTimeoutMs` | 操作审批超时，毫秒 | `60000` |
| `wecom.robots` | 机器人回调凭据列表 | `[]` |

单个机器人配置：

| 配置项 | 必填 | 说明 |
|--------|------|------|
| `path` | 是 | 回调路径，如 `/dotcraft` |
| `token` | 是 | 企业微信后台配置的 Token |
| `aesKey` | 是 | EncodingAESKey，通常 43 位，不含等号 |

## 3）企业微信后台配置

在企业微信管理后台配置接收消息：

- URL：`http://your-server:9000/dotcraft`
- Token：与 `.craft/wecom.json` 中 `robots[].token` 一致
- EncodingAESKey：与 `.craft/wecom.json` 中 `robots[].aesKey` 一致

如果通过 Nginx/Caddy 等反向代理暴露 HTTPS，适配器仍可监听本地 HTTP。

## 4）启动

Desktop 会从打包资源中识别 `wecom-standard` 外部渠道，可在渠道管理界面启动。

开发环境可以直接运行：

```bash
cd sdk/typescript
npm run build --workspace @dotcraft/channel-wecom
npx dotcraft-channel-wecom --workspace F:\dotcraft
```

也可以在包目录下运行：

```bash
cd sdk/typescript/packages/channel-wecom
npm run build
npm start -- --workspace F:\dotcraft
```

## 使用说明

- 文件和附件会回复诊断信息，不直接提交给 Agent。
- 会话按 `userId + chatId` 隔离，`channelContext` 为 `chat:<ChatId>`。
- 支持 `/new`、`/help`、Heartbeat、Cron 等通用命令和投递链路。
- 审批关键词包括 `同意`、`同意全部`、`拒绝`、`yes`、`yes all`、`no`。

## Runtime 工具

| 工具 | 作用 | 备注 |
|------|------|------|
| `WeComSendVoice(filePath)` | 向当前企业微信聊天发送语音 | 仅支持 AMR，本地绝对路径，先上传临时素材 |
| `WeComSendFile(filePath)` | 向当前企业微信聊天发送文件 | 本地绝对路径，先上传临时素材 |

这些工具由适配器通过 external channel capabilities 暴露。工具名保持 PascalCase。

## Cron 和 Heartbeat

| `channel` | `to` | 投递目标 | 前置条件 |
|-----------|------|----------|----------|
| `"wecom"` | `"chat:<ChatId>"` 或 `"<ChatId>"` | 企业微信会话 | 该 ChatId 已有消息进入，适配器缓存了 webhook |

建议在企业微信会话中创建 Cron 任务，让任务自动关联当前 ChatId。若目标 chatId 尚未建立 webhook 缓存，投递会失败并返回 `No WeCom webhook is available for target ...`。
