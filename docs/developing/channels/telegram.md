# Connect DotCraft to Telegram

Connect a Telegram bot to DotCraft with a BotFather token. DotCraft receives messages by long polling, so you do not need a public webhook URL.

## Quick setup

1. Open Telegram and start a chat with `@BotFather`.
2. Send `/newbot` and follow BotFather's prompts.
3. Copy the bot token.
4. Open the target workspace in DotCraft Desktop.
5. Open **Channels**, select **Telegram**, then select **Connect**.

   ![Connect the Telegram channel from its details page](https://github.com/DotHarness/resources/raw/master/dotcraft/channels/telegram-detail-light.png)

6. Paste the bot token, then review the optional proxy and timeout settings.

   ![Configure the Telegram bot in DotCraft Desktop](https://github.com/DotHarness/resources/raw/master/dotcraft/channels/telegram-configuration-light.png)

7. Save the channel and turn it on.
8. Send a message to the Telegram bot.

Desktop should show the Telegram channel as connected after polling starts.

## Platform setup details

Telegram setup lives mostly in BotFather:

1. Use `/setdescription`, `/setabouttext`, and `/setuserpic` if you want the bot profile to look finished.
2. Add the bot to a group if DotCraft should answer there.
3. Review BotFather privacy mode for group chats.

With Telegram privacy mode enabled, the bot may only receive commands, replies, and messages that mention it. If you need DotCraft to see every group message, change the bot privacy setting in BotFather.

## Test the connection

1. Send `hello` in a direct chat with the bot.
2. Confirm DotCraft replies.
3. Send `/help` to confirm commands are registered.
4. Send `/new`, then send another message and confirm it starts a fresh conversation.
5. In a group, mention or reply to the bot if privacy mode is enabled.

## What works after setup

- Direct messages and group messages can start DotCraft turns when Telegram delivers them to the bot.
- `/new` starts a fresh DotCraft conversation.
- `/help` shows available commands.
- Approval and user-input prompts use Telegram inline buttons when possible.
- File and image delivery are available through channel delivery tools.
- Only one long-polling process can use the same bot token at a time.

## Standalone adapter

Run the Telegram adapter yourself only when Desktop is not managing the channel process.

```bash
cd sdk/typescript
npm run build --workspace @dotcraft/channel-telegram
npx dotcraft-channel-telegram --workspace /path/to/workspace
```

Register the channel as a standalone WebSocket adapter in the shared [channel configuration reference](./reference).

## Reference

See [Channel configuration reference](./reference) for the Telegram JSON example, `ExternalChannels` registration, and field table.

## Related docs

- [Channels & Bots](../../features/entry-points/channels)
- [Channel configuration reference](./reference)
- [Channel adapters](../sdks/channels)
- [Telegram Python adapter](./python-telegram)
