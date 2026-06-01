# @dotcraft/sdk (TypeScript)

DotCraft TypeScript SDK 面向三类使用者：

- 应用开发者：通过 `DotCraft.local()` / `DotCraft.remote()` 连接 DotCraft 并运行线程。
- 协议客户端：通过 `@dotcraft/sdk/wire` 直接访问 AppServer JSON-RPC。
- 渠道作者：通过 `@dotcraft/sdk/channel` 构建外部社交渠道模块。

TypeScript SDK 遵循共享的 AppServer 与 Hub 模型，并提供 TypeScript 专属包和 helper。Python SDK 的 `dotcraft_wire` 包名保持不变。

## 包与入口

```typescript
import { DotCraft, textPart, skillRefPart } from "@dotcraft/sdk";
import { DotCraftWireClient, WebSocketTransport } from "@dotcraft/sdk/wire";
import { HubClient } from "@dotcraft/sdk/hub";
import { ModuleChannelAdapter } from "@dotcraft/sdk/channel";
import { runModuleConformanceSuite } from "@dotcraft/sdk/testing";
```

TypeScript 一方渠道包：

- `@dotcraft/channel-feishu`
- `@dotcraft/channel-weixin`
- `@dotcraft/channel-telegram`
- `@dotcraft/channel-qq`
- `@dotcraft/channel-wecom`

## 安装

```bash
cd sdk/typescript
npm install
npm run build
```

仓库内其它包依赖：

```json
{
  "dependencies": {
    "@dotcraft/sdk": "*"
  }
}
```

## 本地 Hub 快速开始

`DotCraft.local()` 会发现或启动本机 Hub，调用 Hub ensure 获取工作区 AppServer WebSocket，然后完成 `initialize` / `initialized` 握手。

```typescript
import { DotCraft } from "@dotcraft/sdk";

const dotcraft = await DotCraft.local({
  workspacePath: "E:/Git/dotcraft",
  approvalHandler: async (request) => {
    console.log("approval requested", request);
    return "decline";
  },
});

const thread = await dotcraft.threads.getOrCreate({ userId: "me" });
const result = await thread.run("Summarize this workspace.");

console.log(result.text);
await dotcraft.close();
```

生产应用应始终提供显式 `approvalHandler`。未提供时 SDK 会按兼容策略默认返回 `accept`，适合测试与非交互脚本，但不适合作为生产审批策略。

## 远程 WebSocket

```typescript
import { DotCraft } from "@dotcraft/sdk";

const dotcraft = await DotCraft.remote({
  url: "ws://127.0.0.1:9100/ws",
  token: "",
});
```

如果 `url` 已含 `token` 查询参数，SDK 不会重复追加。

## 线程与运行

```typescript
const thread = await dotcraft.threads.start({
  userId: "alice",
  displayName: "Build review",
});

for await (const event of thread.runStreamed("Review the current diff.")) {
  if (event.type === "agent_message_delta") process.stdout.write(event.delta ?? "");
  if (event.type === "completed") console.log(event.result?.text);
}
```

`run()` 返回合并后的最终文本；`runStreamed()` 返回结构化事件并保留原始 JSON-RPC 消息。SDK 不会自动解析 `/command`、`$skill` 或 `@file` 文本；调用方应显式使用 `commandRefPart()`、`skillRefPart()`、`fileRefPart()` 或 raw command API。共享 SDK 事件拓扑见 [SDK](./#事件拓扑)。

## 动态工具

Runtime dynamic tools 使用“声明即绑定”：每个工具同时包含发给 AppServer 的 descriptor 和本地 `handler`。SDK 会在 `thread/start` 或 `thread/resume` 前注册 handler，发给服务器时剥离 handler。

```typescript
const thread = await dotcraft.threads.start({
  userId: "alice",
  dynamicTools: [
    {
      namespace: "local",
      name: "Echo",
      description: "Echo input.",
      inputSchema: { type: "object" },
      handler: async (request) => ({
        success: true,
        contentItems: [{ type: "text", text: JSON.stringify(request.arguments) }],
      }),
    },
  ],
});
```

当 deferred tools 需要模型先搜索相关工具时，可以配合 `additionalContext` 放一条简短提示。该内容会作为 app context 渲染，不是更高优先级的指令。

```typescript
const thread = await dotcraft.threads.start({
  userId: "alice",
  dynamicTools: [
    {
      namespace: "issues",
      name: "ListIssues",
      description: "List issues from the connected issue tracker.",
      inputSchema: { type: "object" },
      deferLoading: true,
      handler: async () => ({
        success: true,
        contentItems: [{ type: "text", text: "No open issues." }],
      }),
    },
  ],
  additionalContext: {
    "issues.threadGuidance": {
      kind: "application",
      value: "When the user asks about issues, search for the relevant issues tool first.",
    },
  },
});

await dotcraft.threads.resume(thread.id, { additionalContext: {} });
```

使用该字段前先检查 `capabilities.runtimeAdditionalContext`。恢复线程时，省略 `additionalContext` 会保留当前 runtime context；`{}` 会清空它。

## Raw Wire API

```typescript
import { DotCraftWireClient, WebSocketTransport } from "@dotcraft/sdk/wire";

const client = new DotCraftWireClient(new WebSocketTransport({ url: "ws://127.0.0.1:9100/ws" }));
await client.connect();
await client.initialize({ clientName: "raw-client", clientVersion: "0.1.0" });

const threads = await client.threadList({ channelName: "sdk", userId: "me" });
const raw = await client.request("thread/read", { threadId: threads[0]?.id });
```

## 渠道模块

模块契约与宿主集成见 [TypeScript Module 集成](../integrations/typescript-module)。一方渠道包都依赖 `@dotcraft/sdk`，并从子路径导入：

```typescript
import { textPart } from "@dotcraft/sdk";
import { WebSocketTransport } from "@dotcraft/sdk/wire";
import { ModuleChannelAdapter } from "@dotcraft/sdk/channel";
```

`@dotcraft/sdk/channel` 还导出稳定的 channel runtime 组件：`ThreadResolver`、`ChannelMessageQueue`、`CommandRouter`、`TurnStreamReducer`、`DeliveryDispatcher`、`ChannelToolDispatcher`、`ApprovalDispatcher`、`ModuleConfigLoader` 和 `ModuleLifecycleState`。一方渠道包默认通过 `ChannelAdapter` 组合使用这些组件；新渠道只需要在需要自定义线程解析、命令路由、分段流处理或模块生命周期时直接使用它们。

## 调试

- 在 `ChannelAdapter` 构造选项中传入 `debugStream: true`，日志前缀为 `[dotcraft-sdk:adapter-stream]`。
- 调用 `configureTextMergeDebug(true)`，文本合并日志前缀为 `[dotcraft-sdk:text-merge]`。

## 验证

```bash
cd sdk/typescript
npm run typecheck:all
npm run test:all
npm run pack:verify
```

MIT
