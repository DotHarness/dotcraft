import { type ConfigDescriptor } from "@dotcraft/channel";

type LocalizedConfigDescriptor = ConfigDescriptor & {
  localizedDisplayLabel?: Partial<Record<"en" | "zh-Hans", string>>;
  localizedDescription?: Partial<Record<"en" | "zh-Hans", string>>;
};

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
  },
  {
    key: "weixin.apiBaseUrl",
    displayLabel: "Weixin API Base URL",
    description: "Tencent iLink API base URL.",
    localizedDisplayLabel: {
      en: "Weixin API Base URL",
      "zh-Hans": "微信 API 基础地址",
    },
    localizedDescription: {
      en: "Tencent iLink API base URL.",
      "zh-Hans": "腾讯 iLink API 的基础地址。",
    },
    required: true,
    dataKind: "string",
    masked: false,
    interactiveSetupOnly: false,
    advanced: true,
    defaultValue: "https://ilinkai.weixin.qq.com",
  },
  {
    key: "weixin.pollIntervalMs",
    displayLabel: "Poll Interval (ms)",
    description: "Delay between polling cycles after each response.",
    localizedDisplayLabel: {
      en: "Poll Interval (ms)",
      "zh-Hans": "轮询间隔（毫秒）",
    },
    localizedDescription: {
      en: "Delay between polling cycles after each response.",
      "zh-Hans": "每次响应完成后，下一轮轮询开始前的等待时间。",
    },
    required: false,
    dataKind: "number",
    masked: false,
    interactiveSetupOnly: false,
    advanced: true,
    defaultValue: 3000,
  },
  {
    key: "weixin.pollTimeoutMs",
    displayLabel: "Poll Timeout (ms)",
    description: "Long-poll timeout for getUpdates requests.",
    localizedDisplayLabel: {
      en: "Poll Timeout (ms)",
      "zh-Hans": "轮询超时（毫秒）",
    },
    localizedDescription: {
      en: "Long-poll timeout for getUpdates requests.",
      "zh-Hans": "getUpdates 长轮询请求的超时时间。",
    },
    required: false,
    dataKind: "number",
    masked: false,
    interactiveSetupOnly: false,
    advanced: true,
    defaultValue: 30000,
  },
];
