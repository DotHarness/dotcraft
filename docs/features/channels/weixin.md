# Connect DotCraft to Weixin

Connect Weixin to DotCraft as a [channel](./) through Tencent iLink. The first login uses a QR code, and normal replies are sent as plain text.

## Quick setup

1. Confirm the Weixin account has Tencent iLink bot access.
2. Open the target workspace in DotCraft Desktop.
3. Open **Channels**, select **WeChat**, then select **Connect**.

   ![Connect the WeChat channel from its details page](https://github.com/DotHarness/resources/raw/master/dotcraft/channels/weixin-detail-light.png)

4. Keep the default iLink API address unless Tencent gave you a different endpoint.

   ![Review the Weixin channel settings in DotCraft Desktop](https://github.com/DotHarness/resources/raw/master/dotcraft/channels/weixin-configuration-light.png)

5. Save the channel and turn it on.
6. Scan the QR code shown in Desktop.
7. Wait until Desktop shows the Weixin channel as connected.
8. Send a message to the Weixin bot account.

## Platform setup details

Weixin setup is authentication-first:

1. Use the Weixin account that Tencent iLink authorizes for bot access.
2. Keep Desktop open while the first QR login completes.
3. Restarting Desktop normally reuses the saved session.
4. Scan a new QR code when Desktop asks you to re-authenticate.

No public callback URL is required for the Desktop-managed Weixin channel.

## Test the connection

1. Send `hello` to the Weixin bot account.
2. Confirm DotCraft replies in the same chat.
3. Send `/new`, then send another message and confirm it starts a fresh conversation.
4. Ask DotCraft to do something that needs approval and reply with an approval keyword.
5. Send a task that returns a file or image if your workflow depends on media delivery.

## What works after setup

- `/new` starts a fresh DotCraft conversation in the current Weixin chat.
- The login session is saved for normal restarts.
- If the session expires, Desktop shows a new QR code.
- Regular replies and media captions are flattened to plain text because iLink does not expose Markdown rendering.
- File and image delivery are available through channel delivery tools.
- Approval replies accept plain chat keywords such as `同意`, `允许`, `yes`, `approve`, `拒绝`, `no`, `reject`, and `deny`. Replying `同意全部` or `approve all` allows the same kind of action for the rest of the session.

## Standalone adapter

Run the Weixin adapter yourself only when Desktop is not managing the channel process.

```bash
cd sdk/typescript
npm run build --workspace @dotcraft/channel-weixin
npx dotcraft-channel-weixin --workspace /path/to/workspace
```

Use `--config /custom/weixin.json` when the adapter config is not stored at `.craft/weixin.json`. In terminal mode, the QR code is rendered in the terminal.

The standalone `ExternalChannels` registration is in the [channel configuration reference](./reference).

## Related docs

- [Channel configuration reference](./reference) — every field, default, and registration shape for the Weixin config file.
- [Channel adapters](../../developing/sdks/channels) — the adapter base class, its message flow, and the handler contract.
- [WeCom channel](./wecom) — enterprise chats take a different route, through a callback URL.
