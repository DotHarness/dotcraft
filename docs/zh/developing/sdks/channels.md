# 渠道适配器

渠道适配器把外部消息平台作为一等渠道接入 DotCraft。它为每个用户解析出 thread，在上面跑 turn，再把回复投递回平台。

> [!NOTE]
> 渠道适配器是语言特定的 profile，只有 **TypeScript** 提供。.NET SDK 没有渠道适配器。

要用内置的 Channel 策略，继承 adapter 基类即可。基类负责按身份分队的消息队列、thread 解析、斜杠命令路由、turn 流归并、服务端请求 handler 和心跳。

![一条平台消息在渠道适配器里的一轮：按身份排队、解析 thread 与斜杠命令路由、在服务端跑一轮 turn、把 turn 流规约成一条回复并投递回同一个会话](/channel-adapter-loop.svg)

## 最小适配器

```ts
import { ChannelAdapter } from "@dotcraft/channel";

class MyChannel extends ChannelAdapter {
  async onDeliver(target: string, content: string, _metadata: Record<string, unknown>): Promise<boolean> {
    await platform.send(target, content);
    return true;
  }

  async onApprovalRequest(request: Record<string, unknown>): Promise<string> {
    return await platform.requestApproval(request);
  }

  protected async onSegmentCompleted(
    _threadId: string,
    _turnId: string,
    content: string,
    _isFinal: boolean,
    target: string,
  ): Promise<boolean> {
    return await this.onDeliver(target, content, {});
  }
}
```

## 生命周期与恢复

- 接收平台事件前调用 `start()`。它连接 Wire client、注册 Channel handler，然后在 `initialize` 时声明 Channel 能力。关闭时调用 `stop()`。
- 用 `handleMessage` 转发每个事件。该调用只表示事件已进入内存队列，不表示 turn 或平台投递已经完成。
- 队列身份由 user id 与 channel context 共同确定。同一身份的消息串行执行，不同身份可以并发。适配器已知 thread 时，斜杠命令可以绕过队列，让 stop 一类命令能影响正在运行的 turn。
- 适配器会恢复 paused thread，替换过期或 inactive 的 thread，并在替代 thread 上重试。服务端报告已有 turn 在运行时，输入会重新排队。
- Wire client 会重连并重新执行初始化，但不会持久化或重放平台事件与已经发出的投递调用。保持平台接收器在线，按需在平台侧做去重或重试。重连不是投递恢复机制。

## Handler 规则

| Hook | 契约 |
|------|------|
| `onDeliver` | 必须实现。把纯文本投递到平台目标并报告是否成功。默认结构化投递 handler 会把文本消息委托给它。 |
| `onApprovalRequest` | 必须实现。返回有效审批决定。hook 抛出异常时，adapter 会回答 `cancel`。 |
| `onSend` | 可选。需要结构化投递时覆盖它，并用 `getDeliveryCapabilities` 声明与实现一致的能力。默认实现接受文本，其他类型返回 `UnsupportedDeliveryKind`。 |
| `getChannelTools` + `onToolCall` | 可选。只声明 call hook 已实现的工具。默认 call hook 返回 `UnsupportedTool`。 |
| `onReplyProgress` | 可选的观察 hook，在 turn 运行期间接收有序的 AgentMessage 文本。它不会把文本标记为已投递。平台更新自行合并限流，投递回退仍由 `onSegmentCompleted` 或 `onTurnCompleted` 负责。 |
| `onTurnCompleted`、`onTurnFailed`、`onTurnCancelled`、`onSegmentCompleted` | 按需覆盖，用于平台格式化、渐进投递以及失败或取消通知。 |

适配器还通过 `onUserInputRequest` 处理用户输入请求，默认返回空答案集。heartbeat 响应由基类自行注册，平台子类不需要实现。

渐进投递时，`onSegmentCompleted` 在未成功投递时必须返回 `false`。其他返回值都算作已投递，默认的 `onTurnCompleted` 随后不会再发一次完整回复。

## 包

TypeScript Channel authoring API 由 private `@dotcraft/channel` 包提供。Adapter 和 module authoring API 从根入口导入，队列与路由从 `/runtime` 导入，媒体 helper 从 `/media` 导入，conformance helper 从 `/testing` 导入，Channel contract 元数据从 `/meta` 导入。把写好的模块挂进 Desktop 或自己的宿主进程，见[渠道模块集成](../integrations/typescript-module)。

DotCraft 还基于同一套基类为各内置渠道提供托管 TypeScript 模块。它们都依赖 `@dotcraft/channel`，后者再依赖 `@dotcraft/sdk`。安装与配置见[渠道配置参考](../../features/channels/reference)。

## 相关文档

- [AppServer 协议](../protocols/appserver-protocol)——适配器之下的 JSON-RPC 契约。
- [TypeScript 参考](./typescript)——适配器的完整签名。
