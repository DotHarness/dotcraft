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
    shortDescription: "Connect DotCraft to Feishu chats and bot workflows.",
    localizedShortDescription: {
      en: "Connect DotCraft to Feishu chats and bot workflows.",
      "zh-Hans": "让 DotCraft 接入飞书会话和机器人工作流。",
    },
    longDescription:
      "Use the Feishu channel to receive workspace events, route messages through DotCraft, and collaborate from Feishu groups with structured delivery.",
    localizedLongDescription: {
      en: "Use the Feishu channel to receive workspace events, route messages through DotCraft, and collaborate from Feishu groups with structured delivery.",
      "zh-Hans": "通过飞书渠道接收工作区事件、转发 DotCraft 消息，并在飞书群聊中以结构化方式协作。",
    },
    previewPrompt: "Summarize this Feishu thread and save the next action to workspace memory.",
    localizedPreviewPrompt: {
      en: "Summarize this Feishu thread and save the next action to workspace memory.",
      "zh-Hans": "总结这段飞书讨论，并把下一步写入工作区记忆。",
    },
  },
  packageName: "@dotcraft/channel-feishu",
  configFileName: "feishu.json",
  supportedTransports: ["websocket"],
  requiresInteractiveSetup: false,
  capabilitySummary: {
    hasChannelTools: true,
    hasStructuredDelivery: true,
    requiresInteractiveSetup: false,
    capabilitySetMayVaryByEnvironment: false,
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
