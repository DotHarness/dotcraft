# Channels & Bots

DotCraft can answer from the chat tools your team already uses: QQ, WeCom, Feishu / Lark, Telegram, and WeChat.

![Available channels in DotCraft Desktop](https://github.com/DotHarness/resources/raw/master/dotcraft/channels/catalog-light.png)

## Connect a channel

1. Open a workspace in DotCraft Desktop.
2. Open **Channels**.
3. Select the platform you want to connect.
4. Fill in the platform credentials shown on the form.
5. Finish the matching setup in the platform console or bot tool.
6. Turn the channel on.
7. Send a test message to the bot.

Desktop manages the bundled TypeScript channel process for you. Use the standalone adapter path only when you want to run the adapter yourself.

## Built-in channels

| Platform | Connection | Main capabilities | Setup |
|---|---|---|---|
| **QQ** | NapCat or OneBot v11 reverse WebSocket | Private chats, groups, approval keywords, media delivery | [QQ setup](../../developing/channels/qq) |
| **WeCom** | Group bot callback URL, Token, EncodingAESKey | Enterprise group chats, approvals, file and image delivery | [WeCom setup](../../developing/channels/wecom) |
| **Feishu / Lark** | Self-built app with Bot and WebSocket event subscription | Card replies, approvals, reactions, optional official CLI | [Feishu setup](../../developing/channels/feishu) |
| **Telegram** | BotFather token and long polling | Direct chats, groups, `/new`, `/help`, inline approvals | [Telegram setup](../../developing/channels/telegram) |
| **WeChat / Weixin** | Tencent iLink QR login | Weixin chats, saved login session, plain-text replies, file and image delivery | [Weixin setup](../../developing/channels/weixin) |
| **Telegram (Python)** | Python standalone adapter | Reference adapter for custom Python channel work | [Python Telegram setup](../../developing/channels/python-telegram) |

## How channel conversations work

![DotCraft channel adapter topology](/channel-adapter-topology.svg)

- Messages sent to a connected bot become DotCraft conversation turns.
- Replies are delivered back to the same chat automatically.
- Approval and user-input requests appear in the chat when the platform supports them.
- `/new` starts a fresh conversation in channels that support slash commands.
- Desktop can stay open on the same workspace to inspect history or continue the conversation.

For the underlying model, see [Unified Session Core](../../developing/architecture/session-core).

## Hand off a conversation

![DotCraft channel handoff](https://github.com/DotHarness/resources/raw/master/dotcraft/whats-new/channel-handoff.gif)

Bind an existing Desktop conversation to a connected social channel from the conversation's Apps menu. DotCraft shows a `/bind 123456` command; send that command in the target chat to continue the same conversation there.

The binding applies only to that chat. Other chats keep their normal channel conversation.

## Security checklist

Before exposing a bot to a group or public chat:

- Keep file and shell actions behind approval.
- Limit the channel to trusted users, groups, or chats when the platform supports it.
- Use a strong AppServer WebSocket token for standalone adapters.
- Run production deployments behind HTTPS when the platform calls back to DotCraft.
- Use [OpenSandbox](../self-hosted/security#sandbox-opensandbox) for stronger tool isolation when needed.

Full checklist and exact fields: [Security & Sandbox](../self-hosted/security) and [Configuration Reference](../../developing/configuration#tools-security-and-sandbox).

## Build a custom channel

Use the built-in channels first when they cover your platform. For a new platform or custom deployment:

- Channel modules: [Channel Module integration](../../developing/integrations/typescript-module)
- Channel adapter base class: [Channel adapters](../../developing/sdks/channels)
- Python SDK: [Python SDK](../../developing/sdks/python)
- Wire protocol: [AppServer Protocol](../../developing/protocols/appserver-protocol)

## Related docs

- [Channel configuration reference](../../developing/channels/reference)
- [Channel adapters](../../developing/sdks/channels)
- [Security & Sandbox](../self-hosted/security)
- [Server Deployment](../self-hosted/server-deployment)
