# Connect DotCraft to Feishu

Connect a Feishu or Lark bot to DotCraft with a self-built app and WebSocket event subscription.

## Quick setup

1. In the Feishu developer console, create a self-built app.
2. Enable the Bot capability.
3. Enable event subscription over long connection / WebSocket.
4. Copy the App ID and App Secret.
5. Open the target workspace in DotCraft Desktop.
6. Open **Channels**, then select **Feishu**.
7. Paste the App ID and App Secret.
8. Save the channel and turn it on.

Desktop should show the Feishu channel as connected after the bot connects to Feishu events.

## Platform setup details

In the Feishu developer console:

1. Add the bot to the chats where DotCraft should respond.
2. Grant message event permissions so DotCraft can receive bot messages.
3. Grant message send permissions so DotCraft can reply with cards.
4. Grant `cardkit:card:write` (**Create and update cards**) so replies can stream with the native typewriter effect.
5. Grant resource permission if users will send images or files to DotCraft.
6. Grant chat metadata permission if the bot needs group context.
7. Grant reaction permission if you want DotCraft to mark handled messages with a reaction.

Native streaming is enabled by default. If the CardKit permission or API is unavailable, DotCraft automatically sends the completed reply with standard cards instead. Set `feishu.streaming.enabled` to `false` to always use standard progressive cards.

Publish or release the app in the target tenant before testing in group chats. Tenant policy can still block events or message sends even when scopes are selected.

## Test the connection

1. Send a direct message to the Feishu bot.
2. Confirm DotCraft replies with a Feishu card.
3. Add the bot to a group and @mention it.
4. Confirm DotCraft replies in the group.
5. Ask DotCraft to do something that needs approval and use the approval card buttons.

## What works after setup

- Direct messages are handled without a mention.
- Group messages require an @mention by default.
- Replies stream into an evolving Feishu card when CardKit is available; standard cards remain the automatic fallback.
- DotCraft can acknowledge handled messages with the configured reaction.
- Image and file input can be downloaded when the app has resource permission.

### Advanced docx and wiki tools

Feishu docx and wiki tools are optional. Enable them only after the app has the required document scopes and the target document, folder, or wiki space is shared with the app bot.

## Standalone adapter

Run the Feishu adapter yourself only when Desktop is not managing the channel process.

```bash
cd sdk/typescript
npm run build --workspace @dotcraft/channel-feishu
npx dotcraft-channel-feishu --workspace /path/to/workspace
```

Register the channel as a standalone WebSocket adapter in the shared [channel configuration reference](./reference).

## Reference

See [Channel configuration reference](./reference) for the Feishu JSON example, `ExternalChannels` registration, and field table.

## Related docs

- [Channels & Bots](../../features/entry-points/channels)
- [Channel configuration reference](./reference)
- [Channel adapters](../sdks/channels)
- [Channel Module integration](../integrations/typescript-module)
