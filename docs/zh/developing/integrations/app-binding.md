# App Binding

本页面面向 App 集成开发者和 client 开发者。App Binding 将工作区 App 连接与每个 thread 使用 App tools 的授权分开。

Desktop 操作流程见 [Connected Apps](../../features/agent-system/connected-apps)。

## 连接与 binding

| 范围 | 用途 | 控制面 |
|---|---|---|
| **App connection** | 在工作区中认证一个 App principal | `app/connection/*` |
| **Thread binding** | 授权一个 thread 使用该 App | `thread/appBindings/*`、`app/binding/*` |

一个 App 可以有一份工作区连接和多份 thread bindings。在一个 thread 中关闭 App 只撤销对应 binding；断开 App principal 会撤销该连接拥有的全部 bindings。

## 连接 App principal

1. 可信 client 使用 App ID 调用 `app/connection/start`。
2. App 通过 `app/connection/request/get` 读取 handoff。
3. App 调用 `app/connection/connect`。
4. Server 只返回一次 principal credential。
5. App 立即在已经 initialized 的 AppServer 连接上调用 `app/connection/authenticate`。
6. 后续连接使用已保存的 credential 认证；通过 `app/connection/refresh` 轮换 credential。

Principal credential 会在 30 天后过期。轮换后，旧 credential 立即失效。

`app/connection/revoke` 会移除工作区连接，并撤销它拥有的全部 thread bindings。

## 激活 thread binding

可信 client 通过 `thread/appBindings/enable` 发起 binding。Server 会创建一个十分钟有效的 binding request。

App principal 在线时，会收到带 `bindingRequestId` 的 `app/binding/requested`。App 离线时，可信 client 会从 `thread/appBindings/enable` 获得请求专用 handoff，并把它交给 App。完成认证的 App 先通过 `app/binding/request/get` 读取请求，然后激活：

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

Endpoint 必须提供 Streamable HTTP MCP server。DotCraft 会创建 binding-scoped MCP session，并在 binding ready 前读取 tool snapshot。

`thread/appBindings/revoke` 只移除一个 thread 的 binding，不会断开 App principal。

### 新 thread 选择

Client 可以在创建 thread 前暂存 App 选择。完成 `thread/start` 后，先启用选中的 Apps 并等待 ready，再提交第一个 turn。

## 能力变化

第一个有效 tool snapshot 由最初的启用操作批准。

- 收窄能力范围的变化会自动接受。
- 扩大的 tool schema、可见性、风险、UI、CSP、domain 或 permission authority 需要再次确认。
- 可信 client 通过 `thread/appBindings/confirmCapabilities` 保留之前批准的基线，或接受新增能力。

接受扩展后，新能力会成为有效基线。保留原有基线会拒绝扩展、移除 live MCP session，并让 binding 保持 offline，直到 App 使用兼容的能力集重新 rebind。

## 离线与 rebind

离线 binding 会保留稳定的 tool schemas，但调用会返回 `AppBindingOffline`。

进程重启后，完成认证的 App 先调用 `app/bindings/list`，再通过 `app/binding/rebind` 提交：

- 当前 `authorityRevision`；
- 可信 endpoint；
- 新 bearer。

每个 binding 都有自己的 MCP session 与 bearer。Live MCP clients 和 binding bearer 不会持久化。

## Endpoint 规则

- 只接受 Streamable HTTP endpoint。
- 远程 endpoint 必须使用 HTTPS。
- Loopback endpoint 可以使用 HTTP。
- App Binding 不接受 command、arguments、environment、working directory 或 stdio 配置。
- Redirect 或信任边界变化后必须重新激活。

## 社交渠道

社交会话 binding 使用 social binding 方法和原生 plugin tools，不使用 MCP tools。DotCraft 在 server 端注入已绑定的投递目标。

Channel tools 不得声明 `target`、`chatId`、`groupId`、`conversationId`、`deliveryTarget` 或这些字段的别名。

## 安全边界

完成认证的 App connection 只能调用 App Binding app-role 方法。它不能读取 threads、启动 turns、检查 workspace 或控制其他 App。

DotCraft 会持久化加盐 credential verifiers 与不含敏感信息的规范化 capability snapshots，不会持久化 principal credentials、binding bearers、live MCP clients 或 UI resource bodies。

## 相关文档

- [Connected Apps](../../features/agent-system/connected-apps)
- [构建 App](./build-an-app)
- [MCP Apps](./mcp-apps)
- [AppServer 协议](../protocols/appserver-protocol)
- [安全与沙箱](../../features/self-hosted/security)
