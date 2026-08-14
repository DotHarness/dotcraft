# 渠道适配器

渠道适配器把外部消息平台（Telegram、飞书、QQ 等）作为一等渠道桥接到 DotCraft。适配器为每个用户解析线程、运行轮次，并把回复投递回平台。

> [!NOTE]
> Channel Adapter 是语言特定的 profile，**TypeScript 与 Python** 提供。.NET SDK 不提供 Channel Adapter。

需要内置 Channel 策略时，请继承 adapter 基类。它负责按身份的消息队列、thread 解析、斜杠命令路由、turn 流归并、服务端请求 handler 和心跳。

## 最小适配器

::: code-group

```ts [TypeScript]
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

```python [Python]
from dotcraft.channel import ChannelAdapter
from dotcraft.wire import StdioTransport

class MyChannel(ChannelAdapter):
    def __init__(self, client_version: str):
        super().__init__(
            transport=StdioTransport(),
            channel_name="my-channel",
            client_name="my-adapter",
            client_version=client_version,
        )

    async def on_deliver(self, target: str, content: str, metadata: dict) -> bool:
        await platform_send(target, content)
        return True

    async def on_approval_request(self, request: dict) -> str:
        return await platform_request_approval(request)
```

:::

## 生命周期与恢复

- 接收平台事件前调用 `start()`。它会连接 Wire client、注册 Channel handler，然后在 `initialize` 时声明 Channel 能力。关闭时调用 `stop()`；Python 还会取消按身份运行的 worker task。
- 用 `handleMessage` / `handle_message` 转发每个事件。该调用只表示事件已进入内存队列，不表示 turn 或平台投递已经完成。
- 队列身份由 user id 与 channel context 共同确定。同一身份的消息串行执行，不同身份可以并发。适配器已知 thread 时，斜杠命令可以绕过队列，使 stop 等命令能够影响正在运行的 turn。
- 适配器会恢复 paused thread、替换过期或 inactive thread 并在替代 thread 上重试；服务端报告已有 turn 在运行时，输入会重新排队。
- Wire client 会重连并重新执行初始化，但不会持久化或重放外部平台事件或已经完成的投递调用。请保持平台接收器运行，并按需在平台侧实现去重或重试；不要把重连当作投递恢复。

## Handler 规则

| Hook | 契约 |
|------|------|
| `onDeliver` / `on_deliver` | 必须实现。把纯文本投递到平台目标并报告是否成功。默认结构化投递 handler 会把文本消息委托给它。 |
| `onApprovalRequest` / `on_approval_request` | 必须实现。返回有效审批决定；hook 抛出异常时，adapter 会回答 `cancel`。 |
| `onSend` / `on_send` | 可选。需要结构化投递时覆盖，并声明与实现一致的 delivery capability。默认实现接受文本，其他类型返回 `UnsupportedDeliveryKind`。 |
| `getChannelTools` + `onToolCall` / `get_channel_tools` + `on_tool_call` | 可选。只声明 call hook 已实现的工具；默认 call hook 返回 `UnsupportedTool`。 |
| `onReplyProgress` | 仅 TypeScript 提供的可选观察 hook，在 Turn 运行期间接收有序的 AgentMessage 文本。它不会把文本标记为已投递；平台更新应合并限流，投递回退仍由 `onSegmentCompleted` 或 `onTurnCompleted` 负责。 |
| turn 与 segment hook | 按需覆盖，用于平台格式化、渐进投递以及失败或取消通知。 |

TypeScript 还通过 `onUserInputRequest` 处理用户输入请求，默认返回空答案；Python 不声明该 callback。两种语言的基类都会注册 heartbeat 响应，平台子类不需要自行实现。

两种语言的渐进投递略有不同。TypeScript 的 `onSegmentCompleted` 未成功投递时必须返回 `false`；其他返回值都会被视为已投递，此后默认 `onTurnCompleted` 会避免再次发送完整回复。Python 如果在 `on_segment_completed` 中发送 segment，还应覆盖 `on_turn_completed` 并记录是否已发送，避免重复投递完整回复。

## 一方渠道

TypeScript Channel authoring API 由 private `@dotcraft/channel` 包提供。Adapter 和 module authoring API 从根入口导入，队列与路由从 `/runtime` 导入，媒体 helper 从 `/media` 导入，conformance helper 从 `/testing` 导入，Channel contract 元数据从 `/meta` 导入。

TypeScript 为多个平台提供托管渠道模块。每个模块依赖 `@dotcraft/channel`，后者再依赖 `@dotcraft/sdk`。其安装与行为按平台文档说明：

- [QQ](../channels/qq) · [企业微信](../channels/wecom) · [飞书](../channels/feishu) · [Telegram](../channels/telegram) · [微信](../channels/weixin)

Python 提供 Telegram 参考适配器：

- [Telegram (Python)](../channels/python-telegram)

## 相关文档

- [AppServer 协议](../protocols/appserver-protocol)——底层 JSON-RPC 契约。
- 参考：[TypeScript](./typescript) · [Python](./python)。
