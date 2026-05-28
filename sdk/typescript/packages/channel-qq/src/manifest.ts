import { sdkContractVersion } from "@dotcraft/sdk";
import { type ModuleManifest } from "@dotcraft/sdk/channel";

export const manifest: ModuleManifest = {
  moduleId: "qq-standard",
  channelName: "qq",
  displayName: "QQ",
  interface: {
    shortDescription: "Connect DotCraft to QQ groups through OneBot.",
    localizedShortDescription: {
      en: "Connect DotCraft to QQ groups through OneBot.",
      "zh-Hans": "通过 OneBot 让 DotCraft 接入 QQ 群聊。",
    },
    longDescription:
      "Use the QQ channel to let DotCraft receive group messages, respond through the configured OneBot gateway, and keep team conversations connected to your workspace.",
    localizedLongDescription: {
      en: "Use the QQ channel to let DotCraft receive group messages, respond through the configured OneBot gateway, and keep team conversations connected to your workspace.",
      "zh-Hans": "通过 QQ 渠道让 DotCraft 接收群消息、经由已配置的 OneBot 网关回复，并把团队讨论连接到工作区。",
    },
    previewPrompt: "Track this QQ group task and record the blockers.",
    localizedPreviewPrompt: {
      en: "Track this QQ group task and record the blockers.",
      "zh-Hans": "跟进这个 QQ 群任务，并记录阻塞点。",
    },
  },
  packageName: "@dotcraft/channel-qq",
  configFileName: "qq.json",
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
    bin: "dotcraft-channel-qq",
    supportsWorkspaceFlag: true,
    supportsConfigOverrideFlag: true,
  },
};
