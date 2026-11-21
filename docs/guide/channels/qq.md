# Connect DotCraft to QQ

Connect a QQ account to DotCraft through NapCat or another OneBot v11 gateway.

> [!CAUTION]
> Third-party QQ frameworks carry account risk. Use a dedicated QQ account.

## Connect the bot

1. Sign in to [NapCat](https://napneko.github.io/) with the bot's QQ account.
2. In the target DotCraft Desktop workspace, go to **Plugins → Channels**, install **QQ**, and select **Set up**.
3. Set the OneBot listen address (default: `127.0.0.1:6700`). Add at least one admin user, allowed user, or allowed group. To authenticate the connection, set an access token in DotCraft and use the same token in NapCat.

   ![Configure the QQ channel in DotCraft Desktop](https://github.com/DotHarness/resources/raw/master/dotcraft/channels/qq-configuration.png)

4. Save the settings and select **Connect**.
5. In NapCat WebUI, add a reverse WebSocket connection to `ws://127.0.0.1:6700/` and set the message format to `array`. For NapCat in Docker or on another machine, use a DotCraft address reachable from NapCat.
6. Send a private message from an allowed account, or @mention the bot in an allowed group. DotCraft replies in the same QQ chat.

## Using the bot

- Each QQ user has a separate DotCraft chat in private messages. Each group shares one DotCraft chat. Group messages require an @mention by default.
- Admin users can run commands such as `/model`. Allowed users and group members can use the other [chat commands](./#commands-in-a-chat). Command choices appear as a numbered list.
- DotCraft reacts to handled messages through gateways that support `set_msg_emoji_like`, such as NapCat.
- The agent sends text, images, files, voice, and video. With your approval, it can send to another group or to a QQ user who has messaged the bot.

## Related docs

- [QQ channel configuration](../../configuration/channels#qq) — listener, access token, and allowlists.
