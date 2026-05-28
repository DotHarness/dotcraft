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
    tools?: {
      docs?: {
        enabled?: boolean;
      };
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

/** @deprecated Use FeishuConfig instead. */
export type AppConfig = FeishuConfig;

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

export interface FeishuDocxDocumentInfo {
  documentId: string;
  revisionId: number;
  title: string;
  url: string;
}

export interface FeishuDocxRawContent {
  documentId: string;
  content: string;
}

export interface FeishuDocxBlockInfo {
  blockId: string;
  blockType: number;
  parentId?: string;
  children?: string[];
  textContent?: string;
}

export interface FeishuDocxBlockCreateResult {
  documentId: string;
  revisionId?: number;
  blocks: FeishuDocxBlockInfo[];
}

export interface FeishuDocxBlockListPage {
  documentId: string;
  items: FeishuDocxBlockInfo[];
  nextPageToken?: string;
  hasMore?: boolean;
}

export interface FeishuDocxBlockDeleteResult {
  documentId: string;
  parentBlockId: string;
  startIndex: number;
  endIndex: number;
}

export interface FeishuDocxBlockBatchUpdateResult {
  documentId: string;
  revisionId?: number;
  updatedBlocks: FeishuDocxBlockInfo[];
}

export interface FeishuDriveMediaUploadResult {
  fileToken: string;
  fileName?: string;
  size?: number;
}

export interface FeishuDocxCommentContentElement {
  type: string;
  text?: string;
  mentionUser?: string;
  link?: string;
}

export interface FeishuDocxCommentContent {
  elements: FeishuDocxCommentContentElement[];
}

export interface FeishuDocxCommentReply {
  replyId: string;
  userId?: string;
  createTime?: string;
  updateTime?: string;
  isSolved?: boolean;
  text?: string;
  content?: FeishuDocxCommentContent;
}

export interface FeishuDocxCommentReplyList {
  replies: FeishuDocxCommentReply[];
  hasMore?: boolean;
}

export interface FeishuDocxCommentQuote {
  blockId?: string;
  content?: FeishuDocxCommentContent;
}

export interface FeishuDocxComment {
  commentId: string;
  userId?: string;
  createTime?: string;
  updateTime?: string;
  isSolved?: boolean;
  isWhole?: boolean;
  quote?: FeishuDocxCommentQuote;
  replyList: FeishuDocxCommentReplyList;
}

export interface FeishuDocxCommentListPage {
  fileToken: string;
  items: FeishuDocxComment[];
  nextPageToken?: string;
  hasMore?: boolean;
}

export interface FeishuDocxCommentBatchQueryResult {
  fileToken: string;
  items: FeishuDocxComment[];
}

export interface FeishuDocxCommentReplyListPage {
  fileToken: string;
  commentId: string;
  items: FeishuDocxCommentReply[];
  nextPageToken?: string;
  hasMore?: boolean;
}

export interface FeishuDocxCommentCreateResult {
  fileToken: string;
  commentId: string;
  createTime?: string;
}

export interface FeishuDocxCommentReplyCreateResult {
  fileToken: string;
  commentId: string;
  replyId: string;
  createTime?: string;
}

export type FeishuWikiObjType =
  | "wiki"
  | "doc"
  | "docx"
  | "sheet"
  | "bitable"
  | "mindnote"
  | "file"
  | "slides";

export interface FeishuWikiNodeInfo {
  spaceId: string;
  nodeToken: string;
  objToken: string;
  objType: string;
  nodeType: string;
  parentNodeToken?: string;
  originNodeToken?: string;
  originSpaceId?: string;
  hasChild?: boolean;
  title?: string;
  objCreateTime?: string;
  objEditTime?: string;
  nodeCreateTime?: string;
}

export interface FeishuWikiNodeListPage {
  items: FeishuWikiNodeInfo[];
  nextPageToken?: string;
  hasMore?: boolean;
}

export interface FeishuWikiSpaceInfo {
  spaceId: string;
  name?: string;
  description?: string;
  visibility?: string;
  spaceType?: string;
  openSharing?: string;
}

export interface FeishuWikiSpaceListPage {
  items: FeishuWikiSpaceInfo[];
  nextPageToken?: string;
  hasMore?: boolean;
}

export interface FeishuMoveDocxToWikiResult {
  wikiToken?: string;
  taskId?: string;
  applied?: boolean;
}

export interface FeishuWikiMoveTaskStatus {
  taskId: string;
  status: number;
  statusMessage?: string;
  wikiToken?: string;
  objToken?: string;
  objType?: string;
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
  parts: Record<string, unknown>[];
  messageId: string;
  parentId?: string;
  rootId?: string;
  mentions: FeishuMention[];
  sender: ParsedInboundSender;
}
