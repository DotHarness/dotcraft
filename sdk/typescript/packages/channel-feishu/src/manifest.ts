import { CHANNEL_CONTRACT_VERSION as channelContractVersion } from "@dotcraft/channel/meta";
import { type ModuleManifest } from "@dotcraft/channel";

export const manifest: ModuleManifest = {
  moduleId: "feishu-standard",
  channelName: "feishu",
  displayName: "飞书",
  localizedDisplayName: {
    en: "Feishu",
    "zh-Hans": "飞书",
  },
  interface: {
    shortDescription: "Send and receive messages through a custom app bot.",
    localizedShortDescription: {
      en: "Send and receive messages through a custom app bot.",
      "zh-Hans": "通过自建应用机器人收发消息。",
    },
    longDescription:
      "Message the bot in Feishu and DotCraft replies in the same chat. In groups, mention the bot. Replies can include formatted text, images, and files, and approvals appear as cards.",
    localizedLongDescription: {
      en: "Message the bot in Feishu and DotCraft replies in the same chat. In groups, mention the bot. Replies can include formatted text, images, and files, and approvals appear as cards.",
      "zh-Hans": "在飞书中给机器人发消息，DotCraft 会在同一会话中回复，群聊中需要 @ 机器人。回复可以包含格式文本、图片和文件，需要审批的操作会以卡片形式出现。",
    },
    previewPrompt: "Summarize this Feishu thread and save the next action to workspace memory.",
    localizedPreviewPrompt: {
      en: "Summarize this Feishu thread and save the next action to workspace memory.",
      "zh-Hans": "总结这段飞书讨论，并把下一步写入工作区记忆。",
    },
    setupSteps: [
      "On the [Feishu Open Platform](https://open.feishu.cn/app), create a custom app, add the bot capability, and grant permissions to read and send messages.",
      "In Manage, enter the app's App ID and App Secret, then select Connect.",
      "Under Events & Callbacks, choose to receive events through a persistent connection and subscribe to the Receive message event.",
      "Publish the app version and add the bot to the chats where you will use it.",
    ],
    localizedSetupSteps: {
      en: [
        "On the [Feishu Open Platform](https://open.feishu.cn/app), create a custom app, add the bot capability, and grant permissions to read and send messages.",
        "In Manage, enter the app's App ID and App Secret, then select Connect.",
        "Under Events & Callbacks, choose to receive events through a persistent connection and subscribe to the Receive message event.",
        "Publish the app version and add the bot to the chats where you will use it.",
      ],
      "zh-Hans": [
        "在[飞书开放平台](https://open.feishu.cn/app)创建企业自建应用，添加机器人能力，并开通消息读取与发送权限。",
        "在「管理」中填写应用的 App ID 和 App Secret，然后点击「连接」。",
        "在应用的「事件与回调」中选择使用长连接接收事件，并订阅「接收消息」事件。",
        "发布应用版本，并将机器人添加到需要使用的会话。",
      ],
    },
  },
  packageName: "@dotcraft/channel-feishu",
  configFileName: "feishu.json",
  supportedTransports: ["websocket"],
  requiresInteractiveSetup: false,
  capabilitySummary: {
    hasChannelTools: true,
    requiresInteractiveSetup: false,
    capabilitySetMayVaryByEnvironment: true,
  },
  channelContractVersion,
  supportedChannelProtocolVersions: ["0.2"],
  variant: "standard",
  launcher: {
    bin: "dotcraft-channel-feishu",
    supportsWorkspaceFlag: true,
    supportsConfigOverrideFlag: true,
  },
};
