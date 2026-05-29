# 服务器部署

DotCraft 可以在 Linux 主机上以无头 AppServer 方式运行。推荐的服务器路径是 `deploy/docker` 中的 Docker Compose 部署，它会打包：

- 自包含的 `dotcraft` AppServer 二进制
- Node.js 和 TypeScript 渠道模块（`telegram`、`feishu`、`qq`、`wecom`、`weixin`）
- 可选 OpenSandbox，仅在启用 `sandbox` Compose profile 时启动

## Docker Compose 快速开始

```bash
cd deploy/docker
cp .env.example .env
# 编辑 .env
docker compose up -d
```

该栈会在 `deploy/docker/workspace` 下创建工作区。AppServer 默认监听 `ws://<server>:9100/ws`，Dashboard 默认监听 `http://<server>:8080`。

如果 `APPSERVER_TOKEN` 留空，容器会把稳定 token 写入 `workspace/.craft/appserver.token`。

## 选择渠道

在 `.env` 中设置 `ENABLED_CHANNELS`：

```dotenv
ENABLED_CHANNELS=telegram,feishu
```

支持值：`telegram`、`feishu`、`qq`、`wecom`、`weixin`。`all` 会启用 `telegram,feishu,qq,wecom`；微信因为需要扫码登录，故意不包含在 `all` 里。

渲染器会写入 `transport: "managedWebsocket"` 和 `builtinModule: "channel-<name>"` 的 `ExternalChannels` 条目。随后 AppServer 会启动 Node 适配器，并自动注入 WebSocket URL/token。

环境变量只配置必填凭据：

```dotenv
TELEGRAM_BOT_TOKEN=
FEISHU_APP_ID=
FEISHU_APP_SECRET=
QQ_ACCESS_TOKEN=
WECOM_ROBOT_TOKEN=
WECOM_ROBOT_AES_KEY=
```

高级字段保留在挂载目录中的渠道文件里，例如 `workspace/.craft/qq.json` 和 `workspace/.craft/wecom.json`。白名单、群聊 @ 规则、审批超时、回调路径、卡片文本等都直接编辑这些文件。重启会保留这些修改。

## 可选沙箱

沙箱默认关闭。普通的 `docker compose up -d` 不会挂载宿主机 Docker socket。

启用 OpenSandbox：

```bash
SANDBOX_ENABLED=true docker compose --profile sandbox up -d
```

这会用同一个镜像启动第二个服务，并挂载 `/var/run/docker.sock`。DotCraft 配置会把 `Tools.Sandbox.Domain` 指向 `opensandbox:5880`。

## 生产环境注意事项

- AppServer 暴露到 localhost 之外时，请使用强 `APPSERVER_TOKEN`。
- TLS 建议由反向代理终止。内置 AppServer 监听 `ws://`，不是 `wss://`。
- 当前服务器镜像和 Release 归档只提供 x64。Arm64 主机在 arm64 产物可用前，请使用 Docker 模拟或从源码构建。
- QQ 使用发布出来的 OneBot 反向 WebSocket 端口（默认 `6700`）。
- 企业微信使用发布出来的回调端口（默认 `9000`）。
- 微信扫码登录文件位于 `workspace/.craft/tmp/channel-weixin-standard/`。

相关文档：[Channels 与 Bots](../features/entry-points/channels.md)、[安全与沙箱](../features/security.md)，以及 `deploy/docker/README_ZH.md`。
