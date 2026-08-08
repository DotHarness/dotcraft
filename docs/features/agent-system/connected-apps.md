# Connected Apps

Connected Apps let a conversation use tools from products you already work with.

![Connect an app once for the workspace, then choose it for each conversation](/connected-apps-flow.svg)

First connect the app to the current workspace. Then choose which conversations may use it.

## Connect an app

1. Open **Plugins**, then open the plugin that provides the app.
2. Select **Install**.
3. Review the confirmation, then select **Add to DotCraft**.
4. If setup asks for a companion application, select **Install app**, finish the installation, then select **Refresh**.
5. Select **Connect**.
6. Complete the confirmation in the app, then return to DotCraft.

When setup is complete, the plugin shows the app under **App Settings**. Return there whenever you need to reconnect or disconnect it.

## Use apps in a new conversation

1. Open a new conversation.
2. Select **Apps**.
3. Turn on the apps you want this conversation to use.
4. Send your first message.

DotCraft prepares the selected apps before it sends the first message.

> [!NOTE]
> Connected apps may already be selected in a new conversation. Review the list and turn off anything you do not want to use before sending your first message.

## Change apps in a conversation

Open **Apps** in the conversation header, then turn apps on or off.

This changes only the current conversation. It does not disconnect the app from the workspace or affect other conversations.

## Review additional access

If an app adds new capabilities, **Review** appears beside it.

- Select **Keep previous capabilities** to decline the additional access and retain the previously approved baseline. The app may be unavailable until it reconnects with that access.
- Select **Accept capabilities** to let the conversation use the expanded capability set.

Review the change before accepting it, especially when it adds write actions or access to more data.

## Reconnect or disconnect an app

Open the plugin, then open **App Settings**.

- Select **Reconnect** when the connection has expired or the app asks you to sign in again.
- Open **Connected**, then select **Disconnect** to remove the workspace connection.

> [!CAUTION]
> Turning an app off removes it from one conversation. Disconnecting it in **App Settings** affects every conversation in the current workspace.

For apps used through social channels, follow the channel's binding flow. See [Channels & Bots](../entry-points/channels).

## Related docs

- [Plugins & Tools](./plugins-tools)
- [Plugin marketplaces](./plugin-marketplaces)
- [Channels & Bots](../entry-points/channels)
- [Security & Sandbox](../self-hosted/security)
- [DotCraft App](../../developing/integrations/app-binding)
