export { configDescriptors, configGroups } from "./config-descriptors.js";
export { manifest } from "./manifest.js";
export { createModule } from "./module.js";
export {
  OneBotReverseWsServer,
  getAtQQ,
  getImageUrl,
  getPlainText,
  isActionOk,
  normalizeMessageSegments,
  type OneBotAction,
  type OneBotActionResponse,
  type OneBotMessageEvent,
  type OneBotMessageSegment,
} from "./onebot.js";
export { QQPermissionService, normalizeId, normalizeIds, type QQUserRole } from "./permission.js";
export { QQAdapter, validateQQConfig } from "./qq-adapter.js";
export type { QQConfig } from "./qq-config.js";
export { QQMediaError, qqDeliveryCapabilities, sendQQMessage, type QQDeliveryResult } from "./qq-send.js";
export { channelContextForQQEvent, parseQQTarget } from "./target.js";
