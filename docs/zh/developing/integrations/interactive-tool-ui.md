# 交互式工具 UI

App Binding 工具可以为它的结果提供**富交互 UI**。工具声明一个 `ui://` HTML 资源；DotCraft **桌面端**会把它渲染在一个沙箱 iframe 中，并在 UI 与宿主之间运行一个小型的 postMessage JSON-RPC 桥。这套模型采用 **MCP Apps**，并绑定到 App Binding 的权限体系：iframe 是隔离的，每个由 UI 发起的动作都会按线程绑定（scope、风险、审批）重新校验并写入审计。

交互式 UI **仅在桌面端**渲染。未协商 `interactiveToolUi` capability 的客户端（包括聊天渠道）会回退到工具的文本结果——UI 始终是增强，绝不是正确性的前提。

> [!NOTE]
> 示例以 **Oratorio** 为例。请把命名空间、工具名和 `ui://` 路径替换成你自己应用的。

## 何时使用

当工具结果是用户需要*操作*的东西时——一块可浏览点击的看板、一个可查看并操作的条目、一个可刷新的状态——而不仅仅是模型转述的文本，就适合用交互式 UI。纯信息型结果保持文本即可。

## 给工具加 UI——四步

### 1. 在工具上声明 `_meta.ui`

把工具描述符的 `_meta.ui` 指向一个 `ui://` 资源。这是**面向客户端**的元数据——它绝不会进入模型可见的工具描述。

```json
{
  "name": "ListBoardItems",
  "_meta": {
    "ui": {
      "resourceUri": "ui://oratorio/board.html",
      "visibility": ["model", "app"],
      "prefersBorder": true,
      "csp": { "connectDomains": ["https://127.0.0.1:7777"] },
      "permissions": []
    }
  }
}
```

| `_meta.ui` 字段 | 含义 |
|---|---|
| `resourceUri` | 要渲染的 `ui://` 文档。改 URI 就是缓存失效 / 版本切换的手段。 |
| `visibility` | 谁可以调用该工具——`["model","app"]`（默认）、`["app"]`（仅 UI 可调、对模型隐藏）或 `["model"]`。 |
| `csp` | iframe 的网络/资源白名单：`connectDomains`、`resourceDomains`、`frameDomains`。默认禁止联网。 |
| `permissions` | iframe 可用的高权限特性：`camera`、`microphone`、`geolocation`、`clipboardWrite`。默认全部拒绝。 |
| `prefersBorder` | 宿主卡片外框带边框渲染。 |

### 2. 提供 `ui://` 资源

把 HTML 随应用打包，并在 AppServer 客户端上注册该文件夹：

```csharp
client.ServeStaticUiResources("ui://oratorio", Path.Combine(AppContext.BaseDirectory, "UiResources"));
```

文件夹下的每个文件都会被提供（`ui://oratorio/board.html`、`…/item.html` ……），按扩展名给出正确 MIME，并按需读取——所以开发期改动会即时生效。

### 3. 用正确的受众返回结果

一个工具结果带有三份**受众不同**的载荷：

| 字段 | 受众 | 用途 |
|---|---|---|
| `contentItems` | 仅模型 | 模型阅读并转述的文本/图像；也是非桌面端的文本回退。 |
| `structuredResult` | 模型 + UI | UI 渲染、模型也可读取的精简 JSON（用于后续操作的 id）。保持小。 |
| `_meta` | 仅 UI | 给 UI 的更大或更敏感的展示数据。**绝不到达模型。** |

**始终填好文本结果**（`contentItems` / `structuredResult`），让非桌面端和模型都能正常工作——UI 只是增强。

### 4. 在 HTML 里说桥协议

你的文档通过 `window.parent` postMessage（JSON-RPC 2.0）与宿主通信。先发 `ui/initialize`，再处理推送来的 `tool-result`：

```js
parent.postMessage({ jsonrpc: "2.0", id: 1, method: "ui/initialize", params: {} }, "*");

window.addEventListener("message", (event) => {
  const m = event.data;
  if (m.method === "ui/notifications/tool-result") render(m.params.structuredContent);
  if (m.method === "ui/notifications/host-context-changed") applyTheme(m.params.theme);
});
```

## 桥协议

宿主会推送 `tool-input`、`tool-result` 和 `host-context-changed`（主题/语言/显示模式）；你的 UI 可以发送：

| 请求 | 用途 |
|---|---|
| `tools/call` | 调用一个 app 绑定的工具（刷新、变更）。按绑定鉴权、与对话解耦、写审计；结果只回给 UI。 |
| `ui/open-link` | 打开 `https:` / `mailto:` 或你应用声明的深链协议。 |
| `ui/message` | 添加一条可见的用户消息并发起一次模型轮（带限流）。 |
| `ui/update-model-context` | 把 UI 状态喂给模型的下一轮（静默、后写覆盖）。 |
| `ui/request-display-mode` | 申请 `inline` / `pip` / `fullscreen`（由宿主裁决）。 |

按钮也可以直接 `fetch` 你自己的本地回环后端——由 `_meta.ui.csp.connectDomains` 放行——而不必调用工具。并非每个动作都是一次工具调用。

## 安全与协商

- **能力协商：** 只有声明了 `interactiveToolUi` 能力的客户端（桌面端）才会收到 `ui://` 资源并能驱动 `ui/*` 桥；其它客户端只拿到文本。
- **沙箱：** iframe 以 `sandbox="allow-scripts"` 运行且无同源访问——opaque 源、无宿主 DOM、无 Node。
- **CSP：** 严格的每资源 CSP，**仅**由经服务端校验的 `_meta.ui.csp` 放宽。`script-src` 永不放宽——外部脚本始终被拦。
- **权限：** iframe **只**获得你在 `_meta.ui.permissions` 中声明的高权限特性，其余一律拒绝。
- **链接：** `ui/open-link` 遵循宿主自有的 scheme 策略（`https:`/`mailto:` + 你应用声明的协议）；其它 scheme 一律拒绝并写审计。
- **工具调用：** UI 的 `tools/call` 会按绑定 scope 和工具可见性重新校验；变更类工具触发正常审批；跨绑定调用被拒。每次调用都写审计。

## 回环 CORS

如果你的 UI 直接 `fetch` 自己的后端（数据通路 B），该后端必须允许 iframe 的 opaque 源：返回 `Access-Control-Allow-Origin: *`、不带凭据、仅限回环。

另见：[App Binding](./app-binding)、[构建应用](./build-an-app)、[AppServer 协议](../protocols/appserver-protocol)。
