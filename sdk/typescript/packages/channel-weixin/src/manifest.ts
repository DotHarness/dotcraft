import { sdkContractVersion } from "@dotcraft/sdk";
import { type ModuleManifest } from "@dotcraft/sdk/channel";

export const manifest: ModuleManifest = {
  moduleId: "weixin-standard",
  channelName: "weixin",
  displayName: "微信",
  localizedDisplayName: {
    en: "WeChat",
    "zh-Hans": "微信",
  },
  interface: {
    shortDescription: "Connect DotCraft to Weixin conversations with guided setup.",
    localizedShortDescription: {
      en: "Connect DotCraft to Weixin conversations with guided setup.",
      "zh-Hans": "通过引导式登录让 DotCraft 接入微信会话。",
    },
    longDescription:
      "Use the Weixin channel to bring personal chat workflows into DotCraft, complete interactive setup when required, and keep conversations connected to workspace actions.",
    localizedLongDescription: {
      en: "Use the Weixin channel to bring personal chat workflows into DotCraft, complete interactive setup when required, and keep conversations connected to workspace actions.",
      "zh-Hans": "通过微信渠道把个人会话工作流接入 DotCraft，在需要时完成交互式登录，并让对话与工作区行动保持连接。",
    },
    previewPrompt: "Capture this Weixin request and draft a reply.",
    localizedPreviewPrompt: {
      en: "Capture this Weixin request and draft a reply.",
      "zh-Hans": "记录这条微信请求，并准备一段回复。",
    },
  },
  packageName: "@dotcraft/channel-weixin",
  configFileName: "weixin.json",
  supportedTransports: ["websocket"],
  requiresInteractiveSetup: true,
  capabilitySummary: {
    hasChannelTools: true,
    hasStructuredDelivery: true,
    requiresInteractiveSetup: true,
    capabilitySetMayVaryByEnvironment: false,
  },
  sdkContractVersion,
  supportedProtocolVersions: ["0.2"],
  variant: "standard",
  launcher: {
    bin: "dotcraft-channel-weixin",
    supportsWorkspaceFlag: true,
    supportsConfigOverrideFlag: true,
  },
};
