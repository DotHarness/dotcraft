import { type ConfigDescriptor, type ConfigGroupDescriptor } from "@dotcraft/channel";

type LocalizedConfigDescriptor = ConfigDescriptor & {
  localizedDisplayLabel?: Partial<Record<"en" | "zh-Hans", string>>;
  localizedDescription?: Partial<Record<"en" | "zh-Hans", string>>;
};

export const configGroups: ConfigGroupDescriptor[] = [
  { id: "configuration", displayLabel: "Configuration", localizedDisplayLabel: { en: "Configuration", "zh-Hans": "配置", ja: "構成", ko: "구성", es: "Configuración", fr: "Configuration", de: "Konfiguration" } },
  { id: "advanced", displayLabel: "Advanced", localizedDisplayLabel: { en: "Advanced", "zh-Hans": "高级", ja: "詳細設定", ko: "고급", es: "Avanzado", fr: "Avancé", de: "Erweitert" } },
];

export const configDescriptors: LocalizedConfigDescriptor[] = [
  {
    key: "dotcraft.wsUrl",
    displayLabel: "AppServer WebSocket URL",
    description: "DotCraft AppServer WebSocket endpoint (ws:// or wss://).",
    localizedDisplayLabel: {
      en: "AppServer WebSocket URL",
      "zh-Hans": "AppServer WebSocket 地址",
    },
    localizedDescription: {
      en: "DotCraft AppServer WebSocket endpoint (ws:// or wss://).",
      "zh-Hans": "DotCraft AppServer 的 WebSocket 端点（ws:// 或 wss://）。",
    },
    required: true,
    dataKind: "string",
    masked: false,
    interactiveSetupOnly: false,
    group: "configuration",
    defaultValue: "ws://127.0.0.1:9100/ws",
  },
  {
    key: "dotcraft.token",
    displayLabel: "AppServer Auth Token",
    description: "Optional token used by DotCraft AppServer WebSocket transport.",
    localizedDisplayLabel: {
      en: "AppServer Auth Token",
      "zh-Hans": "AppServer 认证令牌",
    },
    localizedDescription: {
      en: "Optional token used by DotCraft AppServer WebSocket transport.",
      "zh-Hans": "DotCraft AppServer WebSocket 传输使用的可选认证令牌。",
    },
    required: false,
    dataKind: "secret",
    masked: true,
    interactiveSetupOnly: false,
    group: "configuration",
  },
  {
    key: "telegram.botToken",
    displayLabel: "Telegram Bot Token",
    description: "Bot token issued by BotFather.",
    localizedDisplayLabel: {
      en: "Telegram Bot Token",
      "zh-Hans": "Telegram Bot Token",
    },
    localizedDescription: {
      en: "Bot token issued by BotFather.",
      "zh-Hans": "由 BotFather 签发的 Telegram Bot Token。",
    },
    required: true,
    dataKind: "secret",
    masked: true,
    interactiveSetupOnly: false,
    group: "configuration",
  },
  {
    key: "telegram.httpsProxy",
    displayLabel: "Telegram HTTPS Proxy",
    description: "Optional HTTPS proxy URL used for Telegram Bot API requests.",
    localizedDisplayLabel: {
      en: "Telegram HTTPS Proxy",
      "zh-Hans": "Telegram HTTPS 代理",
    },
    localizedDescription: {
      en: "Optional HTTPS proxy URL used for Telegram Bot API requests.",
      "zh-Hans": "Telegram Bot API 请求使用的可选 HTTPS 代理地址。",
    },
    required: false,
    dataKind: "string",
    masked: false,
    interactiveSetupOnly: false,
    group: "advanced",
  },
  {
    key: "telegram.approvalTimeoutMs",
    displayLabel: "Approval Timeout (ms)",
    description: "Timeout before Telegram approval requests auto-cancel.",
    localizedDisplayLabel: {
      en: "Approval Timeout (ms)",
      "zh-Hans": "审批超时（毫秒）",
    },
    localizedDescription: {
      en: "Timeout before Telegram approval requests auto-cancel.",
      "zh-Hans": "Telegram 审批请求自动取消前的等待时长。",
    },
    required: false,
    dataKind: "number",
    masked: false,
    interactiveSetupOnly: false,
    group: "advanced",
    defaultValue: 120000,
  },
  {
    key: "telegram.pollTimeoutMs",
    displayLabel: "Poll Timeout (ms)",
    description: "Long-poll timeout for Telegram getUpdates requests.",
    localizedDisplayLabel: {
      en: "Poll Timeout (ms)",
      "zh-Hans": "轮询超时（毫秒）",
    },
    localizedDescription: {
      en: "Long-poll timeout for Telegram getUpdates requests.",
      "zh-Hans": "Telegram getUpdates 长轮询请求的超时时间。",
    },
    required: false,
    dataKind: "number",
    masked: false,
    interactiveSetupOnly: false,
    group: "advanced",
    defaultValue: 30000,
  },
];
