# AppServer 模式

本页面向直接管理 AppServer 的集成方与贡献者，日常 Desktop 与 `dotcraft exec` 走 [Hub 本地协调](./hub)。AppServer 是建立在宿主所拥有的 [Session Core](../architecture/session-core) 之上的可选协议与传输边界：它通过 JSON-RPC 将宿主唯一的 `ISessionService` 投影给外部客户端，而不会创建第二套会话内核。一个 AppServer 进程持有一份 Session Core，stdio 与 WebSocket 两种传输可以同时开着，Desktop、ACP、`dotcraft exec`、外部渠道适配器和自定义集成连上来共享同一份会话状态。

客户端库 API 见 [DotCraft SDK](../sdks/)，wire message 定义见 [AppServer 协议](../protocols/appserver-protocol)。

![DotCraft AppServer 模式拓扑：一个宿主进程同时提供 stdio 与 WebSocket 传输，外部客户端共享同一份 Session Core](/appserver-mode-topology.svg)

## 启动

```bash
# stdio 模式（默认，适用于子进程通信）
dotcraft app-server

# 纯 WebSocket 模式（适用于远程连接、多客户端）
dotcraft app-server --listen ws://127.0.0.1:9100

# stdio + WebSocket 双模式
dotcraft app-server --listen ws+stdio://127.0.0.1:9100
```

服务端监听不带路径的 `ws://host:port` 地址，客户端连接时追加 `/ws`，例如 `ws://host:port/ws`。下面的示例都遵循这条规则。

内置监听器不做 TLS 终止，`--listen wss://…` 会被直接拒绝。需要 TLS 时在 AppServer 前面放一层反向代理由它终止，客户端再连 `wss://host/ws`。

## 命令行连接远程 AppServer

```bash
# 一次性命令
dotcraft exec --remote ws://127.0.0.1:9100/ws "总结当前工作区"

# 带 Token 认证
dotcraft exec --remote ws://server:9100/ws --token my-secret "总结当前工作区"
```

## 命令行参考

### 子命令与全局参数

| 命令 / 参数 | 说明 |
|---|---|
| `dotcraft exec <prompt>` | 运行一次性命令行 Agent 任务 |
| `dotcraft exec -` | 从 stdin 读取输入并运行一次性任务 |
| `dotcraft app-server` | 启动 AppServer（默认 stdio 模式） |
| `--listen <URL>` | AppServer 传输方式，搭配 `app-server` 使用 |
| `--remote <URL>` | 客户端连接远程 AppServer，搭配 `exec` 或 ACP 使用 |
| `--token <VALUE>` | WebSocket 认证 Token，可搭配 `--listen` 或 `--remote` |

### `--listen` URL Scheme

| Scheme | 传输模式 | stdout 行为 | 示例 |
|---|---|---|---|
| `stdio://` | 纯 stdio（默认） | 保留给 JSON-RPC | `--listen stdio://` |
| `ws://host:port` | 纯 WebSocket | 正常控制台输出 | `--listen ws://127.0.0.1:9100` |
| `ws+stdio://host:port` | stdio + WebSocket | 保留给 JSON-RPC | `--listen ws+stdio://127.0.0.1:9100` |

## Transport 模式

### stdio（默认）

AppServer 通过 stdin/stdout 以换行分隔的 JSON（JSONL）格式通信。这是 ACP 和自定义客户端常用的本地子进程通信方式。

```
Client (stdin) → JSON-RPC Request → AppServer
AppServer → JSON-RPC Response/Notification → Client (stdout)
AppServer → 诊断日志 → stderr
```

**特点**：

- 1:1 通信（一个客户端对应一个服务进程）
- stdout 被 wire protocol 占用，控制台日志输出到 stderr
- 无需网络配置，适合本地开发

### WebSocket

AppServer 在指定地址上启动 WebSocket 监听，每个 WebSocket 文本帧携带一条完整的 JSON-RPC 消息。

```bash
dotcraft app-server --listen ws://127.0.0.1:9100
```

