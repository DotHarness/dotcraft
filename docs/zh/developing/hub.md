# Hub 本地协调

Hub 是 DotCraft 的本地运行时协调器。它在你的电脑上按用户运行，负责发现、启动、复用和停止每个工作区对应的 AppServer。Desktop / TUI 默认通过 Hub 工作，普通用户**不需要直接接触 Hub**。

> [!NOTE]
> 远程、CI、机器人或显式调试 AppServer 的场景请走 [AppServer 模式](./appserver.md)。

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

启动后 Hub 在本机回环地址提供本地管理 API，并把发现信息写入 `~/.craft/hub/hub.lock`。

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

当 Hub 或 AppServer 发现 `appserver.lock` 是已退出进程留下的 stale lock 时，会自动移除并继续启动。如果锁指向的 AppServer 仍在运行且 WebSocket 端点健康，Hub 会直接复用该端点，而不是启动重复进程。

## Desktop 与托盘

Desktop 提供 Hub 的可视化层，Hub 自身是无界面的后台协调器。Desktop 可以提供：

- 打开或切换工作区
- 查看最近和正在运行的工作区
- 打开 Desktop 或 Dashboard
- 重启或停止 Hub 托管的工作区运行时
- 接收 Hub 转发的系统通知（任务完成、需要审批、运行时状态变化）

托盘退出时，Desktop 可以请求 Hub 停止它托管的工作区 AppServer。

## 故障排查

### Desktop 无法打开工作区

确认 `dotcraft` / `dotcraft.exe` 在 `PATH` 中，或在 Desktop 设置里配置 AppServer 可执行文件路径。

### 工作区被占用

`<workspace>/.craft/appserver.lock` 指向另一个仍然存活、且 Hub 无法安全复用的 AppServer。关闭占用工作区的 Desktop / TUI / CLI，或在托盘里停止对应工作区运行时后重试。已退出进程留下的锁会自动恢复。

### 本地端口冲突

Hub 自动分配端口。失败通常是端口被占用、权限受限或安全软件阻止本地回环连接。重启 Hub 或 Desktop 通常会重新分配端口。

## 实现客户端

参考 [Hub 协议](./hub-protocol.md) 实现自己的本地客户端：发现 Hub、调用 `ensure`、订阅生命周期事件。

## 相关入口

- [AppServer 模式](./appserver.md) — 远程 / 多客户端 / CI
- [Hub 协议](./hub-protocol.md) — 客户端协议概览
- [统一会话核心](../features/session-core.md) — Hub 与 AppServer 在整体架构中的位置
