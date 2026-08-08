# Hub Protocol

Hub Protocol 是 DotCraft 本地客户端用来发现和管理工作区 AppServer 的本机协议。它面向 Desktop、CLI、编辑器扩展和其他本地客户端；如果你只想和 Agent 对话，真正的会话流量仍然走 [AppServer Protocol](./appserver-protocol)。

TypeScript、.NET 或 Python 应用应优先使用 [DotCraft SDK Hub API](../sdks/)。它已经实现发现、binary policy、结构化错误和 AppServer 启动流程。只有在实现自定义传输、不受支持的语言或调试协议时，才直接使用本页的 raw HTTP/SSE 契约。

Hub 的职责是本地协调，不是会话代理：

- Hub 通过 HTTP JSON API 管理本机工作区 AppServer。
- Hub 通过 SSE 广播生命周期事件。
- Hub 不暴露 `thread/*`、`turn/*`、`approval/*`、`mcp/*` 等 AppServer JSON-RPC 方法。
- 调用 `appservers/ensure` 后，客户端应直接连接返回的 AppServer WebSocket 端点。

## 适用场景

实现 Hub 客户端适合这些场景：

- 你正在开发 DotCraft 应用、CLI 集成、IDE 扩展或本地 GUI。
- 你希望多个本地客户端共享同一个工作区运行时。
- 你需要显示本机工作区运行状态、托盘菜单或系统通知。
- 你希望按需启动 AppServer，同时避免同一个工作区被重复启动。

如果你的客户端连接的是远程 AppServer，或者你自己显式管理 AppServer 进程，可以跳过 Hub。

## 协议

Hub Local API 在回环地址上使用 HTTP JSON。所有 JSON 字段使用 camelCase。

| 能力 | 说明 |
|------|------|
| 发现 | 读取 `~/.craft/hub/hub.lock` |
| API 传输 | HTTP JSON |
| 事件传输 | Server-Sent Events (`GET /v1/events`) |
| 地址 | 默认绑定回环地址 |
| 认证 | 受保护端点使用 `Authorization: Bearer <token>` |
| 状态检查 | `GET /v1/status` 不需要认证 |

`hub.lock` 的典型内容：

```json
{
  "pid": 12345,
  "apiBaseUrl": "http://127.0.0.1:49231",
  "token": "local-random-token",
  "startedAt": "2026-04-30T06:30:00Z",
  "version": "0.1.0",
  "binaryPath": "/path/to/dotcraft"
}
```

客户端读取锁文件后应同时验证：

1. `pid` 指向的进程仍然存活。
2. `GET {apiBaseUrl}/v1/status` 可访问。
3. 返回的 `apiBaseUrl`、版本、能力和可选 `binaryPath` 符合客户端预期。

如果验证失败，客户端可以删除对该锁文件的信任，并按需启动 `dotcraft hub`。

## 启动流程

![DotCraft Hub bootstrap flow](/hub-bootstrap-flow.svg)

客户端在连接到 AppServer 后，普通会话流量不再经过 Hub。

## 认证

除 `GET /v1/status` 外，所有管理端点都需要 Bearer 令牌：

```http
Authorization: Bearer <token-from-hub-lock>
```

未授权响应：

```json
{
  "error": {
    "code": "unauthorized",
    "message": "缺少 Hub 令牌或令牌无效。",
    "details": null
  }
}
```

Hub 是同一操作系统用户下的本地协调器，不是跨用户安全边界。不要把 Hub API 暴露到非回环网络。

## API 概览

| 端点 | 认证 | 说明 |
|----------|------|------|
| `GET /v1/status` | 否 | 返回 Hub 元数据和能力。 |
| `POST /v1/shutdown` | 是 | 停止 Hub，并触发托管 AppServer 清理。 |
| `POST /v1/appservers/ensure` | 是 | 确保工作区 AppServer 可用，必要时启动。 |
| `GET /v1/appservers` | 是 | 列出运行中和已知的工作区 AppServer。 |
| `GET /v1/appservers/by-workspace?path=...` | 是 | 查询某个工作区，不启动新进程。 |
| `POST /v1/appservers/stop` | 是 | 停止一个 Hub 托管的工作区 AppServer。 |
| `POST /v1/appservers/restart` | 是 | 重启一个工作区 AppServer。 |
| `POST /v1/services/ensure` | 是 | 启动或复用一个已注册的一方本地服务。 |
| `GET /v1/services/by-id?id=...` | 是 | 查询一个已注册的本地服务，但不启动它。 |
| `POST /v1/services/stop` | 是 | 停止一个 Hub 托管的本地服务。 |
| `POST /v1/services/restart` | 是 | 替换一个已注册的本地服务进程。 |
| `GET /v1/events` | 是 | 订阅 Hub 生命周期事件。 |
| `POST /v1/notifications/request` | 是 | 请求本地通知，由 Desktop 或托盘展示。 |

