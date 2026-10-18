# 渠道适配器

渠道适配器把外部消息平台作为一等渠道接入 DotCraft。它为每段会话解析出 thread，把平台消息和命令转发过去，并承接 Agent 用 `SendChannelMessage` 发出的投递。

适配器是运行时抽象。要让 Desktop 等 DotCraft 宿主发现并管理同一个适配器，可将其封装为[渠道模块](../channel-modules)。模块补充宿主所需的元数据和生命周期包装，并不会取代适配器。

> [!NOTE]
> 渠道适配器是语言特定的 profile，只有 **TypeScript** 提供。.NET SDK 没有渠道适配器。

要用内置的 Channel 策略，继承 adapter 基类即可。基类负责按身份分队的消息队列、thread 解析、命令路由、把输入并入运行中的 turn、turn 活动归并、服务端请求 handler 和心跳。

适配器不会自行发出 Agent 的回复。聊天里的人只看得到 Agent 用 `SendChannelMessage` 发出的内容，这些内容以 `ext/channel/send` 请求到达适配器，由 `onSend` 处理。Turn hook 只报告活动，不带文本，适配器据此表明 Agent 正在工作。

![一条平台消息在渠道适配器里的一轮：按身份排队、解析 thread 或直接执行命令、开启新的 turn 或并入运行中的 turn，只有 Agent 主动发送的消息才会回到聊天](/channel-adapter-loop.svg)

## 最小适配器

```ts
import {
  ChannelAdapter,
  type ChannelAdapterMessageOpts,
  type ChannelChatDescriptor,
  type ChannelDeliveryCapabilities,
} from "@dotcraft/channel";

class MyChannel extends ChannelAdapter {
  async onDeliver(target: string, content: string, _metadata: Record<string, unknown>): Promise<boolean> {
    await platform.send(target, content);
    return true;
  }

  async onApprovalRequest(request: Record<string, unknown>): Promise<string> {
    return await platform.requestApproval(request);
  }

  protected getDisplayName(): string {
    return "My Chat";
  }

  protected getDeliveryCapabilities(): ChannelDeliveryCapabilities {
    return {
      text: { format: "plain" },
      targets: { group: false, direct: "none" },
    };
  }

  protected getChatDescriptor(opts: ChannelAdapterMessageOpts, _channelContext: string): ChannelChatDescriptor {
    return { kind: "direct", label: opts.userName, membership: "shared" };
  }
}
```

这个适配器只发送文本。要投递附件，声明 `media` 条目并覆盖 `onSend`。

## 生命周期与恢复

- 接收平台事件前调用 `start()`。它连接 Wire client、注册 Channel handler，然后在 `initialize` 时声明 Channel 能力。关闭时调用 `stop()`。
- 用 `handleMessage` 转发每个事件。该调用只表示事件已进入内存队列，不表示 turn 已经开始。`channelContext` 使用消息所在的会话，群消息不要用发送者的私聊，因为 Agent 的消息默认发到这里。adapter 能触达发送者的私聊时传入 `directTarget`，Agent 就能在那里给对方发消息。
- 只有配置的管理员列表里的发送者，才在 `sender` 中设置 `senderRole: "admin"`。平台放行的其他人都以 `user` 转发，仅限管理员的命令会拒绝他们。不允许的发送者在调用 `handleMessage` 之前就丢弃。
- 队列身份由 user id 与 channel context 共同确定。同一身份的消息串行执行，不同身份可以并发。
- thread 上有 turn 在运行时，新消息通过 `turn/steer` 并入这个 turn。turn 无法接收时，消息用 `turn/enqueue` 排队，否则开启新的 turn。适配器会替换过期或 inactive 的 thread，并在替代 thread 上重试。
- Wire client 会重连并重新执行初始化，但不会持久化或重放平台事件。保持平台接收器在线，按需在平台侧做去重或重试。

## 命令

只有首个词是服务端 `command/list` 中某个命令的名称或别名时，消息才算命令。其他文本，包括以 `/` 开头的路径，都是普通输入。命令绕过队列，一到达就用 `command/execute` 执行，turn 运行中也一样，所以 `/stop` 会立即生效。

