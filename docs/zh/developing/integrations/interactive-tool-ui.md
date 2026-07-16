# MCP Apps

MCP Apps 允许 MCP 工具附带交互式结果视图。MCP server 声明 `ui://` resource，DotCraft Desktop 通过标准 AppBridge contract 在沙箱中渲染它。其他客户端继续使用工具的文本 fallback。

App Binding 不定义独立 UI 协议。App Binding 应用从 binding-scoped Streamable HTTP MCP server 提供工具和视图。

## 声明视图

在 MCP 工具上添加稳定的 UI metadata：

```json
{
  "name": "ListBoardItems",
  "inputSchema": { "type": "object" },
  "_meta": {
    "ui": {
      "resourceUri": "ui://example/board.html",
      "visibility": ["model", "app"]
    }
  }
}
```

使用 `visibility` 将工具发布给模型、应用或两者。DotCraft 遇到无效 metadata 时会把能力视为不可用，不会扩大访问范围。

## 提供 resource

通过标准 MCP `resources/list` 和 `resources/read` 返回 resource。MCP App HTML 使用以下 MIME type：

```text
text/html;profile=mcp-app
```

把脚本和样式随 server 一起打包。在 resource 的 `_meta.ui` 中声明 CSP domain 和浏览器 permission；Desktop 会拒绝 resource 未声明的能力。

## 返回可用的 fallback

每次调用都应返回简洁的 `content`，供模型和非可视客户端使用。将机器可读结果放入 `structuredContent`；result `_meta` 只提供给 MCP App。

```json
{
  "content": [{ "type": "text", "text": "Found 4 board items." }],
  "structuredContent": { "itemIds": ["OR-12", "OR-15"] },
  "_meta": { "selectedItemId": "OR-12" }
}
```

UI 通过官方 `@modelcontextprotocol/ext-apps` client 通信。使用 `tools/call`、打开链接、display mode 请求和 host context 通知等 AppBridge 操作，不要创建私有 postMessage 协议。

## 安全边界

- iframe 使用 opaque origin，不能访问宿主 DOM 或 Node。
- resource CSP 和 permission 是显式且经过 capability 检查的 metadata。
- App 发起的工具调用始终受原 MCP server 和实时 authority 限制。
- 撤销 App Binding 会立即关闭对应 MCP session 和视图。

## 相关文档

- [App Binding](./app-binding)
- [构建应用](./build-an-app)
- [AppServer 协议](../protocols/appserver-protocol)
