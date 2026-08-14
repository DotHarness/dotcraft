import { type ConfigDescriptor } from "@dotcraft/channel";

type LocalizedConfigDescriptor = ConfigDescriptor & {
  localizedDisplayLabel?: Partial<Record<"en" | "zh-Hans", string>>;
  localizedDescription?: Partial<Record<"en" | "zh-Hans", string>>;
  enumValues?: string[];
};

export const configDescriptors: LocalizedConfigDescriptor[] = [
  {
    key: "dotcraft.wsUrl",
    displayLabel: "DotCraft WebSocket URL",
    description: "AppServer WebSocket endpoint (ws:// or wss://).",
    localizedDisplayLabel: {
      en: "DotCraft WebSocket URL",
      "zh-Hans": "DotCraft WebSocket 地址",
    },
    localizedDescription: {
      en: "AppServer WebSocket endpoint (ws:// or wss://).",
      "zh-Hans": "AppServer 的 WebSocket 端点（ws:// 或 wss://）。",
    },
    required: true,
    dataKind: "string",
    masked: false,
    interactiveSetupOnly: false,
    defaultValue: "ws://127.0.0.1:9100/ws",
  },
  {
    key: "dotcraft.token",
    displayLabel: "DotCraft Token",
    description: "Optional bearer token for the DotCraft AppServer.",
    localizedDisplayLabel: {
      en: "DotCraft Token",
      "zh-Hans": "DotCraft 令牌",
    },
    localizedDescription: {
      en: "Optional bearer token for the DotCraft AppServer.",
      "zh-Hans": "DotCraft AppServer 的可选 Bearer 令牌。",
    },
    required: false,
    dataKind: "secret",
    masked: true,
    interactiveSetupOnly: false,
  },
  {
    key: "feishu.appId",
    displayLabel: "Feishu App ID",
    description: "App ID for Feishu/Lark bot.",
    localizedDisplayLabel: {
      en: "Feishu App ID",
      "zh-Hans": "飞书应用 ID",
    },
    localizedDescription: {
      en: "App ID for Feishu/Lark bot.",
      "zh-Hans": "飞书 / Lark 机器人的应用 ID。",
    },
    required: true,
    dataKind: "string",
    masked: false,
    interactiveSetupOnly: false,
  },
  {
    key: "feishu.appSecret",
    displayLabel: "Feishu App Secret",
    description: "App secret for Feishu/Lark bot.",
    localizedDisplayLabel: {
      en: "Feishu App Secret",
      "zh-Hans": "飞书应用 Secret",
    },
    localizedDescription: {
      en: "App secret for Feishu/Lark bot.",
      "zh-Hans": "飞书 / Lark 机器人的应用 Secret。",
    },
    required: true,
    dataKind: "secret",
    masked: true,
    interactiveSetupOnly: false,
  },
  {
    key: "feishu.brand",
    displayLabel: "Platform",
    description: "Select the Feishu or Lark service environment.",
    localizedDisplayLabel: {
      en: "Platform",
      "zh-Hans": "服务平台",
    },
    localizedDescription: {
      en: "Select the Feishu or Lark service environment.",
      "zh-Hans": "选择接入的服务环境：飞书或 Lark。",
    },
    required: false,
    dataKind: "enum",
    masked: false,
    interactiveSetupOnly: false,
    advanced: true,
    defaultValue: "feishu",
    enumValues: ["feishu", "lark"],
  },
  {
    key: "feishu.cardTitle",
    displayLabel: "Reply Card Title",
    description: 'Brand text shown on card headers. Leave empty to use "DotCraft".',
    localizedDisplayLabel: {
      en: "Reply Card Title",
      "zh-Hans": "回复卡片标题",
    },
    localizedDescription: {
      en: 'Brand text shown on reply/progress/transcript card headers. Leave empty to use "DotCraft".',
      "zh-Hans": '回复、进度与流式卡片头部显示的品牌文本。留空则回退为 "DotCraft"。',
    },
    required: false,
    dataKind: "string",
    masked: false,
    interactiveSetupOnly: false,
    advanced: true,
    defaultValue: "DotCraft",
  },
  {
    key: "feishu.streaming.enabled",
    displayLabel: "Native Streaming Cards",
    description: "Use Feishu CardKit typewriter updates and fall back to standard cards when unavailable.",
    localizedDisplayLabel: {
      en: "Native Streaming Cards",
      "zh-Hans": "原生流式卡片",
    },
    localizedDescription: {
      en: "Use Feishu CardKit typewriter updates and fall back to standard cards when unavailable.",
      "zh-Hans": "使用飞书 CardKit 打字机式更新；能力不可用时自动回退为普通卡片。",
    },
    required: false,
    dataKind: "boolean",
    masked: false,
    interactiveSetupOnly: false,
    advanced: true,
    defaultValue: true,
  },
  {
    key: "feishu.approvalTimeoutMs",
    displayLabel: "Approval Timeout (ms)",
    description: "Timeout before approval requests auto-cancel.",
    localizedDisplayLabel: {
      en: "Approval Timeout (ms)",
      "zh-Hans": "审批超时（毫秒）",
    },
    localizedDescription: {
      en: "Timeout before approval requests auto-cancel.",
      "zh-Hans": "审批请求自动取消前的等待时长。",
    },
    required: false,
    dataKind: "number",
    masked: false,
    interactiveSetupOnly: false,
    advanced: true,
    defaultValue: 120000,
  },
  {
    key: "feishu.groupMentionRequired",
    displayLabel: "Require @mention in Groups",
    description: "Only process group messages that mention the bot.",
    localizedDisplayLabel: {
      en: "Require @mention in Groups",
      "zh-Hans": "群聊中必须 @机器人",
    },
    localizedDescription: {
      en: "Only process group messages that mention the bot.",
      "zh-Hans": "仅处理群聊里明确 @ 机器人的消息。",
    },
    required: false,
    dataKind: "boolean",
    masked: false,
    interactiveSetupOnly: false,
    defaultValue: true,
  },
  {
    key: "feishu.ackReactionEmoji",
    displayLabel: "Ack Reaction Emoji",
    description: "Emoji type used for inbound acknowledgement.",
    localizedDisplayLabel: {
      en: "Ack Reaction Emoji",
      "zh-Hans": "确认表情",
    },
    localizedDescription: {
      en: "Emoji type used for inbound acknowledgement.",
      "zh-Hans": "收到消息后用于确认的表情类型。",
    },
    required: false,
    dataKind: "string",
    masked: false,
    interactiveSetupOnly: false,
    advanced: true,
    defaultValue: "GLANCE",
  },
  {
    key: "feishu.downloadDir",
    displayLabel: "Download Directory",
    description: "Local directory for downloaded message attachments.",
    localizedDisplayLabel: {
      en: "Download Directory",
      "zh-Hans": "下载目录",
    },
    localizedDescription: {
      en: "Local directory for downloaded message attachments.",
      "zh-Hans": "下载消息附件到本地时使用的目录。",
    },
    required: false,
    dataKind: "path",
    masked: false,
    interactiveSetupOnly: false,
    advanced: true,
  },
  {
    key: "feishu.tools.docs.enabled",
    displayLabel: "Enable Feishu Docx Tools",
    description:
      "Register Feishu docx and wiki tools (create/read/edit/media/wiki move/rename/comments) for this channel.",
    localizedDisplayLabel: {
      en: "Enable Feishu Docx Tools",
      "zh-Hans": "启用飞书文档工具",
    },
    localizedDescription: {
      en: "Register Feishu docx and wiki tools (create/read/edit/media/wiki move/rename/comments) for this channel.",
      "zh-Hans": "为当前飞书渠道注册文档与知识库工具（创建、读取、编辑、媒体嵌入、知识库迁移/重命名、评论能力）。",
    },
    required: false,
    dataKind: "boolean",
    masked: false,
    interactiveSetupOnly: false,
    defaultValue: false,
  },
  {
    key: "feishu.debug.adapterStream",
    displayLabel: "Debug Adapter Stream",
    description: "Enable detailed adapter stream diagnostics.",
    localizedDisplayLabel: {
      en: "Debug Adapter Stream",
      "zh-Hans": "调试适配器流",
    },
    localizedDescription: {
      en: "Enable detailed adapter stream diagnostics.",
      "zh-Hans": "启用详细的适配器流诊断日志。",
    },
    required: false,
    dataKind: "boolean",
    masked: false,
    interactiveSetupOnly: false,
    advanced: true,
    defaultValue: false,
  },
  {
    key: "feishu.debug.textMerge",
    displayLabel: "Debug Text Merge",
    description: "Enable transcript merge debug logs.",
    localizedDisplayLabel: {
      en: "Debug Text Merge",
      "zh-Hans": "调试文本合并",
    },
    localizedDescription: {
      en: "Enable transcript merge debug logs.",
      "zh-Hans": "启用回复文本合并的调试日志。",
    },
    required: false,
    dataKind: "boolean",
    masked: false,
    interactiveSetupOnly: false,
    advanced: true,
    defaultValue: false,
  },
];
