# TypeScript SDK 参考

`@dotcraft/sdk` 提供生成契约、纯 JSON-RPC client、高层 Thread 与 Run API、Hub 管理和宿主配置。安装和首次运行请从[快速开始](./quickstart)开始。

## 包信息

| | |
|---|---|
| 包 | `@dotcraft/sdk`（源码预览） |
| 模块格式 | ESM（`"type": "module"`） |
| 运行时基线 | Node.js 20+ |
| 协议元数据 | `@dotcraft/sdk/meta` 导出的 `SDK_VERSION`、`CONTRACT_VERSION`、`APPSERVER_PROTOCOL_VERSION` 和 `CONTRACT_SHA256` |

该包目前尚未发布到 npm。请按照[快速开始](./quickstart)从仓库构建，并安装本地目录。

## 入口点

| 入口点 | 用途 |
|--------|------|
| `@dotcraft/sdk/contracts` | 生成的 DTO、方法映射、联合类型和协议元数据；不依赖 Node.js、WebSocket 或运行时 I/O。 |
| `@dotcraft/sdk/wire` | `DotCraftWireClient`、JSON-RPC 传输、生命周期状态，以及 typed/raw 协议 API。 |
| `@dotcraft/sdk` | `DotCraft`、`DotCraftThread`、Run API、回调、输入 helper、approval 常量和高层错误。 |
| `@dotcraft/sdk/hub` | Hub 发现、管理、进程启动、结构化错误和事件流。 |
| `@dotcraft/sdk/app-binding` | App Binding handoff helper 和生成的 App Binding Contracts。 |
| `@dotcraft/sdk/dynamic-tools` | Runtime Dynamic Tool authoring API。 |
| `@dotcraft/sdk/testing` | SDK Transport 测试辅助工具。 |
| `@dotcraft/sdk/meta` | SDK、contract、protocol 和 contract hash 元数据。 |

DotCraft Desktop 是该 SDK 的第一个完整生产宿主消费者。Electron Renderer 可以安全导入 `@dotcraft/sdk/contracts`；运行时入口应放在 Node.js 或 Electron Main，Renderer 不应直接创建 AppServer 或 Hub 连接。

## Wire API

已知协议方法会通过生成的方法映射进行类型检查：

```ts
const wire = new DotCraftWireClient(transport, options);

const result = await wire.request("thread/list", params);
await wire.notify("initialized", {});
const dispose = wire.on("thread/started", (params) => {
  console.log(params.thread.id);
});
```

只有第三方扩展或尚未进入目录的方法才使用显式命名的 raw API：

```ts
const value = await wire.requestRaw("ext/example/read", { id: "42" });
await wire.notifyRaw("ext/example/changed", { id: "42" });
const dispose = wire.onRaw("ext/example/event", (params) => console.log(params));
```

`DotCraft` 是应用入口，提供面向应用的 Thread 与 Run 模型，并通过生成的 operation 调用已收录的 AppServer 方法。

## 连接生命周期

Wire Client 会报告 `connecting`、`initializing`、`ready`、`disconnected`、`reconnecting`、`reconnectError` 和 `closed`。

- Raw Wire 连接默认不重连；高层和 Channel 配置会显式启用重连。
- 默认 RPC 超时为 30 秒，包含在重连队列中的等待时间。
- 重连使用 1 到 30 秒的指数退避和抖动，最多按调用顺序排队 1024 个新请求。
- 断线时进行中的请求会失败且不会重放。新传输完成初始化后才释放排队请求。
- Handler 注册会跨重连保留；thread subscription、活动 Run 和运行时动态工具资源不会自动重建。

## Hub API

Hub Client 可以读取 lock/default chat、查询或确保活动 Hub、解析工作区 AppServer、ensure/restart/stop/list AppServer、读取状态和事件，以及关闭 Hub。

Hub 错误保留结构化 `code`、`message` 和 `details`。进程启动接受显式 executable 和 binary mismatch policy：

- `ignore`
- `restartIfMismatch`
- `errorIfMismatch`

未提供 expected executable 时，默认策略是 `ignore`。

## 高层导出

主入口导出 `DotCraft`、`DotCraftThread`、`DotCraftRunResult`、`DotCraftRunEvent`、高层强类型错误、输入构造器和 approval 决策。Contracts、Wire、Hub、App Binding、Runtime Dynamic Tools、测试工具和元数据分别从专用入口导入。

## Channel 模块

Channel authoring 和运行时 API 位于 private `@dotcraft/channel` package，入口包括根入口以及 `/runtime`、`/media`、`/testing` 和 `/meta`。一方模块依赖该 package：`@dotcraft/channel-feishu`、`@dotcraft/channel-weixin`、`@dotcraft/channel-telegram`、`@dotcraft/channel-qq` 和 `@dotcraft/channel-wecom`。参见 [Channel Adapter](./channels)。

## 验证

```bash
cd sdk/typescript
npm run build
npm run typecheck:all
npm run test:all
```

## 相关文档

- [快速开始](./quickstart)
- [Thread 与 Run](./runs)
- [工具与审批](./tools)
- [Channel Adapter](./channels)
- [AppServer 协议](../protocols/appserver-protocol)
