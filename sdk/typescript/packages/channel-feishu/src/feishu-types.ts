import type { InputPart } from "@dotcraft/channel";

export interface FeishuConfig {
  dotcraft: {
    wsUrl: string;
    token?: string;
  };
  feishu: {
    appId: string;
    appSecret: string;
    verificationToken?: string;
    encryptKey?: string;
    brand?: "feishu" | "lark";
    cardTitle?: string;
    approvalTimeoutMs?: number;
    groupMentionRequired?: boolean;
    ackReactionEmoji?: string;
    downloadDir?: string;
    cli?: {
      enabled?: boolean;
      /** Feishu user scopes to request; an empty list keeps the CLI on Bot identity only. */
      userScopes?: string[];
    };
    streaming?: {
      /** Use Feishu CardKit typewriter updates when available. Defaults to true. */
      enabled?: boolean;
    };
    /** Debug logging to stderr; only keys set to `true` enable tracing. */
    debug?: {
      /** Verbose `consumeTurnEventStream` traces (adapter stream). */
      adapterStream?: boolean;
      /** Traces inside `mergeReplyTextFromDeltaAndSnapshot`. */
      textMerge?: boolean;
    };
  };
}

export interface FeishuSenderId {
  open_id?: string;
  user_id?: string;
  union_id?: string;
}

export interface FeishuMention {
  key: string;
  id: FeishuSenderId;
  name: string;
  tenant_key?: string;
}

export interface FeishuMessageEvent {
  app_id?: string;
  sender: {
    sender_id: FeishuSenderId;
    sender_type?: string;
    tenant_key?: string;
  };
  message: {
    message_id: string;
    root_id?: string;
    parent_id?: string;
    chat_id: string;
    thread_id?: string;
    chat_type: "p2p" | "group";
    message_type: string;
    content: string;
    create_time?: string;
    mentions?: FeishuMention[];
  };
}

export interface FeishuCardActionEvent {
  app_id?: string;
  event_id?: string;
  token?: string;
  operator?: {
    open_id?: string;
    user_id?: string;
    union_id?: string;
    tenant_key?: string;
  };
  action?: {
    tag?: string;
    value?: Record<string, unknown> | string;
  };
  context?: {
    open_message_id?: string;
    open_chat_id?: string;
  };
}

export interface FeishuBotInfo {
  appName: string;
  botName: string;
  openId: string;
  hasBotIdentity: boolean;
  tenantKey?: string;
  activateStatus?: number;
  rawFieldKeys?: string[];
  diagnosticMessage?: string;
  diagnosticTag?: FeishuBotDiagnosticTag;
}

export interface FeishuSendResult {
  messageId: string;
  chatId: string;
}

export interface FeishuChatInfo {
  chatMode: string;
  groupMessageType: string;
}

export type FeishuBotDiagnosticTag =
  | "missingToken"
  | "botCapabilityDisabled"
  | "identityFieldsMissing";

export interface FeishuReplyOptions {
  replyInThread?: boolean;
  uuid?: string;
}

export interface FeishuListChatMessagesOptions {
  startTime: string;
  endTime?: string;
  pageSize?: number;
  pageToken?: string;
}

export interface FeishuChatMessageSender {
  openId?: string;
  userId?: string;
  unionId?: string;
  senderType?: string;
  tenantKey?: string;
}

export interface FeishuChatMessageItem {
  messageId: string;
  chatId: string;
  chatType?: "p2p" | "group";
  messageType: string;
  createTime?: string;
  parentId?: string;
  rootId?: string;
  sender: FeishuChatMessageSender;
  mentions: FeishuMention[];
  rawContent: string;
}

export interface FeishuChatMessagePage {
  items: FeishuChatMessageItem[];
  nextPageToken?: string;
  hasMore?: boolean;
}

export type FeishuApiErrorKind =
  | "permission"
  | "auth"
  | "invalidArgument"
  | "rateLimited"
  | "upstream"
  | "unknown";

export interface FeishuApiErrorOptions {
  kind: FeishuApiErrorKind;
  message: string;
  retryable: boolean;
  code?: number;
  msg?: string;
  httpStatus?: number;
  raw?: unknown;
  cause?: unknown;
}

export class FeishuApiError extends Error {
  readonly kind: FeishuApiErrorKind;
  readonly retryable: boolean;
  readonly code?: number;
  readonly msg?: string;
  readonly httpStatus?: number;
  readonly raw?: unknown;

  constructor(options: FeishuApiErrorOptions) {
    super(options.message, options.cause !== undefined ? { cause: options.cause } : undefined);
    this.name = "FeishuApiError";
    this.kind = options.kind;
    this.retryable = options.retryable;
    this.code = options.code;
    this.msg = options.msg;
    this.httpStatus = options.httpStatus;
    this.raw = options.raw;
  }
}

export interface ParsedInboundSender {
  openId?: string;
  userId?: string;
  unionId?: string;
}

export interface ParsedInboundMessage {
  kind: "text" | "parts";
  userId: string;
  userName: string;
  threadUserId: string;
  channelContext: string;
  chatId: string;
  chatType: "p2p" | "group";
  text: string;
  parts: InputPart[];
  messageId: string;
  parentId?: string;
  rootId?: string;
  /** Topic root message id when the message belongs to a Feishu topic. */
  threadKey?: string;
  mentions: FeishuMention[];
  sender: ParsedInboundSender;
}
