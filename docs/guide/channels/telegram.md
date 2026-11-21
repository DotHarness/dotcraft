# Connect DotCraft to Telegram

Connect a Telegram bot to DotCraft with a BotFather token.

## Connect the bot

1. Send `/newbot` to `@BotFather` in Telegram, follow its prompts, and copy the bot token.
2. In the target DotCraft Desktop workspace, go to **Plugins → Channels**, install **Telegram**, and select **Set up**.
3. Paste the token. Add numeric Telegram user IDs to **Admin Users** for commands such as `/model`. Assign the bot token to one DotCraft instance at a time.

   ![Configure the Telegram bot in DotCraft Desktop](https://github.com/DotHarness/resources/raw/master/dotcraft/channels/telegram-configuration.png)

4. Save the settings, select **Connect**, and send the bot a direct message.

To use the bot in a group, add it to the group and @mention it or reply to it. To receive every group message, turn off privacy mode in BotFather.

## Using the bot

- Each group member has their own DotCraft chat with the bot.
- The agent sends formatted text, images, files, audio, and video. The bot shows that it is typing while the agent works.
- The bot menu lists each sender's available [chat commands](./#commands-in-a-chat), including workspace commands. Approvals, follow-up questions, and command choices use inline buttons.

## Related docs

- [Telegram channel configuration](../../configuration/channels#telegram) — bot token, proxy, and timeouts.
