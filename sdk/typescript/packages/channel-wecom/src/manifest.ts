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
    shortDescription: "Send and receive messages through a group bot.",
    localizedShortDescription: {
      en: "Send and receive messages through a group bot.",
      "zh-Hans": "通过群机器人收发消息。",
    },
    longDescription:
      "Message the bot in WeCom and DotCraft replies in the same chat. Only whitelisted users and chats get replies. Replies can include text, images, files, and voice.",
    localizedLongDescription: {
      en: "Message the bot in WeCom and DotCraft replies in the same chat. Only whitelisted users and chats get replies. Replies can include text, images, files, and voice.",
      "zh-Hans": "在企业微信中给机器人发消息，DotCraft 会在同一会话中回复，仅回复白名单内的用户和会话。回复可以包含文本、图片、文件和语音。",
    },
    previewPrompt: "Sync this WeCom thread into project memory.",
    localizedPreviewPrompt: {
      en: "Sync this WeCom thread into project memory.",
      "zh-Hans": "把这段企业微信讨论同步到项目记忆中。",
    },
    setupSteps: [
      "In WeCom, create a group bot, turn on callback mode, and note its Token and EncodingAESKey.",
      "In Manage, add the bot with its callback path, Token, and EncodingAESKey, add admin users, whitelisted users, or whitelisted chats, then select Connect.",
      "Set the bot's callback URL to a public HTTPS address that forwards to DotCraft's callback listen port (`9000` by default).",
    ],
    localizedSetupSteps: {
      en: [
        "In WeCom, create a group bot, turn on callback mode, and note its Token and EncodingAESKey.",
        "In Manage, add the bot with its callback path, Token, and EncodingAESKey, add admin users, whitelisted users, or whitelisted chats, then select Connect.",
        "Set the bot's callback URL to a public HTTPS address that forwards to DotCraft's callback listen port (`9000` by default).",
      ],
      "zh-Hans": [
        "在企业微信中创建群机器人，启用回调模式，并记录 Token 和 EncodingAESKey。",
        "在「管理」中添加机器人配置，填写回调路径、Token 和 EncodingAESKey，添加管理员用户、白名单用户或白名单会话，然后点击「连接」。",
        "将机器人的回调 URL 设置为可从公网访问的 HTTPS 地址，并转发到 DotCraft 的回调监听端口（默认为 `9000`）。",
      ],
    },
  },
  packageName: "@dotcraft/channel-wecom",
  configFileName: "wecom.json",
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
    bin: "dotcraft-channel-wecom",
    supportsWorkspaceFlag: true,
    supportsConfigOverrideFlag: true,
  },
};