### `GET /v1/status`

响应示例：

```json
{
  "hubVersion": "0.1.0",
  "pid": 12345,
  "startedAt": "2026-04-30T06:30:00Z",
  "statePath": "/Users/me/.craft/hub",
  "apiBaseUrl": "http://127.0.0.1:49231",
  "binaryPath": "/path/to/dotcraft",
  "capabilities": {
    "appServerManagement": true,
    "managedServiceManagement": true,
    "portManagement": true,
    "events": true,
    "notifications": true,
    "tray": false
  }
}
```

`tray: false` 表示 Hub 本身无界面；托盘和系统通知 UI 由 Desktop 负责。

### `POST /v1/appservers/ensure`

请求示例：

```json
{
  "workspacePath": "/Users/me/project",
  "client": {
    "name": "my-client",
    "version": "0.1.0"
  },
  "startIfMissing": true,
  "runtimeTools": {
    "ripgrepPath": "/absolute/path/to/rg"
  }
}
```

响应示例：

```json
{
  "workspacePath": "/Users/me/project",
  "canonicalWorkspacePath": "/Users/me/project",
  "state": "running",
  "pid": 23456,
  "endpoints": {
    "appServerWebSocket": "ws://127.0.0.1:49300/ws?token=..."
  },
  "serviceStatus": {
    "appServerWebSocket": {
      "state": "allocated",
      "url": "ws://127.0.0.1:49300/ws?token=...",
      "reason": null
    },
    "dashboard": {
      "state": "disabled",
      "url": null,
      "reason": "Dashboard 或追踪已禁用。"
    }
  },
  "serverVersion": "0.1.0",
  "startedByHub": true,
  "exitCode": null,
  "lastError": null,
  "recentStderr": null
}
```

重要字段：

- `state`: 取值为 `stopped`、`starting`、`running`、`unhealthy`、`stopping` 或 `exited`。
- `endpoints.appServerWebSocket`: 客户端连接 AppServer Protocol 时应使用的 URL。
- `serviceStatus`: `dashboard` 和运行时辅助服务的状态。
- `startedByHub`: 当前进程是否由此 Hub 管理。

如果 `startIfMissing` 为 `false`，客户端可以查看状态，而不会创建新进程。

`runtimeTools` 是可选的本机运行时提示集合。Desktop 用它把内嵌的 `rg`、TypeScript 模块运行时和内置插件 roots 传给 AppServer。Hub 只把这些值作为 `DOTCRAFT_RG_PATH`、`DOTCRAFT_MODULES_DIR`、`DOTCRAFT_BUILTIN_PLUGIN_ROOTS` 等环境变量传给托管进程，不会在状态响应中回显。

如果 Hub 发现工作区 `appserver.lock` 已经 stale，会删除该锁并继续处理。如果锁指向的 AppServer 仍然存活，且其 `appServerWebSocket` 端点能完成 initialize 握手，Hub 可以返回该端点，并将 `startedByHub` 置为 `false`、相关 `serviceStatus` 标记为 `external`。如果这个 live lock 无法安全复用，Hub 会返回 `workspaceLocked`。

### 停止与重启

停止请求：

```json
{
  "workspacePath": "/Users/me/project"
}
```

重启使用相同的请求体，也可以包含 `runtimeTools`。

### 一方本地服务

托管本地服务是封闭的 DotCraft 产品能力。插件和 Marketplace manifest 不能通过此 API 注册进程。当前构建注册了用户级 `oratorio` 服务。

Ensure 请求：

```json
{
  "serviceId": "oratorio",
  "startIfMissing": true,
  "executable": "/absolute/path/to/oratorio-server"
}
```

宿主负责解析 `executable`。客户端不能提供参数、环境变量、状态目录或健康检查路径。并发 ensure 会复用同一个健康进程。

```json
{
  "serviceId": "oratorio",
  "state": "running",
  "pid": 24567,
  "endpoint": "http://127.0.0.1:49310",
  "accessToken": "ephemeral-service-token",
  "version": "0.5.2",
  "lastError": null,
  "recentStderr": null
}
```

将 `endpoint` 和 `accessToken` 视为仅供宿主使用的凭据。不要把它们交给 Renderer，也不要记录或持久化。服务状态只存在于当前 Hub 生命周期内。Hub 关闭时停止它拥有的进程，但不会在服务失败后自动重启。

停止请求使用 `{ "serviceId": "oratorio" }`。重启还必须提供解析后的 `executable`。

### 通知请求

通知请求：

