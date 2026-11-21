# Connect DotCraft to Weixin

Connect Weixin to DotCraft by scanning a QR code in WeChat.

## Connect the bot

1. In WeChat on your phone, turn on **WeChat ClawBot** under **Me → Settings → Plugins**.
2. In the target DotCraft Desktop workspace, go to **Plugins → Channels** and install **WeChat**.

   ![Review the WeChat channel settings in DotCraft Desktop](https://github.com/DotHarness/resources/raw/master/dotcraft/channels/weixin-configuration.png)

3. Select **Connect** and scan the QR code with WeChat.
4. Send a message in the WeChat ClawBot chat. DotCraft replies there.

DotCraft reuses your sign-in on reconnect. Scan a new QR code when asked to sign in again.

## Using the bot

- The bot owner can run [chat commands](./#commands-in-a-chat), including admin commands such as `/model`. Command choices appear as a numbered list.
- The agent sends plain text, images, and files. It can reach Weixin users who have messaged the bot.

## Related docs

- [Weixin channel configuration](../../configuration/channels#weixin) — API address and timeouts.
- [WeCom channel](./wecom) — connect a bot for enterprise chats through a callback URL.
