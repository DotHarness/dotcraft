# Connect DotCraft to QQ

Connect a QQ account to DotCraft through NapCat or another OneBot v11 gateway.

> [!CAUTION]
> Third-party QQ protocol frameworks can create account risk. Use a dedicated QQ account and review the risk before deployment.

## Quick setup

1. Open the target workspace in DotCraft Desktop.
2. Open **Channels**, then select **QQ**.
3. Set the OneBot listen address. The default is `127.0.0.1:6700`.
4. Enter an access token if NapCat should authenticate to the DotCraft endpoint.
5. Add at least one admin user, allowed user, or allowed group.
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
- A QQ group shares one conversation, with the actual sender recorded on each message.
- Group messages require an @mention by default.
- Empty admin and allowlist settings mean the bot ignores QQ messages.
- Approval replies accept `同意`, `允许`, `yes`, `approve`, `拒绝`, `no`, `reject`, and `deny`.
- Voice, video, and file delivery are available through channel delivery tools.
- Voice and video tools use `filePath` for local files, `fileUrl` for HTTP(S) URLs, and `fileBase64` for raw base64 payloads. The `file` argument accepts only URL and `base64://` sources; use `filePath` for local paths so DotCraft can request file-read approval.
- For local voice, video, and file uploads, DotCraft reads the file before sending it to NapCat. Docker deployments do not need to mount the workspace into the NapCat container.

## Standalone adapter

Run the QQ adapter yourself only when Desktop is not managing the channel process.

```bash
cd sdk/typescript
npm run build --workspace @dotcraft/channel-qq
npx dotcraft-channel-qq --workspace /path/to/workspace
```

Use `--config /custom/qq.json` when the adapter config is not stored at `.craft/qq.json`.

Register the channel as a standalone WebSocket adapter in the shared [channel configuration reference](./reference).

## Reference

See [Channel configuration reference](./reference) for the QQ JSON example, `ExternalChannels` registration, and field table.

## Related docs

- [Channels & Bots](../../features/entry-points/channels)
- [Channel configuration reference](./reference)
- [Channel adapters](../sdks/channels)
- [TypeScript Module Integration](../integrations/typescript-module)
