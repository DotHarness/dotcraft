# App Binding

App Binding 将一个已安装应用连接到一个 DotCraft 线程。Binding 管理连接和授权；工具与交互 UI 使用 binding-scoped MCP session。

## Binding 授予什么

选择**启用**后，DotCraft 会创建一个十分钟有效的 handoff。应用以自己的 app principal 完成认证，再用 Streamable HTTP MCP endpoint 和一次性 bearer 激活 binding。随后 DotCraft 读取 MCP 工具快照。

- 第一个有效快照由最初的“启用”点击直接批准。
- 收窄的变化会自动接受。
- schema、可见性、风险、UI、CSP、域名或权限扩大时必须再次确认。
- 离线 binding 保留稳定工具 schema，但调用会返回 `AppBindingOffline`。
- 撤销会立即移除该 binding 的 MCP session、调用、view 和模型可见工具。

每个 binding 都使用独立 MCP session 和凭据。远程 endpoint 必须使用 HTTPS；HTTP 仅允许 loopback。DotCraft 重启后 binding 保持离线，直到同一 app principal 使用新 bearer 完成 rebind。

## 社交渠道

会话绑定使用独立的 social binding 方法，但工具是原生插件工具，不是 MCP 工具。DotCraft 在服务端注入已绑定的投递目标。渠道工具不得声明或传入 `target`、`chatId`、`groupId`、`conversationId`、`deliveryTarget` 及其别名。

## 安全边界

认证后的应用连接只能调用 App Binding app-role 方法，不能读取线程、启动 turn、检查 workspace 或控制其他应用。DotCraft 只持久化加盐凭据 verifier 和不含敏感信息的规范化能力快照；principal credential、binding bearer、live MCP client 与 UI resource body 都不会落盘。

实现方式见[构建应用](./build-an-app)和 [AppServer 协议](../protocols/appserver-protocol)。
