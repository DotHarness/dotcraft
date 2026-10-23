# Connect DotCraft to Feishu

Connect a Feishu or Lark bot to DotCraft as a [channel](./) with a self-built app and WebSocket event subscription.

## Quick setup

1. In the Feishu developer console, create a self-built app.
2. Enable the Bot capability.
3. Enable event subscription over long connection / WebSocket.
4. Copy the App ID and App Secret.
5. Open the target workspace in DotCraft Desktop.
6. Open **Channels**, select **Feishu**, then select **Connect**.

   ![Connect the Feishu channel from its details page](https://github.com/DotHarness/resources/raw/master/dotcraft/channels/feishu-detail.png)

7. Paste the App ID and App Secret. Add the open_id of each person who should run admin commands such as `/model` to **Admin Users**, then review the platform and group-message settings.

   ![Configure the Feishu bot in DotCraft Desktop](https://github.com/DotHarness/resources/raw/master/dotcraft/channels/feishu-configuration.png)

8. Save the channel and turn it on.

Desktop should show the Feishu channel as connected after the bot connects to Feishu events.

## Platform setup details

In the Feishu developer console:

1. Add the bot to the chats where DotCraft should respond.
2. Grant message event permissions so DotCraft can receive bot messages.
3. Grant message send permissions so the agent can send messages and DotCraft can post approval and status cards.
4. Grant resource permission if users will send images or files to DotCraft, or the agent will send them.
5. Grant chat metadata permission if the bot needs group context.
6. Grant reaction permission if you want DotCraft to mark handled messages with a reaction.

Publish or release the app in the target tenant before testing in group chats. Tenant policy can still block events or message sends even when scopes are selected.

## Test the connection

1. Send a direct message to the Feishu bot.
2. Confirm DotCraft replies with a Feishu card.
3. Add the bot to a group and @mention it.
4. Confirm DotCraft replies in the group.
5. Ask DotCraft to do something that needs approval and use the approval card buttons.

## What works after setup

- Direct messages are handled without a mention.
- Group messages require an @mention by default.
- Mention the bot inside a topic and the conversation stays in that topic; conversations in different topics stay separate.
- The agent sends formatted text as cards, plus images, files, audio, and video.
- DotCraft marks each message it picks up with the configured reaction. When the work runs longer than 20 seconds, a status card shows its progress and ends as done, failed, or stopped.
- Approvals and command choices, such as the models `/model` lists, appear as card buttons.
- Card text follows your Feishu display language: Chinese, English, Japanese, Korean, Spanish, French, and German.
- Image and file input can be downloaded when the app has resource permission.

### Official Feishu CLI

Set `feishu.cli.enabled` to `true` to let Feishu-origin conversations use the bundled official Feishu CLI as the configured Bot. Grant the app only the scopes required by the commands you intend to use, and share target resources with the app Bot where Feishu requires it. See the [Channel configuration](../../configuration/channels#feishu) for approvals and command restrictions.

### Personal access

A calendar, a personal drive, a mailbox — these belong to a person, and the Bot cannot open them. Authorize one account and the agent can read them on your behalf. It only ever reads; nothing is created, changed, or sent as you.

Open **Personal Access Scopes** in the channel settings, choose **Edit**, and tick what the agent may read, by product: Calendar, Docs, Drive, Mail, and so on. Expand a product to pick single scopes. While the channel is running, each product shows how many of its scopes the app has not enabled yet; enable them in the [Feishu developer console](https://open.feishu.cn/document/server-docs/application-scope/scope-list) first, or the authorization fails. Save the channel; new scopes apply the next time an account authorizes.

Also enable **持续访问已授权的数据** (`offline_access`) on the app. DotCraft always requests it so the authorization keeps working, so it is not in the list.

Then send `/feishu-auth` to the bot in a direct message and approve the link it replies with. You can also just ask the agent for something personal and let it offer the link; either way the link arrives privately, because whoever opens it becomes the authorized account. That account is the one the agent reads as, for everyone in the channel. Send `/feishu-auth status` to see which account is bound, or `/feishu-auth revoke` to remove it.

## Related docs

- [Channel configuration](../../configuration/channels) — every field, default, and registration shape for the Feishu config file.
