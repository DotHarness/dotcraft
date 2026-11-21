# Connect DotCraft to Feishu

Connect a Feishu or Lark bot to DotCraft with a self-built app and a WebSocket event subscription.

## Connect the bot

1. On the [Feishu Open Platform](https://open.feishu.cn/app), create a self-built app. Enable the bot capability and grant permissions to receive and send messages.
2. In the target DotCraft Desktop workspace, go to **Plugins → Channels**, install **Feishu**, and select **Set up**. Enter the app's App ID and App Secret. Add each admin's `open_id` to **Admin Users** for commands such as `/model`.

   ![Configure the Feishu bot in DotCraft Desktop](https://github.com/DotHarness/resources/raw/master/dotcraft/channels/feishu-configuration.png)

3. Save the settings and select **Connect**.
4. In **Events & Callbacks**, use a persistent connection and subscribe to the **Receive message** event.
5. Publish the app in the target tenant and add the bot to the chats where you want it to reply. Send a direct message or @mention the bot in a group to test the connection.

Grant resource access to send or receive images and files, chat metadata access for group context, and reaction access to mark handled messages.

## Using the bot

- Group messages require an @mention by default. Each group topic has its own DotCraft chat.
- The agent sends formatted text as cards, plus images, files, audio, and video. Approvals and [command choices](./#commands-in-a-chat) appear as card buttons.
- DotCraft marks handled messages with a reaction. Work that lasts more than 20 seconds gets a status card.
- Card text follows your Feishu display language.

### Official Feishu CLI

Turn on **Official Feishu CLI** in the channel settings. Grant the app the scopes for the commands you use and share the relevant resources with the bot. See the [Feishu channel configuration](../../configuration/channels#feishu) for command access and approvals.

### Personal access

Authorize an account to let the agent read personal resources such as calendars, Drive, and mail. The same read-only account serves everyone in the channel.

1. In channel settings, select **Personal Access Scopes → Edit** and choose what the agent may read. Enable those scopes and **持续访问已授权的数据** (`offline_access`) in the [Feishu developer console](https://open.feishu.cn/document/server-docs/application-scope/scope-list), then save the channel settings.
2. Send `/feishu-auth` to the bot in a direct message and authorize the account in the link it returns. Authorize again to apply new scopes.

Use `/feishu-auth status` to check the authorized account and `/feishu-auth revoke` to revoke access.

## Related docs

- [Feishu channel configuration](../../configuration/channels#feishu) — app settings and CLI access.
