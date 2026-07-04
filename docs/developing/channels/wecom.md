# Connect DotCraft to WeCom

Connect a WeCom group bot to DotCraft with the WeCom callback settings from your enterprise admin console.

## Quick setup

1. Open the target workspace in DotCraft Desktop.
2. Open **Channels**, then select **WeCom**.
3. Set the callback listen host, port, and scheme. The default local listener is `0.0.0.0:9000`.
4. Add a robot entry with a callback path, Token, and EncodingAESKey.
5. Add at least one admin user, allowed user, or allowed chat.
6. Save the channel and turn it on.
7. In WeCom, set the group bot callback URL to the public URL that reaches the DotCraft listener.
8. Paste the same Token and EncodingAESKey into the WeCom bot settings.

Desktop should show the WeCom channel as connected after WeCom verifies the callback.

## Platform setup details

In the WeCom admin console:

1. Create or open the group bot that should call DotCraft.
2. Enable callback mode.
3. Set the callback URL to your public HTTPS endpoint plus the robot path from Desktop.
4. Set Token to the same robot token from Desktop.
5. Set EncodingAESKey to the same robot AES key from Desktop.
6. Add the bot to the chats where DotCraft should respond.

WeCom must reach the callback from the public internet. For local Desktop use, put an HTTPS reverse proxy or tunnel in front of the Desktop listener.

## Test the connection

1. Send a message in an allowed WeCom chat.
2. Confirm DotCraft replies in the same chat.
3. Ask DotCraft to do something that needs approval.
4. Confirm the approval request appears in WeCom and that your reply is accepted.

## What works after setup

- WeCom messages from allowed users or chats can start DotCraft turns.
- Admin users can approve higher-risk actions from WeCom.
- Approval replies accept the normal approve and reject keywords.
- File and image delivery are available through channel delivery tools.
- Messages from users or chats outside the allowlist are ignored.

## Standalone adapter

Run the WeCom adapter yourself only when Desktop is not managing the channel process.

```bash
cd sdk/typescript
npm run build --workspace @dotcraft/channel-wecom
npx dotcraft-channel-wecom --workspace /path/to/workspace
```

Register the channel as a standalone WebSocket adapter in the shared [channel configuration reference](./reference).

## Reference

See [Channel configuration reference](./reference) for the WeCom JSON example, `ExternalChannels` registration, and field table.

## Related docs

- [Channels & Bots](../../features/entry-points/channels)
- [Channel configuration reference](./reference)
- [Weixin channel](./weixin)
- [Channel adapters](../sdks/channels)
