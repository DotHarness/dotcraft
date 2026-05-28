import { sdkContractVersion } from "@dotcraft/sdk";
import { type ModuleManifest } from "@dotcraft/sdk/channel";

export const manifest: ModuleManifest = {
  moduleId: "telegram-standard",
  channelName: "telegram",
  displayName: "Telegram",
  interface: {
    shortDescription: "Connect DotCraft to Telegram bots and chats.",
    localizedShortDescription: {
      en: "Connect DotCraft to Telegram bots and chats.",
      "zh-Hans": "让 DotCraft 接入 Telegram 机器人和会话。",
    },
    longDescription:
      "Use the Telegram channel to route chat messages into DotCraft, send structured replies, and keep lightweight bot workflows close to your project context.",
    localizedLongDescription: {
      en: "Use the Telegram channel to route chat messages into DotCraft, send structured replies, and keep lightweight bot workflows close to your project context.",
      "zh-Hans": "通过 Telegram 渠道把会话消息接入 DotCraft、发送结构化回复，并让轻量机器人工作流贴近项目上下文。",
    },
    previewPrompt: "Turn this Telegram update into a workspace action item.",
    localizedPreviewPrompt: {
      en: "Turn this Telegram update into a workspace action item.",
      "zh-Hans": "把这条 Telegram 更新整理成工作区行动项。",
    },
  },
  packageName: "@dotcraft/channel-telegram",
  configFileName: "telegram.json",
  supportedTransports: ["websocket"],
  requiresInteractiveSetup: false,
  capabilitySummary: {
    hasChannelTools: true,
    hasStructuredDelivery: true,
    requiresInteractiveSetup: false,
    capabilitySetMayVaryByEnvironment: false,
  },
  sdkContractVersion,
  supportedProtocolVersions: ["0.2"],
  variant: "standard",
  launcher: {
    bin: "dotcraft-channel-telegram",
    supportsWorkspaceFlag: true,
    supportsConfigOverrideFlag: true,
  },
};
