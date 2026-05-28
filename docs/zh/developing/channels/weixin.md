# DotCraft 微信渠道适配器

`@dotcraft/channel-weixin` 通过 WebSocket 外部渠道协议，将腾讯 iLink（微信机器人 API）接入 DotCraft。

## 功能概览

- 通过 WebSocket 连接 DotCraft AppServer
- 首次登录采用二维码交互认证
- 会话状态持久化到模块专属状态目录
- 支持流式回合回复与审批处理
- 支持 `/new` 新建会话命令
- 提供真实文件/图片发送工具：`WeixinSendFileToCurrentChat`、`WeixinSendImageToCurrentChat`
- 支持结构化 `file` / `image` 投递（本地路径或 base64 数据源）
- 微信 iLink 不提供 Markdown 渲染入口，普通回复和媒体 caption 会转为纯文本发送

## 安装

```bash
cd sdk/typescript
npm install
npm run build:all
```

## 1）工作区配置（`.craft/config.json`）

将本目录的 `config.example.json` 合并到工作区 `.craft/config.json`，启用 `weixin` 外部渠道：

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

## 2）适配器配置（`.craft/weixin.json`）

在目标工作区创建 `.craft/weixin.json`：

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

字段说明：

- `dotcraft.wsUrl`：DotCraft AppServer WebSocket 地址
- `dotcraft.token`：可选 AppServer 鉴权令牌
- `weixin.apiBaseUrl`：iLink API 基础地址
- `weixin.pollIntervalMs`：可选轮询间隔
- `weixin.pollTimeoutMs`：可选长轮询超时
- `weixin.approvalTimeoutMs`：可选审批超时时间
- `weixin.botType`：可选二维码登录 bot 类型

## 3）CLI 使用方式

推荐方式：

```bash
npx dotcraft-channel-weixin --workspace /path/to/workspace
```

可选配置覆盖：

```bash
npx dotcraft-channel-weixin --workspace /path/to/workspace --config /custom/weixin.json
```

## 4）交互式初始化（二维码登录）

- 首次运行若无已保存会话，会进入 `authRequired`
- 终端模式会在终端渲染二维码
- 用户扫码确认后，适配器进入 `ready`
- 会话过期时生命周期为 `authExpired -> authRequired`，需要重新扫码

## 5）状态与临时目录布局

模块数据统一存储在工作区 `.craft/` 下：

- 持久状态：`.craft/state/weixin-standard/`
  - 凭据、同步游标、上下文 token
- 临时文件：`.craft/tmp/weixin-standard/`
  - 二维码 URL 与二维码图片产物

## 6）宿主集成方式

宿主通过模块契约导入并监听生命周期：

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

## 开发说明

- 构建所有 TypeScript 包：
  - `cd sdk/typescript && npm run build:all`
- 运行全部测试：
  - `cd sdk/typescript && npm run test:all`
- 仅运行本包测试：
  - `npm run test --workspace @dotcraft/channel-weixin`
- 预览打包产物：
  - `cd sdk/typescript/packages/channel-weixin && npm pack --dry-run`

## 致谢

[@tencent-weixin/openclaw-weixin](https://www.npmjs.com/package/@tencent-weixin/openclaw-weixin)
