import { CHANNEL_CONTRACT_VERSION as channelContractVersion } from "@dotcraft/channel/meta";
import { type ModuleManifest } from "@dotcraft/channel";

export const manifest: ModuleManifest = {
  moduleId: "weixin-standard",
  channelName: "weixin",
  displayName: "微信",
  localizedDisplayName: {
    en: "WeChat",
    "zh-Hans": "微信",
  },
  interface: {
    shortDescription: "Send and receive messages through WeChat ClawBot.",
    localizedShortDescription: {
      en: "Send and receive messages through WeChat ClawBot.",
      "zh-Hans": "通过微信 ClawBot 收发消息。",
    },
    longDescription:
      "Message DotCraft in the WeChat ClawBot chat and it replies in the same chat. Replies are plain text and can include images and files.",
    localizedLongDescription: {
      en: "Message DotCraft in the WeChat ClawBot chat and it replies in the same chat. Replies are plain text and can include images and files.",
      "zh-Hans": "在微信的「微信 ClawBot」对话中给 DotCraft 发消息，它会在同一对话中回复。回复为纯文本，也可以包含图片和文件。",
    },
    previewPrompt: "Capture this Weixin request and draft a reply.",
    localizedPreviewPrompt: {
      en: "Capture this Weixin request and draft a reply.",
      "zh-Hans": "记录这条微信请求，并准备一段回复。",
    },
    setupSteps: [
      "In WeChat on your phone, turn on WeChat ClawBot under Me › Settings › Plugins.",
      "Select Connect and scan the QR code with WeChat.",
      "Send a message in the WeChat ClawBot chat.",
    ],
    localizedSetupSteps: {
      en: [
        "In WeChat on your phone, turn on WeChat ClawBot under Me › Settings › Plugins.",
        "Select Connect and scan the QR code with WeChat.",
        "Send a message in the WeChat ClawBot chat.",
      ],
      "zh-Hans": [
        "在手机微信的「我 › 设置 › 插件」中启用「微信 ClawBot」。",
        "点击「连接」，使用微信扫描二维码。",
        "在「微信 ClawBot」对话中发送消息。",
      ],
    },
  },
  packageName: "@dotcraft/channel-weixin",
  configFileName: "weixin.json",
  supportedTransports: ["websocket"],
  requiresInteractiveSetup: true,
  capabilitySummary: {
    hasChannelTools: false,
    requiresInteractiveSetup: true,
    capabilitySetMayVaryByEnvironment: false,
  },
  channelContractVersion,
  supportedChannelProtocolVersions: ["0.2"],
  variant: "standard",
  launcher: {
    bin: "dotcraft-channel-weixin",
    supportsWorkspaceFlag: true,
    supportsConfigOverrideFlag: true,
  },
};
