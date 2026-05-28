# Channels & Bots

DotCraft connects the same workspace to mainstream chat platforms via SDK extensions: QQ, WeCom, Feishu / Lark, Telegram, WeChat. These channels reuse the same session core, memory, skills, and security policy — context you see in Desktop is the same context the bot sees.

![DotCraft Channels configuration and conversations](https://github.com/DotHarness/resources/raw/master/dotcraft/whats-new/channels.gif)

## What's Provided

| Channel | SDK | Documentation |
|---|---|---|
| QQ | TypeScript | [channel-qq](../../developing/channels/qq.md) |
| WeCom | TypeScript | [channel-wecom](../../developing/channels/wecom.md) |
| Feishu / Lark | TypeScript | [channel-feishu](../../developing/channels/feishu.md) |
| Telegram (TypeScript) | TypeScript | [channel-telegram](../../developing/channels/telegram.md) |
| WeChat | TypeScript | [channel-weixin](../../developing/channels/weixin.md) |
| Telegram (Python) | Python | [python-telegram](../../developing/channels/python-telegram.md) |

TypeScript channel modules follow the [TypeScript Module integration contract](../../developing/typescript-module.md): standard `manifest`, `createModule`, `configDescriptors`, and lifecycle states.

## Integration Path

![DotCraft channel adapter topology](/channel-adapter-topology.svg)

Two integration shapes:

- **Embedded in Desktop**: Desktop launches the channel as a subprocess via `transport: "subprocess"` and `builtinModule`. Use the Desktop **Channels** page to fill in platform tokens, callbacks, allowlists, or QR-code auth, then enable in one click.
- **Standalone adapter**: An external process connects to AppServer over WebSocket via `transport: "websocket"`. Best for long-running processes on a server or container.

AppServer and channel registration fields live in [Entry Points and Services](../../developing/configuration.md#entry-points-and-services). Platform-specific connection, permission allowlists, and approval timeouts live in adapter-specific files like `.craft/qq.json` and `.craft/wecom.json`.

## Channels and Unified Session Core

- A group chat = a Thread; multiple messages from the same user = additional Turns / Items in the Thread.
- The channel passes group, user, and message-type metadata; DotCraft uses it to attribute messages to threads.
- When the agent needs approval (write file, shell command), the channel renders the request as a native platform message (button / quote). Execution waits for user consent.
- Desktop / TUI can connect to the same AppServer to see chat history, take over, or correct replies.

See [Unified Session Core](../session-core.md) for the model.

## Scenarios

| Scenario | Recommendation |
|---|---|
| Internal knowledge-base bot | Feishu / WeCom for enterprise IT |
| OSS community Q&A | Telegram / QQ |
| Project support / aftermarket | WeChat / WeCom |
| Trigger an Agent CI report from a group | Any channel + [Automations](../automations.md) |
| Read group history in Desktop and take over | Any channel + Desktop on the same workspace |

## Security Notes

Connecting an external channel exposes the agent to arbitrary user input. Pair it with:

- Approval for outside-workspace file and shell actions
- A narrowed tool surface
- A strong random AppServer WebSocket token
- Optionally [OpenSandbox](../security.md#sandbox-opensandbox) for further isolation

Full checklist and exact fields: [Security & Sandbox](../security.md) and [Configuration Reference](../../developing/configuration.md#tools-security-and-sandbox).

## Building a Custom Channel

DotCraft ships five channels out of the box. To integrate other platforms (Slack, Discord, Lark private deployment, enterprise IM):

- TypeScript: see [TypeScript SDK](../../developing/sdk-typescript.md) and the [Module integration contract](../../developing/typescript-module.md)
- Python: see [Python SDK](../../developing/sdk-python.md)
- Any language: speak [AppServer Protocol](../../developing/appserver-protocol.md) directly

## Troubleshooting

### Adapter cannot connect to AppServer

Confirm AppServer runs in WebSocket mode, the URL contains `/ws`, and the token matches the client config.

### Messages arrive but the agent does not reply

Check that the adapter declared delivery capabilities during initialize. Verify the model provider's merged result on the Dashboard Settings page. Check whether tools were rejected by approval.

### Desktop does not see bot conversations

Confirm Desktop and the bot connect to the **same** workspace / AppServer.

## Related

- [Unified Session Core](../session-core.md)
- [Security & Sandbox](../security.md)
- [TypeScript SDK](../../developing/sdk-typescript.md) · [Python SDK](../../developing/sdk-python.md)
- [TypeScript Module integration contract](../../developing/typescript-module.md)
