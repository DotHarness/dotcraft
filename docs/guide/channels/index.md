# Channels & Bots

Connect DotCraft to QQ, WeCom, Feishu / Lark, Telegram, or WeChat. Channel chats and Desktop share the workspace's history and memory.

![DotCraft channel adapter topology](/channel-adapter-topology.svg)

## Connect a channel

1. Open your workspace in DotCraft Desktop and go to **Plugins → Channels**.
2. Choose your platform and select **Install**.
3. Follow the platform's setup steps, then open **Set up** to enter any required credentials.
4. Select **Connect**. Finish sign-in if prompted, then send the bot a test message.

For other platforms, install a [channel plugin](../../extend/channel-modules).

## Official channel plugins

| Platform | Connection | What the agent can send | While it works | Setup |
|---|---|---|---|---|
| **QQ** | NapCat or OneBot v11 reverse WebSocket | Text, images, files, voice, video | A notice if the request fails | [QQ setup](./qq) |
| **WeCom** | Group bot callback URL, Token, EncodingAESKey | Text, images, files, voice | A notice if the request fails | [WeCom setup](./wecom) |
| **Feishu / Lark** | Self-built app with Bot and WebSocket event subscription | Formatted text, images, files, audio, video | Reaction, status card | [Feishu setup](./feishu) |
| **Telegram** | BotFather token | Formatted text, images, files, audio, video | Typing indicator, a notice if the request fails | [Telegram setup](./telegram) |
| **WeChat / Weixin** | Tencent iLink QR login | Text, images, files | A notice if the request fails | [Weixin setup](./weixin) |

## How the agent talks in a chat

- Send another message while the agent is working to add details or change direction in the same DotCraft chat.
- Replies use the formats and media listed for each platform. Approve sends to other chats when prompted.
- Answer approvals and follow-up questions where you sent the request. Open the workspace in Desktop to read the history or continue the DotCraft chat.

### While the agent works

Feishu shows a reaction and adds a status card after 20 seconds. Telegram shows a typing indicator. Failed requests appear on the status card or in a short chat notice.

## Commands in a chat

Send a command as its own message. Commands run right away, even while the agent is working.

| Command | What it does |
|---|---|
| `/new` | Start a fresh DotCraft chat here. The previous one stays available in `/sessions`. |
| `/stop` | Stop the current work. Background tasks keep running. |
| `/stop all` | Stop the current work, drop queued messages, and stop every background task. |
| `/tasks` | List background tasks. `/tasks stop <n>` stops one. |
| `/status` | Show what the DotCraft chat is doing: state, model, mode, context use, and anything queued or waiting for approval. |
| `/model [model]` | List the available models, or switch this DotCraft chat to one. Admins only. |
| `/mode [agent\|plan]` | Show the mode, or switch it. |
| `/sessions` | List the recent DotCraft chats here. |
| `/resume <n>` | Go back to DotCraft chat `n` from `/sessions`. |
| `/compact` | Compact the DotCraft chat's context. |
| `/automate list\|show\|pause\|resume\|run\|remove` | Manage automations created from this chat. Admins only. |
| `/help` | List the commands you can use. |
| `/init` | Create an `AGENTS.md` file with instructions for DotCraft. Admins only. |
| `/debug` | Toggle debug mode. Admins only. |

`/new` and `/resume` stop the current work before switching DotCraft chats. Run `/compact` and `/init` when the agent is idle.

For choices such as the models from `/model`, select a button in Feishu or Telegram. On other platforms, send the command next to your choice in the numbered list.

For a text approval on work you requested, reply `/approve` to allow, `/deny` to decline, or `/approve session` to allow similar actions for that DotCraft chat. You can also reply `yes` or `同意`, `no` or `拒绝`, and `approve all` or `同意全部` respectively. The request expires when its approval window ends.

Grant admin commands through `adminUsers` in the [channel configuration](../../configuration/channels). In Weixin, the bot owner has admin access.

## Automations from a chat

Ask the agent to run a task on a schedule. Results and failure notices return to the chat where you created it. Manage scheduled tasks with `/automate` or in **Automations** in Desktop.

## Continue a Desktop chat in a chat app

In a Desktop chat, type `/bind` and pick a connected channel. DotCraft shows a command such as `/bind 482913`. Send it in the chat you want within 10 minutes. Messages from that chat then go into the Desktop chat, and the agent sends its replies there.

To use `/new`, `/sessions`, or `/resume` in that chat again, send `/unbind` or choose **Stop continuing in …** from the Desktop chat's **⋯** menu.

## Before you open a bot to a group

- Keep file and shell actions behind approval.
- Limit the channel to trusted users, groups, or chats where the platform supports it.
- Grant admin rights to the people responsible for model selection and automations.
- Use HTTPS for public callback endpoints.

## Related docs

- [Security](../../self-hosting/security) — tighten tools and approvals before a bot faces a group chat
- [Server Deployment](../../self-hosting/server-deployment) — run channels on a server
