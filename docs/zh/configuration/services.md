# 入口与服务

ACP、Dashboard 和 AppServer 的开关与监听地址，以及运行日志。AppServer 在运行中的行为见 [AppServer 模式](../contribute/lifecycle/appserver)，渠道注册见[渠道](./channels)。

## 入口

| 配置项 | 说明 | 默认值 |
|--------|------|--------|
| `Acp.Enabled` | 是否启用 ACP 模式 | `false` |
| `DashBoard.Enabled` | 是否启用 Dashboard | `true` |
| `DashBoard.Host` | Dashboard 监听地址 | `127.0.0.1` |
| `DashBoard.Port` | Dashboard 监听端口 | `8080` |
| `AppServer.Mode` | AppServer 传输模式：`Disabled`、`Stdio`、`WebSocket` 或 `StdioAndWebSocket` | `Disabled` |
| `AppServer.WebSocket.Host` | WebSocket 监听地址 | `127.0.0.1` |
| `AppServer.WebSocket.Port` | WebSocket 监听端口 | `9100` |
| `AppServer.WebSocket.Token` | 远程 WebSocket 客户端需要使用的 token | 空 |

Dashboard 示例：

```json
{
  "DashBoard": {
    "Enabled": true,
    "Host": "127.0.0.1",
    "Port": 8080
  }
}
```

## 运行日志

DotCraft 将工作区宿主的诊断信息写入 `<workspace>/.craft/logs`，将 Hub 的诊断
信息写入 `~/.craft/logs`。

| 字段 | 说明 | 默认值 |
|---|---|---|
| **`Logging.Enabled`** | 将运行诊断信息写入滚动日志文件 | `true` |
| **`Logging.Console`** | 同时将诊断信息写入控制台。协议宿主使用 stderr，确保 stdout 只承载协议数据 | `false` |
| **`Logging.MinLevel`** | 最低日志级别：`Trace`、`Debug`、`Information`、`Warning`、`Error` 或 `Critical` | `Information` |
| **`Logging.Directory`** | 相对于宿主 `.craft` 目录的日志目录 | `logs` |
| **`Logging.RetentionDays`** | 启动时删除更早的滚动日志，`0` 表示不清理 | `7` |
| **`DebugMode`** | 控制台不截断工具调用参数输出 | `false` |

```json
{
  "Logging": {
    "Enabled": true,
    "Console": false,
    "MinLevel": "Information",
    "Directory": "logs",
    "RetentionDays": 7
  }
}
```

运行日志包含时间、级别、进程 ID、类别、消息、异常和当前诊断 scope。原始 ACP 流量与按需启用的
session stream debug 记录使用独立文件，因为它们可能包含敏感内容或大量数据。
