import { CHANNEL_CONTRACT_VERSION as channelContractVersion } from "@dotcraft/channel/meta";
import { type ModuleManifest } from "@dotcraft/channel";

export const manifest: ModuleManifest = {
  moduleId: "wecom-standard",
  channelName: "wecom",
  displayName: "WeCom",
  localizedDisplayName: {
    en: "WeCom",
    "zh-Hans": "企业微信",
  },
  interface: {
    shortDescription: "Connect DotCraft to WeCom bots and group workflows.",
    localizedShortDescription: {
      en: "Connect DotCraft to WeCom bots and group workflows.",
      "zh-Hans": "让 DotCraft 接入企业微信机器人和群聊工作流。",
    },
    longDescription:
      "Use the WeCom channel to receive enterprise chat events, coordinate workspace tasks from group conversations, and deliver DotCraft replies back to the right teams.",
    localizedLongDescription: {
      en: "Use the WeCom channel to receive enterprise chat events, coordinate workspace tasks from group conversations, and deliver DotCraft replies back to the right teams.",
      "zh-Hans": "通过企业微信渠道接收企业会话事件，从群聊中协调工作区任务，并把 DotCraft 回复投递给对应团队。",
    },
    previewPrompt: "Sync this WeCom thread into project memory.",
    localizedPreviewPrompt: {
      en: "Sync this WeCom thread into project memory.",
      "zh-Hans": "把这段企业微信讨论同步到项目记忆中。",
    },
  },
  packageName: "@dotcraft/channel-wecom",
  configFileName: "wecom.json",
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
    bin: "dotcraft-channel-wecom",
    supportsWorkspaceFlag: true,
    supportsConfigOverrideFlag: true,
  },
};
