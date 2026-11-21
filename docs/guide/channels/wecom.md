# Connect DotCraft to WeCom

Connect a WeCom group bot to DotCraft through a public HTTPS callback.

## Connect the bot

1. In the WeCom admin console, create or open a group bot and enable callback mode.
2. In the target DotCraft Desktop workspace, go to **Plugins → Channels**, install **WeCom**, and select **Set up**.
3. Set the callback listener (default: `0.0.0.0:9000`). Add a bot entry with a callback path, Token, and EncodingAESKey. Add at least one admin user, allowed user, or allowed chat.

   ![Configure the WeCom channel in DotCraft Desktop](https://github.com/DotHarness/resources/raw/master/dotcraft/channels/wecom-configuration.png)

4. Save the settings and select **Connect**.
5. In WeCom, set the callback URL to a public HTTPS address that forwards to the DotCraft listener, including the bot's callback path. For a local listener, use an HTTPS reverse proxy or tunnel. Set the Token and EncodingAESKey to match DotCraft.
6. Add the bot to an allowed chat and send a message. DotCraft replies in that chat.

## Using the bot

- DotCraft responds to allowed users and chats. Admin users can run commands such as `/model`. Other allowed users can use the remaining [chat commands](./#commands-in-a-chat). Command choices appear as a numbered list.
- The agent sends text, images, files, and AMR voice messages.

## Related docs

- [WeCom channel configuration](../../configuration/channels#wecom) — callback settings and allowlists.
- [Weixin channel](./weixin) — connect a personal WeChat account by scanning a QR code.
