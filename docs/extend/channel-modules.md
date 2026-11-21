# Channel modules

Distribute a [channel adapter](./sdks/channels) as a plugin so DotCraft can install, configure, and run it for a workspace. The module supplies a static descriptor and an executable bundle. The adapter still owns the platform connection and message delivery.

![The host-visible lifecycle of a channel module, from starting through ready and stopped](/typescript-module-lifecycle.svg)

## Declare the channel plugin

Add a `channel` contribution to the [plugin manifest](./plugin-manifest) in `.craft-plugin/plugin.json`:

```json
{
  "schemaVersion": 1,
  "id": "channel-acme",
  "version": "0.1.0",
  "displayName": "Acme Chat",
  "description": "Connects a workspace to Acme Chat.",
  "capabilities": ["channel"],
  "channel": {
    "manifest": "./channel/manifest.json",
    "entry": "./channel/dist/cli.bundle.mjs",
    "minHostVersion": "0.14.0"
  },
  "interface": {
    "displayName": "Acme Chat",
    "shortDescription": "Send and receive messages in Acme Chat",
    "developerName": "Acme",
    "category": "Channels",
    "capabilities": ["Channel"],
    "defaultPrompt": "Summarize the status update from Acme Chat.",
    "websiteUrl": "https://example.com/acme-chat",
    "logo": "./assets/icon.svg",
    "composerIcon": "./assets/icon.svg"
  }
}
```

`manifest` points to the module descriptor. `entry` points to the bundled Node.js launcher. Both files stay inside the plugin root, with no symbolic links along either path. `minHostVersion` is the earliest DotCraft release that supports the bundle.

## Describe the module

Save this descriptor as `channel/manifest.json`:

```json
{
  "moduleId": "acme-chat-standard",
  "channelName": "acme-chat",
  "displayName": "Acme Chat",
  "packageName": "@acme/dotcraft-channel",
  "configFileName": "acme-chat.json",
  "variant": "standard",
  "channelContractVersion": "1.0.0",
  "supportedChannelProtocolVersions": ["0.2"],
  "supportedTransports": ["websocket"],
  "requiresInteractiveSetup": false,
  "capabilitySummary": {
    "hasChannelTools": false,
    "requiresInteractiveSetup": false,
    "capabilitySetMayVaryByEnvironment": false
  },
  "launcher": {
    "bin": "acme-chat",
    "supportsWorkspaceFlag": true,
    "supportsConfigOverrideFlag": false
  }
}
```

`channelName` identifies the messaging route. `moduleId` identifies a particular implementation and its state. Keep both stable across updates. `configFileName` is a non-reserved JSON filename, not a path.

Add `configDescriptors` for editable fields, and `configGroups` to order them. A field uses a dotted `key`, such as `acme.token`, plus a `displayLabel`, `dataKind`, `required`, `masked`, and `interactiveSetupOnly`. Use `dataKind: "secret"` for credentials and `masked: true` for a field that contains credentials inside an object or list. Never ship secret values as defaults. The `dotcraft.*` keys are host-owned and aren't editable. The descriptors are the plugin's settings schema, so don't declare a separate `settings` file.

## Launch and verify

Build a self-contained Node.js bundle. The host passes `--workspace <path>` and sets `DOTCRAFT_CHANNEL_CRAFT_PATH` to the workspace data directory. Use `DOTCRAFT_CHANNEL_WS_URL` and `DOTCRAFT_CHANNEL_WS_TOKEN` for the [AppServer handshake](./protocols/appserver#channel-adapters). Desktop and Docker supply Node.js. A standalone host requires Node.js 22 or later.

Check the packaged plugin:

```bash
dotcraft plugin verify --candidate ./channel-acme --for-publish
```

Install the verified candidate. In **Plugins → Channels**, set up the channel and select **Connect** to test that the bot receives and sends messages.

## Related docs

- [Channel adapters](./sdks/channels) — message routing, AppServer interaction, and platform delivery.
- [Plugin Market](./plugin-market) — publish a plugin through a marketplace.
