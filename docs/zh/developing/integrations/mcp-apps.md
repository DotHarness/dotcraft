# MCP Apps

MCP Apps 允许 MCP 工具附带交互式结果视图。MCP server 声明 `ui://` resource，DotCraft Desktop 通过标准 AppBridge contract 在沙箱中渲染它。其他客户端继续使用工具的文本 fallback。

[App Binding](./app-binding) 不定义独立 UI 协议。App Binding 应用从自己的 binding-scoped Streamable HTTP MCP server 提供工具和视图。

![MCP App 在 DotCraft 对话中渲染交互式 GitHub Issue 表单](https://github.com/DotHarness/resources/raw/master/dotcraft/whats-new/mcp-apps.gif)

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

使用 `visibility` 将工具发布给模型、应用或两者，省略时两者都发布。DotCraft 遇到无效 metadata 时会把能力视为不可用，不会扩大访问范围。

## 提供 resource

通过标准 MCP `resources/list` 和 `resources/read` 返回 resource。MCP App HTML 使用以下 MIME type：

```text
text/html;profile=mcp-app
```

把脚本和样式随 server 一起打包。Desktop 会移除 HTML 里的 Content-Security-Policy meta 标签，换成自己的策略：从 `default-src 'none'` 起步，只放行 resource 在 `_meta.ui.csp` 中声明的 HTTPS origin。`_meta.ui` 里的浏览器 permission 与 `domain` 只作为能力元数据记录，Desktop 一律不授予，视图拿不到摄像头、麦克风、定位或剪贴板写入权限。

Resource 正文和送进视图的每个结果都以 2 MB 为上限。

## 返回可用的 fallback

每次调用都应返回简洁的 `content`，供模型和非可视客户端使用。将机器可读结果放入 `structuredContent`，result `_meta` 只提供给 MCP App。

```json
{
  "content": [{ "type": "text", "text": "Found 4 board items." }],
  "structuredContent": { "itemIds": ["OR-12", "OR-15"] },
  "_meta": { "selectedItemId": "OR-12" }
}
```

UI 通过官方 `@modelcontextprotocol/ext-apps` client 通信。使用 `tools/call`、打开链接、display mode 请求和 host context 通知等 AppBridge 操作，不要创建私有 postMessage 协议。

## 安全边界

![MCP server 随工具声明视图 resource，DotCraft Desktop 把它渲染进沙箱（opaque origin、只放行已声明的 CSP origin），视图发起的调用只回到同一个 server，非可视客户端保留文本结果](/mcp-apps-boundary.svg)

- iframe 使用 opaque origin，不能访问宿主 DOM 或 Node。
- 宿主 CSP 从 `default-src 'none'` 起步，只按 resource 的声明放行指定 HTTPS origin。
- 无论 resource 声明什么，浏览器 permission 都不会被授予。
- App 发起的工具调用会重新对照当前 tool snapshot 校验：必须来自同一个 MCP server，并且仍然声明 app 可见性。
- 撤销 App Binding 会立即关闭对应 MCP session 和视图。
