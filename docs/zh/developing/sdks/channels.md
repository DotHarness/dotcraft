# 渠道适配器

渠道适配器把外部消息平台（Telegram、飞书、QQ 等）作为一等渠道桥接到 DotCraft。适配器为每个用户解析线程、运行轮次，并把回复投递回平台。

> [!NOTE]
> 渠道适配器是一个语言特定的 profile，**TypeScript 与 Python** 提供。.NET SDK 不提供渠道适配器。其 wire 契约由 [External Channel Adapter](https://github.com/DotHarness/dotcraft/blob/master/specs/protocols/external-channel-adapter.md) 规范定义。

继承适配器基类并实现平台钩子——投递、审批，以及（可选的）渠道工具。其余由 SDK 负责：按身份的消息排队、线程解析与恢复、斜杠命令路由、轮次流归并、心跳。

::: code-group

```ts [TypeScript]
import { ChannelAdapter } from "@dotcraft/sdk/channel";

class MyChannel extends ChannelAdapter {
  async onDeliver(target: string, content: string): Promise<boolean> {
    await platform.send(target, content);
    return true;
  }

  async onApprovalRequest(): Promise<string> {
    return "accept";
  }
}
```

```python [Python]
from dotcraft import ChannelAdapter, StdioTransport

class MyChannel(ChannelAdapter):
    def __init__(self):
        super().__init__(
            transport=StdioTransport(),
            channel_name="my-channel",
            client_name="my-adapter",
            client_version="1.0.0",
        )

    async def on_deliver(self, target: str, content: str, metadata: dict) -> bool:
        await platform_send(target, content)
        return True

    async def on_approval_request(self, request: dict) -> str:
        return "accept"
```

:::

用 `handleMessage` / `handle_message` 把平台消息转发进适配器；适配器会为该身份找到或创建线程、串行化并发输入、运行轮次，并用回复调用你的投递钩子。

## 一方渠道

TypeScript 为多个平台提供托管渠道模块，其安装与行为按平台文档说明：

- [QQ](../channels/qq) · [企业微信](../channels/wecom) · [飞书](../channels/feishu) · [Telegram (TypeScript)](../channels/telegram) · [微信](../channels/weixin)

Python 提供 Telegram 参考适配器：

- [Telegram (Python)](../channels/python-telegram)

## 参见

- [External Channel Adapter](https://github.com/DotHarness/dotcraft/blob/master/specs/protocols/external-channel-adapter.md)——渠道 wire 契约。
- 参考：[TypeScript](./typescript) · [Python](./python)。