基类通过 `onDeliver` 显示命令结果的消息，结果指定了 thread 时把会话切换过去，再对其中的选项调用 `renderCommandChoices`。默认实现渲染一个编号列表，写出每个选项的命令。平台有按钮或卡片时覆盖它，并用所选选项的命令和选择者的消息参数调用 `executeCommandChoice`。

adapter 本地命令只用于平台设置，例如 `/bind` 和账号授权。平台有原生命令菜单时，用 `listCommands()` 填充，带 `requiresAdmin` 的命令只对管理员显示。

用文字回复审批时，除了 `parseApprovalReplyDecision` 识别的回复关键词，还接受 `/approve`、`/approve session` 和 `/deny`。

## Handler 规则

| Hook | 契约 |
|------|------|
| `onDeliver` | 必须实现。把纯文本投递到平台目标并报告是否成功。命令结果、通知和默认的文本发送都使用它。 |
| `onApprovalRequest` | 必须实现。返回有效审批决定。hook 抛出异常时，adapter 会回答 `cancel`。 |
| `getDisplayName` | 必须实现。返回平台的产品名称，例如 `Telegram`。Agent 和 Desktop 会显示它。 |
| `getDeliveryCapabilities` | 必须实现。声明平台如何呈现文本（`plain` 或 `markdown`）、adapter 能触达哪些其他会话（`targets`），以及 `onSend` 能投递的每种附件类型（`media`）。只声明 adapter 真正能触达的目标。 |
| `getChatDescriptor` | 必须实现。描述消息所在的会话：类型（`direct`、`group` 或 `topic`）、名称，以及 thread 收到所有成员的消息（`shared`）还是只收到一位发送者的消息（`sender`）。adapter 创建或恢复 thread 时会发送它。 |
| `onSend` | 覆盖它来投递 `getDeliveryCapabilities` 声明的每种附件类型，无法触达的目标返回 `UnsupportedTarget`。默认实现接受文本，其他类型返回 `UnsupportedDeliveryKind`。基类对重复的投递 id 直接返回首次结果，重试的发送不会重复发出。 |
| `getChannelTools` + `onToolCall` | 可选。只声明 call hook 已实现的工具。默认 call hook 返回 `UnsupportedTool`。渠道工具用于消息之外的平台操作，发消息已由 `SendChannelMessage` 覆盖。 |
| `onTurnStarted`、`onActivity`、`onTurnRetrying`、`onTurnEnded` | 可选。为 adapter 开启的 turn 显示聊天活动，比如正在输入提示或状态卡片。`onActivity` 报告文本、推理和工具 item 的开始与完成，不含内容。服务端重试模型流时触发 `onTurnRetrying`。`onTurnEnded` 只触发一次，结果为 `completed`、`failed`、`cancelled` 或 `disconnected`。默认实现在 `failed` 时发送一条通用失败通知，状态卡片已经显示失败时覆盖它。 |
| `onCurrentThreadChanged` | 可选。`/new`、`/resume` 等命令把会话切换到另一个 thread 时调用。 |
| `renderCommandChoices` | 可选。呈现命令结果中的选项。默认实现通过 `onDeliver` 渲染编号列表。 |

适配器还通过 `onUserInputRequest` 处理用户输入请求，默认返回空答案集。heartbeat 响应由基类自行注册，平台子类不需要实现。

## 包

TypeScript Channel authoring 使用 `@dotcraft/channel`。Adapter authoring API 从根入口导入，队列与路由从 `/runtime` 导入，媒体 helper 从 `/media` 导入，conformance helper 从 `/testing` 导入，Channel contract 元数据从 `/meta` 导入。

DotCraft 的内置托管渠道也使用这套基类。安装与配置见[渠道配置](../../configuration/channels)。

## 相关文档

- [渠道模块](../channel-modules)——封装适配器，供宿主发现并管理生命周期。
- [AppServer 协议](../protocols/appserver#渠道适配器)——适配器之下的 JSON-RPC 契约。
- [TypeScript 参考](./typescript)——适配器使用的 SDK 接口。
