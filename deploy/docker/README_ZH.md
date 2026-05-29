# DotCraft Docker 部署

用一个 Compose 栈启动 DotCraft AppServer、内置 TypeScript 渠道适配器，以及可选 OpenSandbox。

## 快速开始

```bash
cd deploy/docker
cp .env.example .env
# 编辑 .env：模型供应商、模型、API Key、ENABLED_CHANNELS 和渠道密钥
docker compose up -d
```

运行状态会保存在 `./workspace`。首次启动时，entrypoint 会创建：

- `./workspace/.craft/config.json`
- 已启用渠道对应的 `./workspace/.craft/<channel>.json`
- 当 `APPSERVER_TOKEN` 留空时，生成 `./workspace/.craft/appserver.token`

默认 AppServer 地址是 `ws://<server>:9100/ws`。Dashboard 暴露在 `8080` 端口。

## 启用渠道

在 `.env` 中设置 `ENABLED_CHANNELS`：

```dotenv
ENABLED_CHANNELS=telegram,feishu
```

支持值：`telegram`、`feishu`、`qq`、`wecom`、`weixin`。`all` 表示启用 `telegram,feishu,qq,wecom`。

`.env` 只负责渲染必填凭据。高级字段，例如白名单、群聊是否必须 @、卡片标题等，直接编辑挂载目录里的 `./workspace/.craft/<channel>.json`；重启时渲染器只补齐缺失的必填字段，不覆盖这些高级设置。

常用必填值：

```dotenv
TELEGRAM_BOT_TOKEN=
FEISHU_APP_ID=
FEISHU_APP_SECRET=
QQ_ACCESS_TOKEN=
WECOM_ROBOT_TOKEN=
WECOM_ROBOT_AES_KEY=
```

QQ 默认在 `${QQ_PORT:-6700}` 监听 OneBot 反向 WebSocket。企业微信默认在 `${WECOM_PORT:-9000}` 监听回调请求。

微信需要交互式扫码登录。启用后查看 `./workspace/.craft/tmp/channel-weixin-standard/qr.png`。

## 可选沙箱

沙箱默认关闭，所以普通的 `docker compose up -d` 不会挂载 Docker socket。

启用 OpenSandbox：

```bash
SANDBOX_ENABLED=true docker compose --profile sandbox up -d
```

这会使用同一个 DotCraft 镜像启动第二个服务，并挂载 `/var/run/docker.sock`。DotCraft 会将 `Tools.Sandbox.Domain` 指向 `opensandbox:5880`。

## 本地构建

Compose 默认拉取 `ghcr.io/dotharness/dotcraft:latest`。如果要从源码构建，取消 `dotcraft` 服务下 `build:` 配置的注释。

```bash
docker compose build dotcraft
docker compose up -d
```

## 生产环境注意事项

- 暴露 `9100` 端口时必须使用强 `APPSERVER_TOKEN`。
- TLS 建议由反向代理终止；内置 AppServer 监听的是 `ws://`，不是 `wss://`。
- 当前镜像是 linux-x64。Arm64 需要后续新增发布目标。
