# Hub 本地协调

本页面面向集成方与贡献者，大多数用户不需要直接接触 Hub。Hub 是 DotCraft 的本地运行时协调器。它在你的电脑上按用户运行，负责发现、启动、复用和停止每个工作区对应的 AppServer。Desktop 与 CLI 默认通过 Hub 工作。

> [!NOTE]
> 远程、CI、机器人或显式调试 AppServer 的场景请走 [AppServer 模式](./appserver)。

## 关键属性

- 每个 OS 用户通常只有一个 Hub
- 每个工作区仍然只有一个 AppServer
- Hub 不处理普通对话流量，也不代理 AppServer 协议
- 客户端只在启动阶段询问 Hub："请确保这个工作区的 AppServer 可用"
- 启动完成后，客户端**直接**连接返回的 AppServer WebSocket 地址

![DotCraft Hub local coordination topology](/hub-coordination-topology.svg)

## 何时手动启动

通常不需要手动启动 `dotcraft hub`，Desktop 和其他本地客户端会按需启动。调试本地协调行为时：

```bash
dotcraft hub
```

启动后 Hub 在本机回环地址提供本地管理 API，并把发现信息写入 `~/.craft/hub/hub.lock`。Hub 自动分配本地端口。如果启动因端口被占用、权限受限或安全软件阻止本地回环而失败，重启 Hub 或 Desktop 即可重新分配。

## 本地状态

```text
~/.craft/hub/
├── hub.lock          # 当前 Hub 发现信息：API 地址、PID、启动时间、本地 token、binary 路径
└── appservers.json   # Hub 记录的工作区 AppServer 状态（用于展示和恢复）
```

每个工作区还有：

```text
<workspace>/.craft/appserver.lock
```

它表示该工作区当前由哪个 AppServer 进程拥有，防止同一个工作区被多个本地 AppServer 同时占用。

当 Hub 或 AppServer 发现 `appserver.lock` 是已退出进程留下的 stale lock 时，会自动移除并继续启动。如果锁指向的 AppServer 仍在运行且 WebSocket 端点健康，Hub 会直接复用该端点，而不是启动重复进程。如果锁指向一个 Hub 无法安全复用的存活 AppServer，关闭占用该工作区的 Desktop 或 CLI 进程，或在托盘里停止对应工作区运行时，然后重新打开。

## Desktop 与托盘

Desktop 提供 Hub 的可视化层，Hub 自身是无界面的后台协调器。Desktop 可以提供：

- 打开或切换工作区
- 查看最近和正在运行的工作区
- 打开 Desktop 或 Dashboard
- 重启或停止 Hub 托管的工作区运行时
- 接收 Hub 转发的系统通知（任务完成、需要审批、运行时状态变化）

托盘退出时，Desktop 可以请求 Hub 停止它托管的工作区 AppServer。

要让 Desktop 能打开工作区，`dotcraft` / `dotcraft.exe` 必须在 `PATH` 中，或在 Desktop 设置里配置 AppServer 可执行文件路径。

## 实现客户端

常规本地 client 应使用 [DotCraft SDK](../sdks/)。其 Hub API 会发现或启动 Hub、确保工作区 AppServer、保留结构化错误，随后建立 AppServer 连接。只有在实现自定义传输、不受支持的语言或调试协议时，才直接实现 [Hub 协议](../protocols/hub-protocol)。

## 相关文档

- [SDK 快速开始](../sdks/quickstart) — 推荐的 client 路径
- [AppServer 模式](./appserver) — 远程 / 多客户端 / CI
- [Hub 协议](../protocols/hub-protocol) — 客户端协议概览
- [统一会话核心](../architecture/session-core) — Hub 与 AppServer 在整体架构中的位置