**特点**：

- 多客户端并发连接（每个连接独立维护初始化状态和线程订阅）
- stdout 不被占用，控制台正常输出
- 支持远程连接和网络认证

### stdio + WebSocket 双模式

```bash
dotcraft app-server --listen ws+stdio://127.0.0.1:9100
```

适合需要同时支持子进程通信和远程连接的部署。

## 安全认证

监听非回环地址（不是 `127.0.0.1` / `::1`）时必须配置 Token。没有 Token 时 AppServer 拒绝启动，不会留下一个无认证的开放端口。

### 服务端

```bash
dotcraft app-server --listen ws://0.0.0.0:9100 --token my-secret
```

### 客户端

```bash
dotcraft exec --remote ws://server:9100/ws --token my-secret "检查状态"
```

Token 通过 WebSocket 连接 URL 的查询参数传递：`ws://host:port/ws?token=<value>`。服务端一旦设置 `--token`，所有客户端——Desktop、ACP、`dotcraft exec` 和自定义客户端——都必须携带同一个 Token，缺失或不匹配的 Token 会在 WebSocket 握手完成前被 HTTP `401` 拒绝。Token 取值需要是 URL 安全字符（字母数字加 `-`、`_`、`.`），否则客户端要自行做百分号编码。

## 配置方式

### 命令行参数（推荐）

命令行参数优先级高于配置文件：

```bash
dotcraft app-server --listen ws://127.0.0.1:9100 --token my-secret
```

### config.json（替代方案）

适合需要固定配置的部署场景。`ExternalChannels` 写在配置中，告诉 DotCraft 如何启动外部 channel adapter。structured delivery 能力和 `channelTools` 列表不写在配置文件里，由 adapter 在 `initialize` 握手时动态声明。

**AppServer 配置项**

| 配置项 | 说明 | 默认 |
|---|---|---|
| `AppServer.Mode` | 传输模式：`Disabled` / `Stdio` / `WebSocket` / `StdioAndWebSocket` | `Disabled` |
| `AppServer.WebSocket.Host` | WebSocket 监听地址 | `127.0.0.1` |
| `AppServer.WebSocket.Port` | WebSocket 监听端口 | `9100` |
| `AppServer.WebSocket.Token` | WebSocket 认证 Token | 空 |

**命令行客户端配置项**

| 配置项 | 说明 | 默认 |
|---|---|---|
| `CLI.AppServerUrl` | `dotcraft exec` 使用的远程 AppServer WebSocket 地址 | 空 |
| `CLI.AppServerToken` | `dotcraft exec` 使用的远程连接认证 Token | 空 |
| `CLI.AppServerBin` | `dotcraft exec` 启动本地 Hub/AppServer 时使用的自定义可执行文件路径 | 空（使用当前进程） |

**示例**

```json
{
    "AppServer": {
        "Mode": "WebSocket",
        "WebSocket": {
            "Host": "0.0.0.0",
            "Port": 9100,
            "Token": "my-secret"
        }
    }
}
```

```json
{
    "CLI": {
        "AppServerUrl": "ws://server:9100/ws",
        "AppServerToken": "my-secret"
    }
}
```

## 常见场景

| 场景 | 做法 |
|---|---|
| 用脚本运行一次性任务 | `dotcraft exec "..."` |
| 在 Desktop / ACP / 自定义客户端之间共享一个后端 | `dotcraft app-server --listen ws://127.0.0.1:9100` |
| 连接到远程工作区 | 用 WebSocket 监听，客户端连接 `/ws` |
| 构建自定义 raw client | 通过 stdio 或 WebSocket 收发 JSON-RPC 2.0 |

## 相关文档

- [SDK 快速开始](../sdks/quickstart) — 推荐的 client 路径，不必自己实现协议
- [配置参考](../configuration) — `AppServer.*` / `CLI.*` 字段的完整说明
- [架构总览](../architecture/overview) — AppServer 在程序集职责与依赖边界中的位置
