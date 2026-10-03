# Hub 本地协调

本页面向集成方与贡献者，大多数用户不需要直接接触 Hub。Hub 是 DotCraft 的本地运行时协调器，在你的电脑上按用户运行，负责发现、启动、复用和停止每个工作区对应的 AppServer。Desktop 与 CLI 默认通过 Hub 工作。

> [!NOTE]
> 远程、CI、机器人或显式调试 AppServer 的场景请走 [AppServer 模式](./appserver)。

![Hub 本地协调拓扑：Desktop、CLI 与 SDK 客户端只在 bootstrap 阶段经过 Hub，随后直连各自工作区的 AppServer](/hub-coordination-topology.svg)

## 关键属性

- 每个 OS 用户只有一个 Hub
- 每个工作区仍然只有一个 AppServer
- Hub 不处理普通对话流量，也不代理 AppServer 协议
- 客户端只在启动阶段询问 Hub：请确保这个工作区的 AppServer 可用
- 启动完成后，客户端**直接**连接返回的 AppServer WebSocket 地址

## 何时手动启动

通常不需要手动启动 `dotcraft hub`，Desktop 和其他本地客户端会按需启动。调试本地协调行为时：

```bash
dotcraft hub
```

启动后 Hub 在本机回环地址提供本地管理 API，并把发现信息写入 `~/.craft/hub/hub.lock`。Hub 自动分配本地端口。如果启动因端口被占用、权限受限或安全软件阻止本地回环而失败，重启 Hub 或 Desktop 即可重新分配。

## 本地状态

```text
~/.craft/hub/
├── hub.lock                  # 当前 Hub 发现信息：API 地址、PID、启动时间、本地 token、版本、binary 路径
├── appservers.json           # Hub 记录的工作区 AppServer 状态（用于展示和恢复）
├── mobile.json               # 手机访问：开关状态、已配对手机和配对码（只存哈希）、中继地址与 Token
└── mobile-certificate.pfx    # 已配对手机固定信任的证书
```

每个工作区还有：

```text
<workspace>/.craft/appserver.lock
```

它表示该工作区当前由哪个 AppServer 进程拥有，防止同一个工作区被多个本地 AppServer 同时占用。

当 Hub 或 AppServer 发现 `appserver.lock` 是已退出进程留下的 stale lock 时，会自动移除并继续启动。如果锁指向的 AppServer 仍在运行且 WebSocket 端点健康，Hub 会直接复用该端点，而不是启动重复进程。如果锁指向一个 Hub 无法安全复用的存活 AppServer，关闭占用该工作区的 Desktop 或 CLI 进程，或在托盘里停止对应工作区运行时，然后重新打开。

## 手机访问

在**设置 → 连接 → 手机**中打开手机访问后，Hub 会同时运行移动网关：一个 HTTPS 监听器，已配对的手机通过局域网或私有网络访问它。它只接受来自本机回环地址和私有网络地址的连接，手机只信任配对时拿到指纹的那张证书。全局 `~/.craft/config.json` 中的两个设置决定它的监听位置：

| 设置 | 默认值 | 用途 |
|------|--------|------|
| `Hub.MobileHost` | `0.0.0.0` | 网关绑定的地址 |
| `Hub.MobilePort` | `47610` | 固定的网关端口。已配对的手机会记住它，修改后需要重新配对 |

```json
{
  "Hub": {
    "MobilePort": 47611
  }
}
```

如果端口被其他程序占用，打开手机访问会失败，Hub 不会改用其他端口。释放该端口，或设置 `Hub.MobilePort` 后重启 Hub。网关协议由 [DotCraft Mobile 规范](https://github.com/DotHarness/dotcraft/blob/main/specs/clients/mobile.md)定义。

### 随处访问

中继让已配对的手机从其他网络访问这台电脑。把它运行在电脑和手机都能访问的服务器上，并放在提供公网 HTTPS 的反向代理之后。中继只转发加密后的字节：手机仍然使用固定信任的证书和设备凭据与网关建立 TLS 连接，因此中继看不到凭据、项目或对话。

DotCraft 源码中的 Compose 模板会运行中继，并附带一个为你的域名申请证书的 Caddy 代理：

```bash
cp -r docker/relay /opt/dotcraft-relay
cd /opt/dotcraft-relay
cp .env.example .env
```

在 `.env` 中把 `RELAY_PUBLIC_HOST` 设为中继的域名，把 `RELAY_TOKEN` 设为一个足够长的随机值，例如 `openssl rand -hex 32` 的输出。服务器的 80 和 443 端口需要能被访问，Caddy 才能申请证书。启动中继：

```bash
docker compose up -d
```

如果服务器上已有反向代理，用 `docker compose up -d relay` 只启动中继，并把 `/r/*`（包括 WebSocket 升级）转发到 `127.0.0.1:47620`。不使用 Docker 时直接运行：

```bash
dotcraft relay serve --listen http://127.0.0.1:47620 --token <token>
```

Token 也可以通过环境变量 `DOTCRAFT_RELAY_TOKEN` 提供，代替 `--token`。

在电脑上打开**设置 → 连接 → 手机 → 随处访问**，填入中继地址（例如 `https://relay.example.com`）和 Token。手机访问开启期间，Hub 会保持一条到中继的出站连接，断开后自动重连。新生成的配对码会包含中继。之前配对的手机下次在同一网络下连接时会获取它。

## Desktop 与托盘

Hub 自身是无界面的后台协调器，可视化层由 Desktop 托盘提供：

- 打开或切换工作区
- 查看最近和正在运行的工作区
- 打开 Desktop 或 Dashboard
- 重启或停止 Hub 托管的工作区运行时
- 接收 Hub 转发的系统通知（任务完成、需要审批、运行时状态变化）

从托盘退出会请求 Hub 关闭，Hub 随之停止它托管的工作区 AppServer。

要让 Desktop 能打开工作区，`dotcraft` / `dotcraft.exe` 必须在 `PATH` 中，或在 Desktop 设置里配置 AppServer 可执行文件路径。

## 实现客户端

常规本地 client 应使用 [DotCraft SDK](../sdks/)。其 Hub API 会发现或启动 Hub、确保工作区 AppServer、保留结构化错误，随后建立 AppServer 连接。只有在实现自定义传输、不受支持的语言或调试协议时，才直接实现 [Hub 协议](../protocols/hub-protocol)。

## 相关文档

- [SDK 快速开始](../sdks/quickstart) — 推荐的 client 路径，bootstrap 已经封装好
- [统一会话核心](../architecture/session-core) — Hub 与 AppServer 之上的 Thread / Turn / Item 模型
