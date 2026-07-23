# 构建应用

App Binding 使用 AppServer 管理授权，并使用 binding-scoped Streamable HTTP MCP server 提供工具。

## 连接 app principal

1. 可信 DotCraft 客户端用 `appId` 调用 `app/connection/start`。
2. 应用通过 `app/connection/request/get` 读取 handoff。
3. 应用调用 `app/connection/connect` 并保存返回的 credential；该 credential 只返回一次。
4. 立即在已经 initialized 的 AppServer 连接上调用 `app/connection/authenticate`。
5. 后续连接使用已保存的 credential 认证；通过 `app/connection/refresh` 轮换 credential。

Principal credential 会在 30 天后过期，轮换后旧 credential 立即失效。

## 激活线程 binding

用户通过 `thread/appBindings/enable` 启用应用后，在线 App 从 `app/binding/requested` 获取 `bindingRequestId`；如果 App 离线，则使用可信 client 收到的请求专用 handoff。应用先用 `app/binding/request/get` 检查请求，再调用：

```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "app/binding/activate",
  "params": {
    "bindingRequestId": "appbindreq_...",
    "endpoint": "https://app.example/mcp/binding/123",
    "bearer": "one-time-binding-secret"
  }
}
```

工具由该 MCP server 暴露；交互结果使用稳定 MCP Apps `ui://` resource。App descriptor 只包含产品身份、安装与连接体验、品牌和安全链接信息。

进程重启后，调用 `app/bindings/list`，再用当前 `authorityRevision`、可信 endpoint 与新 bearer 调用 `app/binding/rebind`。`thread/appBindings/confirmCapabilities` 只能由可信 DotCraft 客户端调用。

## Endpoint 规则

- 只允许 Streamable HTTP。
- 只允许远程 HTTPS 或 loopback HTTP。
- 不接受 command、arguments、environment、working directory 或 stdio 配置。
- redirect 或信任边界变化后必须重新激活。

用户操作流程见 [Connected Apps](../../features/agent-system/connected-apps)，协议模型见 [App Binding](./app-binding)。

## 相关文档

- [Connected Apps](../../features/agent-system/connected-apps)
- [App Binding](./app-binding)
- [AppServer 协议](../protocols/appserver-protocol)
- [MCP Apps](./mcp-apps)
