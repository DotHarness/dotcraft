# Connect DotCraft to Feishu

Connect a Feishu or Lark bot to DotCraft as a [channel](./) with a self-built app and WebSocket event subscription.

## Quick setup

1. In the Feishu developer console, create a self-built app.
2. Enable the Bot capability.
3. Enable event subscription over long connection / WebSocket.
4. Copy the App ID and App Secret.
5. Open the target workspace in DotCraft Desktop.
6. Open **Channels**, select **Feishu**, then select **Connect**.

   ![Connect the Feishu channel from its details page](https://github.com/DotHarness/resources/raw/master/dotcraft/channels/feishu-detail-light.png)

7. Paste the App ID and App Secret, then review the platform and group-message settings.

   ![Configure the Feishu bot in DotCraft Desktop](https://github.com/DotHarness/resources/raw/master/dotcraft/channels/feishu-configuration-light.png)

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

### Official Feishu CLI

Set `feishu.cli.enabled` to `true` to let Feishu-origin conversations use the bundled official Feishu CLI as the configured Bot. Grant the app only the scopes required by the commands you intend to use, and share target resources with the app Bot where Feishu requires it. See the [Channel configuration reference](./reference#feishu) for approvals and command restrictions.

## Standalone adapter

Run the Feishu adapter yourself only when Desktop is not managing the channel process.

```bash
cd sdk/typescript
npm run build --workspace @dotcraft/channel-feishu
npx dotcraft-channel-feishu --workspace /path/to/workspace
```

The standalone `ExternalChannels` registration is in the [channel configuration reference](./reference).

## Related docs

- [Channel configuration reference](./reference) — every field, default, and registration shape for the Feishu config file.
- [Channel adapters](../../developing/sdks/channels) — the adapter base class, its message flow, and the handler contract.
- [Channel Module integration](../../developing/integrations/typescript-module) — the Feishu module is the worked example of that contract.
