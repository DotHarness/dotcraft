# Connect DotCraft to QQ

Connect a QQ account to DotCraft as a [channel](./) through NapCat or another OneBot v11 gateway.

> [!CAUTION]
> Third-party QQ protocol frameworks can create account risk. Use a dedicated QQ account and review the risk before deployment.

## Quick setup

1. Open the target workspace in DotCraft Desktop.
2. Open **Channels**, select **QQ**, then select **Connect**.

   ![Connect the QQ channel from its details page](https://github.com/DotHarness/resources/raw/master/dotcraft/channels/qq-detail.png)

3. Set the OneBot listen address. The default is `127.0.0.1:6700`.
4. Enter an access token if NapCat should authenticate to the DotCraft endpoint.
5. Add at least one admin user, allowed user, or allowed group.

   ![Configure the QQ channel in DotCraft Desktop](https://github.com/DotHarness/resources/raw/master/dotcraft/channels/qq-configuration.png)

6. Save the channel and turn it on.
7. In NapCat WebUI, add a reverse WebSocket connection to `ws://127.0.0.1:6700/`.
8. Set the NapCat message format to `array`.

Desktop should show the QQ channel as connected after NapCat reaches the DotCraft listener.

## Platform setup details

In NapCat, configure the QQ account that will speak for DotCraft:

1. Log in with the dedicated QQ account.
2. Open the OneBot / WebSocket client settings.
3. Set the reverse WebSocket URL to the DotCraft listen address.
4. Set the Token to the same value you entered in Desktop.
5. Set message format to `array`.

If NapCat runs in Docker or on another machine, replace `127.0.0.1` with an address that can reach the machine running DotCraft Desktop.

## Test the connection

1. Send a private message to the QQ account from an allowed user.
2. In a group, @mention the bot account.
3. Confirm DotCraft replies in the same QQ conversation.
4. Ask DotCraft to do something that needs approval, then reply with `同意`, `允许`, `yes`, or `approve`.

## What works after setup

- Private chats keep a separate DotCraft conversation per QQ user.
- A QQ group shares one conversation.
- Group messages require an @mention by default.
- DotCraft marks each message it picks up with the configured emoji reaction. This needs a gateway that supports `set_msg_emoji_like`, such as NapCat. If the reaction fails, such as in a private chat, the message is still handled without a mark.
- Empty admin and allowlist settings mean the bot ignores QQ messages.
- Admin users can also run admin commands such as `/model`. Allowed users and members of allowed groups use the other [chat commands](./#commands-in-a-chat), and command choices appear as a numbered list.
- Approval replies accept `同意`, `允许`, `yes`, `approve`, `拒绝`, `no`, `reject`, and `deny`. A leading `/` also works, so `/yes` and `/no` are accepted, as do `/approve`, `/approve session`, and `/deny`. Replying `同意全部` or `approve all` allows the same kind of action for the rest of the session.
- While an approval waits for you, the bot answers your other messages in that chat with a reminder instead of passing them on. If you don't answer in time, the action is cancelled and the chat tells you.
- The agent sends text, images, files, voice, and video. With your approval, it can also post to another group or to a user who has messaged the bot.
- NapCat in Docker does not need access to the workspace files.

## Related docs

- [Channel configuration](../../configuration/channels) — every field, default, and registration shape for the QQ config file.
