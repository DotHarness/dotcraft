# Channels & Bots

DotCraft can bring the same agent into the chat platforms your team already uses — QQ, WeCom, Feishu / Lark, Telegram, WeChat. A channel bot shares the same session core, memory, skills, and security policy as everything else, so what you see in Desktop is the same context the bot works from.

![DotCraft Channels configuration and conversations](https://github.com/DotHarness/resources/raw/master/dotcraft/whats-new/channels.gif)

## What's Provided

| Channel | SDK | Documentation |
|---|---|---|
| QQ | TypeScript | [channel-qq](../../developing/channels/qq) |
| WeCom | TypeScript | [channel-wecom](../../developing/channels/wecom) |
| Feishu / Lark | TypeScript | [channel-feishu](../../developing/channels/feishu) |
| Telegram (TypeScript) | TypeScript | [channel-telegram](../../developing/channels/telegram) |
| WeChat | TypeScript | [channel-weixin](../../developing/channels/weixin) |
| Telegram (Python) | Python | [python-telegram](../../developing/channels/python-telegram) |

TypeScript channel modules follow the [TypeScript Module integration contract](../../developing/integrations/typescript-module): standard `manifest`, `createModule`, `configDescriptors`, and lifecycle states.

## Integration Path

![DotCraft channel adapter topology](/channel-adapter-topology.svg)

Three integration shapes:

- **Embedded in Desktop**: Desktop launches the channel for you. Open the Desktop **Channels** page, fill in platform tokens, callbacks, allowlists, or QR-code auth, then enable in one click.
- **Server Compose deployment**: Use [Server Deployment](../self-hosted/server-deployment) to run AppServer, the bundled TypeScript channels, and optional OpenSandbox from Docker Compose.
- **Standalone adapter**: Run the channel as your own process and connect it to AppServer over WebSocket. Best when you need to operate a custom adapter yourself.

For the channel registration and transport fields behind these shapes, see [Entry Points and Services](../../developing/configuration#entry-points-and-services). Each platform's connection, permission allowlists, and approval timeouts are set in the [Configuration Reference](../../developing/configuration).

## Channels and Unified Session Core

- A group chat = a Thread; multiple messages from the same user = additional Turns / Items in the Thread.
- The channel passes group, user, and message-type metadata; DotCraft uses it to attribute messages to threads.
- When the agent needs approval (write file, shell command), the channel renders the request as a native platform message (button / quote). Execution waits for user consent.
- Desktop / TUI can connect to the same AppServer to see chat history, take over, or correct replies.

See [Unified Session Core](../../developing/architecture/session-core) for the model.

## Scenarios

| Scenario | Recommendation |
|---|---|
| Internal knowledge-base bot | Feishu / WeCom for enterprise IT |
| OSS community Q&A | Telegram / QQ |
| Project support / aftermarket | WeChat / WeCom |
| Trigger an Agent CI report from a group | Any channel + [Automations](../agent-system/automations) |
| Read group history in Desktop and take over | Any channel + Desktop on the same workspace |

## Security Notes

Connecting an external channel exposes the agent to arbitrary user input. Pair it with:

- Approval for outside-workspace file and shell actions
- A narrowed tool surface
- A strong random AppServer WebSocket token
- Optionally [OpenSandbox](../self-hosted/security#sandbox-opensandbox) for further isolation

Full checklist and exact fields: [Security & Sandbox](../self-hosted/security) and [Configuration Reference](../../developing/configuration#tools-security-and-sandbox).

## Building a Custom Channel

DotCraft ships five channels out of the box. To integrate other platforms (Slack, Discord, Lark private deployment, enterprise IM):

- TypeScript: see [TypeScript SDK](../../developing/sdks/typescript) and the [Module integration contract](../../developing/integrations/typescript-module)
- Python: see [Python SDK](../../developing/sdks/python)
- Any language: speak [AppServer Protocol](../../developing/protocols/appserver-protocol) directly

## Related docs

- [Unified Session Core](../../developing/architecture/session-core)
- [Security & Sandbox](../self-hosted/security)
- [Server Deployment](../self-hosted/server-deployment)
- [TypeScript SDK](../../developing/sdks/typescript) · [Python SDK](../../developing/sdks/python)
- [TypeScript Module integration contract](../../developing/integrations/typescript-module)
