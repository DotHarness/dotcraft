import { CHANNEL_CONTRACT_VERSION as channelContractVersion } from "@dotcraft/channel/meta";
import { type ModuleManifest } from "@dotcraft/channel";

export const manifest: ModuleManifest = {
  moduleId: "telegram-standard",
  channelName: "telegram",
  displayName: "Telegram",
  interface: {
    shortDescription: "Send and receive messages through a BotFather bot.",
    localizedShortDescription: {
      en: "Send and receive messages through a BotFather bot.",
      "zh-Hans": "通过 BotFather 机器人收发消息。",
    },
    longDescription:
      "Message the bot in Telegram and DotCraft replies in the same chat. Replies can include formatted text, images, files, audio, and video.",
    localizedLongDescription: {
      en: "Message the bot in Telegram and DotCraft replies in the same chat. Replies can include formatted text, images, files, audio, and video.",
      "zh-Hans": "在 Telegram 中给机器人发消息，DotCraft 会在同一会话中回复。回复可以包含格式文本、图片、文件、音频和视频。",
    },
    previewPrompt: "Turn this Telegram update into a workspace action item.",
    localizedPreviewPrompt: {
      en: "Turn this Telegram update into a workspace action item.",
      "zh-Hans": "把这条 Telegram 更新整理成工作区行动项。",
    },
    setupSteps: [
      "In Telegram, send `/newbot` to [@BotFather](https://t.me/BotFather) and follow the prompts to get a bot token.",
      "In Manage, enter the bot token, then select Connect.",
      "Send the bot a message. To use it in a group, add the bot to the group.",
    ],
    localizedSetupSteps: {
      en: [
        "In Telegram, send `/newbot` to [@BotFather](https://t.me/BotFather) and follow the prompts to get a bot token.",
        "In Manage, enter the bot token, then select Connect.",
        "Send the bot a message. To use it in a group, add the bot to the group.",
      ],
      "zh-Hans": [
        "在 Telegram 中向 [@BotFather](https://t.me/BotFather) 发送 `/newbot`，按提示创建机器人并获取 token。",
        "在「管理」中填写 bot token，然后点击「连接」。",
        "向机器人发送消息。如需在群聊中使用，请将机器人加入群聊。",
      ],
    },
  },
  packageName: "@dotcraft/channel-telegram",
  configFileName: "telegram.json",
  supportedTransports: ["websocket"],
  requiresInteractiveSetup: false,
  capabilitySummary: {
    hasChannelTools: false,
    requiresInteractiveSetup: false,
    capabilitySetMayVaryByEnvironment: false,
  },
  channelContractVersion,
  supportedChannelProtocolVersions: ["0.2"],
  variant: "standard",
  launcher: {
    bin: "dotcraft-channel-telegram",
    supportsWorkspaceFlag: true,
    supportsConfigOverrideFlag: true,
  },
};