```json
{
  "workspacePath": "/Users/me/project",
  "kind": "turn.completed",
  "title": "任务完成",
  "body": "Agent 已完成请求的更改。",
  "severity": "success",
  "source": "appserver",
  "threadId": "thread_abc",
  "actionUrl": "dotcraft://workspace/open?path=/Users/me/project&threadId=thread_abc",
  "openDesktopOnClick": true
}
```

响应：

```json
{
  "accepted": true
}
```

`severity` 会被规范化为 `info`、`success`、`warning` 或 `error`。

`threadId`、`actionUrl` 和 `openDesktopOnClick` 是可选字段。AppServer 发送的 turn 通知只有在线程来源为 Desktop 时才会使用 `dotcraft://workspace/open`；其他来源的通知应设置 `openDesktopOnClick: false`，避免点击通知拉起 Desktop。

## 事件

订阅方式：

```http
GET /v1/events
Authorization: Bearer <token>
Accept: text/event-stream
```

Hub 发送标准 SSE 记录：

```text
event: appserver.running
data: {"kind":"appserver.running","at":"2026-04-30T06:31:00Z","workspacePath":"/Users/me/project","data":{"pid":23456,"endpoints":{"appServerWebSocket":"ws://127.0.0.1:49300/ws?token=..."}}}
```

已知事件类型包括：

| 事件 | 说明 |
|-------|------|
| `hub.started` | Hub 启动完成。 |
| `hub.stopping` | Hub 正在停止。 |
| `port.allocated` | Hub 为某个服务分配了本地端口。 |
| `appserver.starting` | 工作区 AppServer 正在启动。 |
| `appserver.running` | 工作区 AppServer 已可用。 |
| `appserver.exited` | 工作区 AppServer 已退出。 |
| `appserver.unhealthy` | 健康检查失败。 |
| `notification.requested` | 有本地通知请求等待 UI 展示。 |

事件负载是扩展点。客户端应根据 `kind` 和已知字段渲染 UI，并忽略未知字段。

## 连接 AppServer

拿到 `endpoints.appServerWebSocket` 后，客户端应打开 WebSocket，并按 AppServer Protocol 进行初始化：

```json
{
  "jsonrpc": "2.0",
  "id": 0,
  "method": "initialize",
  "params": {
    "clientInfo": {
      "name": "my-client",
      "title": "我的客户端",
      "version": "0.1.0"
    },
    "capabilities": {
      "approvalSupport": true,
      "streamingSupport": true
    }
  }
}
```

然后发送：

```json
{
  "jsonrpc": "2.0",
  "method": "initialized",
  "params": {}
}
```

更多会话方法见 [AppServer Protocol](./appserver-protocol)。

## 错误

错误响应统一为：

```json
{
  "error": {
    "code": "workspaceLocked",
    "message": "似乎已有运行中的进程持有工作区 AppServer 锁。",
    "details": {
      "workspacePath": "/Users/me/project",
      "pid": 23456
    }
  }
}
```

常见错误码：

| 错误码 | HTTP | 说明 |
|------|------|------|
| `unauthorized` | 401 | 令牌缺失或不匹配。 |
| `workspaceNotFound` | 400/404 | 工作区路径缺失、不存在，或不是 DotCraft 工作区。 |
| `workspaceLocked` | 409 | 另一个运行中的 AppServer 拥有该工作区锁，且无法安全复用。 |
| `appServerStartFailed` | 500 | 托管 AppServer 启动失败。 |
| `appServerUnhealthy` | 500 | 托管 AppServer 未通过就绪检查或健康检查。 |
| `portUnavailable` | 500 | Hub 无法分配需要的本地端口。 |
| `invalidNotification` | 400 | 通知请求无效。 |
| `managedServiceNotRegistered` | 404 | 当前 DotCraft 构建没有注册该服务 ID。 |
| `managedServiceExecutableRequired` | 400 | 启动或重启需要宿主解析后的 executable。 |
| `managedServiceExecutableNotFound` | 400 | 解析后的 executable 不存在。 |
| `managedServiceStartFailed` | 503 | 服务未通过 ready 或健康检查。 |
| `hubInternalError` | 500 | Hub 遇到未预期的内部错误。 |

## 客户端实现建议

- 默认使用 Hub 管理本地工作区；保留显式远程 AppServer 模式作为高级路径。
- 启动 Hub 后应重读 `hub.lock` 并验证 `/v1/status`，不要假设进程已立即可用。
- 对 `appserver.unhealthy` 和 `appserver.exited` 事件显示可操作状态，例如“重启工作区运行时”。
- 不要把 Hub 令牌或 AppServer 令牌写入日志。
- 对未知端点、服务状态和事件类型保持兼容。

## 相关文档

- [SDK 快速开始](../sdks/quickstart)
- [AppServer Protocol](./appserver-protocol)
- [Hub Local Coordination](../lifecycle/hub)
- [AppServer Mode](../lifecycle/appserver)
