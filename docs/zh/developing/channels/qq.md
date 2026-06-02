# DotCraft QQ 渠道适配器

`@dotcraft/channel-qq` 通过 WebSocket 外部渠道协议将 QQ 接入 DotCraft。适配器连接 DotCraft AppServer，并在本地提供 OneBot v11 反向 WebSocket 服务，供 NapCat 等 OneBot 实现连接。

## 功能概览

- 通过 WebSocket 连接 DotCraft AppServer
- 提供 OneBot v11 反向 WebSocket 服务
- 支持 QQ 私聊和群聊
- 群聊默认仅在 @机器人 时响应
- 每个 QQ 私聊用户或 QQ 群映射到独立 DotCraft thread
- 支持 QQ 会话内审批
- 提供语音、视频、文件相关 Runtime 工具

> [!CAUTION]
> 使用第三方 QQ 协议框架存在账号风险，请自行评估。

## 前置条件

| 需求 | 说明 |
|------|------|
| QQ 账号 | 用作机器人的 QQ 号，建议使用小号 |
| OneBot v11 实现 | 推荐 NapCat，负责登录 QQ 并连接反向 WebSocket |
| DotCraft AppServer | 需要启用 WebSocket 模式 |
| QQ 渠道包 | 发布包中位于 `resources/modules/channel-qq`，开发环境中位于 `sdk/typescript/packages/channel-qq` |

## 1）工作区配置（`.craft/config.json`）

在工作区 `.craft/config.json` 中启用 AppServer WebSocket，并注册 QQ 外部渠道：

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

## 2）适配器配置（`.craft/qq.json`）

在目标工作区创建 `.craft/qq.json`：

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

字段说明：

| 配置项 | 说明 | 默认值 |
|--------|------|--------|
| `dotcraft.wsUrl` | DotCraft AppServer WebSocket 地址 | `ws://127.0.0.1:9100/ws` |
| `dotcraft.token` | AppServer WebSocket Token | 空 |
| `qq.host` | OneBot 反向 WebSocket 监听地址 | `127.0.0.1` |
| `qq.port` | OneBot 反向 WebSocket 监听端口 | `6700` |
| `qq.accessToken` | OneBot 鉴权 Token，需与 NapCat 一致 | 空 |
| `qq.adminUsers` | 管理员 QQ 号列表 | `[]` |
| `qq.whitelistedUsers` | 白名单用户 QQ 号列表 | `[]` |
| `qq.whitelistedGroups` | 白名单群号列表 | `[]` |
| `qq.approvalTimeoutMs` | 操作审批超时，毫秒 | `60000` |
| `qq.requireMentionInGroups` | 群聊是否必须 @ 机器人 | `true` |

如果 `adminUsers`、`whitelistedUsers`、`whitelistedGroups` 都为空，适配器不会响应任何 QQ 用户。请至少配置一个管理员。

## 3）配置 NapCat

在 NapCat WebUI 中创建 WebSocket 客户端（反向 WS）：

1. URL 填写 `ws://127.0.0.1:6700/`。
2. Token 填写与 `qq.accessToken` 相同的值。
3. 消息格式选择 `array`。

如果 NapCat 运行在 Docker 或另一台机器上，请把 `127.0.0.1` 替换为 QQ 适配器所在机器的可访问地址。

## 4）启动

Desktop 会从打包资源中识别 `qq-standard` 外部渠道，可在渠道管理界面启动。

开发环境可以直接运行：

```bash
cd sdk/typescript
npm run build --workspace @dotcraft/channel-qq
npx dotcraft-channel-qq --workspace F:\examples\workspace
```

也可以在包目录下运行：

```bash
cd sdk/typescript/packages/channel-qq
npm run build
npm start -- --workspace F:\examples\workspace
```

## 使用说明

- 群聊默认只响应 @ 机器人的消息。
- 私聊会进入对应用户的独立会话。
- 每个 QQ 群共享一条 DotCraft thread，每条 turn 会记录真实发言人。
- 管理员触发需要审批的操作时，适配器会在 QQ 会话里发送审批提示。
- 回复 `同意`、`允许`、`yes`、`approve` 会通过审批；回复 `拒绝`、`no`、`reject`、`deny` 会拒绝审批。

## Runtime 工具

QQ 渠道提供以下工具，供 Agent 执行显式跨目标投递：

| 工具 | 目标 | 来源 |
|------|------|------|
| `QQSendGroupVoice` | QQ 群 | 本地路径、HTTP URL、`base64://...` |
| `QQSendPrivateVoice` | QQ 用户 | 本地路径、HTTP URL、`base64://...` |
| `QQSendGroupVideo` | QQ 群 | 本地路径、HTTP URL |
| `QQSendPrivateVideo` | QQ 用户 | 本地路径、HTTP URL |
| `QQUploadGroupFile` | QQ 群 | 本地绝对路径 |
| `QQUploadPrivateFile` | QQ 用户 | 本地绝对路径 |

这些工具由适配器通过 external channel capabilities 暴露。工具名保持 PascalCase。
