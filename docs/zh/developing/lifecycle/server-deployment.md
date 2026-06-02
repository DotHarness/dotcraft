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

该栈会在 `deploy/docker/workspace` 下创建工作区。默认情况下，Compose 只把 AppServer、Dashboard 和渠道入口端口发布到服务器本机回环地址：

- AppServer：`ws://127.0.0.1:9100/ws`
- Dashboard：`http://127.0.0.1:8080/dashboard`
- QQ OneBot 反向 WebSocket：`ws://127.0.0.1:6700/`
- 企业微信回调：`http://127.0.0.1:9000/dotcraft`

远程访问 AppServer 和 Dashboard 时，优先使用 Desktop 的远程服务器 SSH tunnel、手动 SSH 端口转发，或反向代理。

如果 `APPSERVER_TOKEN` 留空，容器会把稳定 token 写入 `workspace/.craft/appserver.token`。

## 从 Desktop 连接

Desktop 可以通过系统 SSH 客户端连接这个 Compose 栈。它会复用 `~/.ssh/config`、`ssh-agent`、ProxyJump、硬件密钥和本地已有 key。Desktop 不会要求输入或保存 SSH 密码、私钥或 key passphrase。

1. 确认服务器上的栈已经启动：

   ```bash
   cd /opt/dotcraft/deploy/docker
   docker compose up -d
   ```

2. 确认本机可以免交互 SSH 到服务器：

   ```bash
   ssh -o BatchMode=yes user@host "echo ok"
   ```

3. 在 Desktop 打开 **Settings -> Servers -> Add server**。
4. **SSH target** 填 `user@host`，或填 SSH config alias，例如 `dotcraft-prod`。
5. **Identity file override** 默认留空，除非你确实需要强制指定某个 key。留空时 SSH 会按你的正常配置、agent 和默认 key 工作。
6. 点击 **Test SSH**。成功后添加 stack：
   - **Deployment folder**：服务器上包含 Compose 文件的目录，例如 `/opt/dotcraft/deploy/docker`
   - **Data folder**：留空，使用 stack 的 `workspace` 目录
   - **AppServer port**：`9100`
   - **Dashboard port**：`8080`
   - **Project name**：没有自定义 Compose project name 时留空
7. 点击 **Open in Desktop**。

Desktop 会打开到服务器的 SSH tunnel，读取 `workspace/.craft/appserver.token`，并通过 `ws://127.0.0.1:<local-port>/ws` 连接容器内 AppServer。Dashboard 会使用单独的 SSH tunnel 打开。

如果你已经自己维护 SSH 端口转发，也可以在 Connections 设置里手动填写 remote WebSocket URL；但推荐使用 Servers 页面，因为它会在重连和重启后重新建立 tunnel。

## 配置 SSH key

远端服务器连接需要 key-based、非交互 SSH。如果本机还没有 key，先创建一个：

```bash
ssh-keygen -t ed25519 -C "dotcraft-remote"
```

只把 `.pub` 公钥上传到服务器。不要把私钥复制进 DotCraft、提交到仓库，或上传到服务器。

macOS 或 Linux：

```bash
ssh-copy-id user@host
```

Windows PowerShell：

```powershell
type $env:USERPROFILE\.ssh\id_ed25519.pub | ssh user@host "mkdir -p ~/.ssh && chmod 700 ~/.ssh && cat >> ~/.ssh/authorized_keys && chmod 600 ~/.ssh/authorized_keys"
```

如果 key 设置了 passphrase，先把它加入 `ssh-agent`，再打开 Desktop：

```bash
ssh-add ~/.ssh/id_ed25519
```

Windows 上如有需要先启动 agent：

```powershell
Start-Service ssh-agent
ssh-add $env:USERPROFILE\.ssh\id_ed25519
```

长期使用时，推荐创建 SSH config alias：

```text
Host dotcraft-prod
  HostName example.com
  User root
  Port 22
  IdentityFile ~/.ssh/id_ed25519
```

然后在 Desktop 的 SSH target 中填写 `dotcraft-prod`。最后用与 Desktop 相同的非交互方式验证：

```bash
ssh -o BatchMode=yes dotcraft-prod "echo ok"
```

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

如果渠道网关不在同一台服务器上，只显式发布需要的渠道端口，例如在 `.env` 中设置 `QQ_PUBLISH_HOST=0.0.0.0`，并使用强随机渠道访问 token。

## 可选沙箱

沙箱默认关闭。普通的 `docker compose up -d` 不会挂载宿主机 Docker socket。

启用 OpenSandbox：

```bash
SANDBOX_ENABLED=true docker compose --profile sandbox up -d
```

这会用同一个镜像启动第二个服务，并挂载 `/var/run/docker.sock`。DotCraft 配置会把 `Tools.Sandbox.Domain` 指向 `opensandbox:5880`。

## 生产环境注意事项

- 默认 Compose 文件不会把 AppServer 或 Dashboard 暴露到 localhost 之外。不要把它们直接发布到公网；优先使用 Desktop SSH tunnel，或使用带 TLS 和认证的反向代理。
- 如果通过反向代理暴露这些服务，请使用强 `APPSERVER_TOKEN`，并配置 Dashboard 用户名/密码。
- TLS 建议由反向代理终止。内置 AppServer 监听 `ws://`，不是 `wss://`。
- 当前服务器 Docker 镜像只提供 x64。Arm64 Linux 主机在 arm64 服务器镜像可用前，请使用 Docker 模拟或从源码构建。
- QQ 使用 OneBot 反向 WebSocket 端口（默认 `6700`）。
- 企业微信使用回调端口（默认 `9000`）。
- 微信扫码登录文件位于 `workspace/.craft/tmp/channel-weixin-standard/`。

相关文档：[Channels 与 Bots](../../features/entry-points/channels)、[安全与沙箱](../../features/self-hosted/security)，以及 `deploy/docker/README_ZH.md`。
