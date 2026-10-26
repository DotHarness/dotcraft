import { CHANNEL_CONTRACT_VERSION as channelContractVersion } from "@dotcraft/channel/meta";
import { type ModuleManifest } from "@dotcraft/channel";

export const manifest: ModuleManifest = {
  moduleId: "qq-standard",
  channelName: "qq",
  displayName: "QQ",
  interface: {
    shortDescription: "Send and receive messages through NapCat.",
    localizedShortDescription: {
      en: "Send and receive messages through NapCat.",
      "zh-Hans": "通过 NapCat 收发消息。",
    },
    longDescription:
      "Message the bot in QQ and DotCraft replies in the same chat. In groups, mention the bot. Replies can include text, images, files, voice, and video.",
    localizedLongDescription: {
      en: "Message the bot in QQ and DotCraft replies in the same chat. In groups, mention the bot. Replies can include text, images, files, voice, and video.",
      "zh-Hans": "在 QQ 中给机器人发消息，DotCraft 会在同一会话中回复，群聊中需要 @ 机器人。回复可以包含文本、图片、文件、语音和视频。",
    },
    previewPrompt: "Track this QQ group task and record the blockers.",
    localizedPreviewPrompt: {
      en: "Track this QQ group task and record the blockers.",
      "zh-Hans": "跟进这个 QQ 群任务，并记录阻塞点。",
    },
    setupSteps: [
      "Sign in to [NapCat](https://napneko.github.io/) with the QQ account for the bot.",
      "In NapCat's network settings, add a WebSocket client with the URL `ws://127.0.0.1:6700/` and the message format `array`. If you set an access token in Manage, use the same token.",
      "In Manage, add admin users, whitelisted users, or whitelisted groups, then select Connect.",
      "Send the bot a direct message, or mention it in a whitelisted group.",
    ],
    localizedSetupSteps: {
      en: [
        "Sign in to [NapCat](https://napneko.github.io/) with the QQ account for the bot.",
        "In NapCat's network settings, add a WebSocket client with the URL `ws://127.0.0.1:6700/` and the message format `array`. If you set an access token in Manage, use the same token.",
        "In Manage, add admin users, whitelisted users, or whitelisted groups, then select Connect.",
        "Send the bot a direct message, or mention it in a whitelisted group.",
      ],
      "zh-Hans": [
        "在 [NapCat](https://napneko.github.io/) 中登录机器人使用的 QQ 账号。",
        "在 NapCat 网络配置中添加 WebSocket 客户端，地址为 `ws://127.0.0.1:6700/`，消息格式为 `array`。如在「管理」中设置了访问令牌，请填写相同的令牌。",
        "在「管理」中添加管理员用户、白名单用户或白名单群，然后点击「连接」。",
        "向机器人发送私聊消息，或在白名单群中 @ 机器人。",
      ],
    },
  },
  packageName: "@dotcraft/channel-qq",
  configFileName: "qq.json",
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
    bin: "dotcraft-channel-qq",
    supportsWorkspaceFlag: true,
    supportsConfigOverrideFlag: true,
  },
};
