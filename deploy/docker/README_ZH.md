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

默认情况下，AppServer、Dashboard 和渠道入口端口只发布到服务器本机回环地址：

- AppServer：`ws://127.0.0.1:9100/ws`
- Dashboard：`http://127.0.0.1:8080/dashboard`
- QQ OneBot 反向 WebSocket：`ws://127.0.0.1:6700/`
- 企业微信回调：`http://127.0.0.1:9000/dotcraft`

远程访问 AppServer 和 Dashboard 时，优先使用 Desktop 的远程服务器 SSH tunnel、手动 SSH 端口转发，或反向代理。

## 从 Desktop 连接

Desktop 通过系统 SSH 客户端连接这个栈，不支持输入或保存 SSH 密码。请先确认本机可以免交互 SSH 到服务器：

```bash
ssh-keygen -t ed25519 -C "dotcraft-remote"
ssh-copy-id user@host
ssh -o BatchMode=yes user@host "echo ok"
```

Windows PowerShell 可以这样上传公钥：

```powershell
type $env:USERPROFILE\.ssh\id_ed25519.pub | ssh user@host "mkdir -p ~/.ssh && chmod 700 ~/.ssh && cat >> ~/.ssh/authorized_keys && chmod 600 ~/.ssh/authorized_keys"
```

然后在 Desktop 打开 **Settings -> Servers -> Add server**，SSH target 填 `user@host` 或 SSH config alias，identity override 留空，Test SSH 成功后，把这个 Compose 目录添加为 stack deployment folder。

完整说明见 [服务器部署](../../docs/zh/developing/lifecycle/server-deployment.md)。

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

如果 NapCat、企业微信或其他网关不在同一台服务器上，需要在 `.env` 中显式发布对应渠道端口，例如：

```dotenv
QQ_PUBLISH_HOST=0.0.0.0
```

直接发布渠道端口时，请使用强随机访问 token。

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

- 默认 Compose 文件不会把 AppServer 或 Dashboard 暴露到 localhost 之外；优先使用 Desktop SSH tunnel，不要直接发布到公网。
- 如果通过反向代理暴露这些服务，请使用强 `APPSERVER_TOKEN`，并配置 Dashboard 用户名/密码。
- TLS 建议由反向代理终止；内置 AppServer 监听的是 `ws://`，不是 `wss://`。
- 当前镜像是 linux-x64。Arm64 需要后续新增发布目标。
