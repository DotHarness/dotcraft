# Channels & Bots

Put DotCraft in the chat tools your team already uses, so a colleague can ask in the group chat instead of opening Desktop. QQ, WeCom, Feishu / Lark, Telegram, and WeChat all connect, and the resulting sessions and memory are shared with every other entry point in the workspace.

![Available channels in DotCraft Desktop](https://github.com/DotHarness/resources/raw/master/dotcraft/channels/catalog.png)

## Connect a channel

1. Open a workspace in DotCraft Desktop and go to **Channels**.
2. Pick the platform you want, and fill in its credentials on the form.
3. Finish the matching setup in the platform console or bot tool.
4. Turn the channel on, and send the bot a test message.

Desktop hosts the channel process for you — nothing else to deploy. Which credentials each platform needs is on its setup page in the table below. Settings shared by every channel are in the [Channel configuration](../../configuration/channels).

## Built-in channels

| Platform | Connection | What the agent can send | While it works | Setup |
|---|---|---|---|---|
| **QQ** | NapCat or OneBot v11 reverse WebSocket | Text, images, files, voice, video | A notice if the request fails | [QQ setup](./qq) |
| **WeCom** | Group bot callback URL, Token, EncodingAESKey | Text, images, files, voice | A notice if the request fails | [WeCom setup](./wecom) |
| **Feishu / Lark** | Self-built app with Bot and WebSocket event subscription | Formatted text, images, files, audio, video | Reaction, status card | [Feishu setup](./feishu) |
| **Telegram** | BotFather token | Formatted text, images, files, audio, video | Typing indicator, a notice if the request fails | [Telegram setup](./telegram) |
| **WeChat / Weixin** | Tencent iLink QR login | Text, images, files | A notice if the request fails | [Weixin setup](./weixin) |

## How the agent talks in a chat

![DotCraft channel adapter topology](/channel-adapter-topology.svg)

- Messages you send to the bot continue one conversation. A message you send while the agent is working joins the work in progress, so you can add detail or change direction without waiting.
- The agent replies by sending messages to the chat: text, plus images, files, and voice or video where the platform supports them. People in the chat see only what it sends. Its reasoning, tool calls, and working notes stay in the conversation, where Desktop shows them.
- The agent knows which platform and chat it is in and what that chat can display, so it writes plain text where the platform shows no formatting.
- Ask the agent to post somewhere else, such as another group, and it can send there too. Each new destination asks for approval first.
- Approvals and follow-up questions appear in the chat, as buttons where the platform has them.
- Open the same workspace in Desktop to read the history or keep the conversation going there.

For the model underneath, see [Unified Session Core](../../contribute/architecture/session-core).

### While the agent works

The bot shows that it is busy in whatever way the platform allows. On Feishu it reacts to your message, and a status card appears when the work runs longer than 20 seconds. The card updates quietly and ends as done, failed, or stopped. On Telegram the bot shows that it is typing. Wherever there is no status card, a failed request gets one short notice in the chat.

## Commands in a chat

Send a command as its own message. Commands run right away, even while the agent is working.

| Command | What it does |
|---|---|
| `/new` | Start a fresh conversation in this chat. The previous one stays available in `/sessions`. |
| `/stop` | Stop the current work. Background tasks keep running. |
| `/stop all` | Stop the current work, drop queued messages, and stop every background task. |
| `/tasks` | List background tasks. `/tasks stop <n>` stops one. |
| `/status` | Show what the conversation is doing: state, model, mode, context use, and anything queued or waiting for approval. |
| `/model [model]` | List the available models, or switch this conversation to one. Admins only. |
| `/mode [agent\|plan]` | Show the mode, or switch it. |
| `/sessions` | List this chat's recent conversations. |
| `/resume <n>` | Go back to conversation `n` from `/sessions`. |
| `/compact` | Compact the conversation's context. |
| `/automate list\|show\|pause\|resume\|run\|remove` | Manage automations created from this chat. Admins only. |
| `/help` | List the commands you can use. |
| `/init` | Create an `AGENTS.md` file with instructions for DotCraft. Admins only. |
| `/debug` | Toggle debug mode. Admins only. |

`/new` and `/resume` stop the current work before they switch conversations. `/compact` and `/init` run only when the agent is idle; otherwise the bot says it is busy. The workspace's own custom commands work in chats too.

When a command offers choices, such as the models `/model` lists, Feishu and Telegram show them as buttons. Other platforms show a numbered list with the command to send for each choice. Telegram also fills its command menu, showing each sender only the commands they can use.

To answer a pending approval by text, reply `/approve`, `/approve session` to allow the same kind of action for the rest of the conversation, or `/deny`. Each platform's own reply keywords and buttons work as well.

Admin commands are limited to the senders in the channel's admin list. Set it with `adminUsers` in the [Channel configuration](../../configuration/channels); with an empty list, nobody in the chat can run admin commands. Weixin has no admin list: only the bot's owner can reach it, so every Weixin sender can run admin commands.

## Automations from a chat

Ask the agent in a chat to do something on a schedule, and the automation it creates belongs to that chat. Each run posts to the chat only when the agent has something to send, so a check that finds nothing stays quiet. If a run fails, the bot posts one short notice naming the automation. Manage these automations with `/automate`, or from **Automations** in Desktop.

## Continue a Desktop conversation in chat

In a Desktop conversation, type `/bind` and pick a connected channel. DotCraft shows a command such as `/bind 482913`. Send it in the chat you want within 10 minutes. Messages from that chat then go into the conversation, and the agent sends its replies there.

The binding applies only to that chat; other chats keep their own conversations. To stop, choose **Stop continuing in …** from the conversation's **⋯** menu, or type `/unbind`.

## Before you open a bot to a group

Before putting a bot in a group or public chat:

- Keep file and shell actions behind approval.
- Limit the channel to trusted users, groups, or chats where the platform supports it.
- Give admin rights only to the people who should change models or manage automations.
- Set a strong random AppServer WebSocket token when you run an adapter yourself.
- Serve production deployments over HTTPS when the platform calls back into DotCraft.

The exact field names are in the [Entry points and services](../../configuration/services).

## Related docs

- [Security](../../self-hosting/security) — tighten tools and approvals before a bot faces a group chat
- [Server Deployment](../../self-hosting/server-deployment) — keep channels running on a server instead of your own machine
