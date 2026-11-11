import type { JsonValue } from "@dotcraft/sdk/contracts";

export interface CapabilitySummary {
  hasChannelTools: boolean;
  requiresInteractiveSetup: boolean;
  capabilitySetMayVaryByEnvironment: boolean;
}

export interface ToolApprovalDescriptor {
  [key: string]: unknown;
  kind: string;
  targetArgument: string;
  operation?: string;
  operationArgument?: string;
  /** Ignored by the server, which owns approval policy. */
  required?: boolean;
  promptTemplate?: string;
}

export interface ChannelToolDescriptor {
  [key: string]: unknown;
  name: string;
  description: string;
  inputSchema: JsonValue;
  outputSchema?: JsonValue;
  approval?: ToolApprovalDescriptor;
  requiresChatContext?: boolean;
  deferLoading?: boolean;
}

export type ChannelTextFormat = "plain" | "markdown";

export type ChannelDirectReach = "any" | "knownOnly" | "none";

export type ChannelMediaConstraints = {
  maxBytes?: number;
  allowedMimeTypes?: string[];
  allowedExtensions?: string[];
  supportsHostPath?: boolean;
  supportsUrl?: boolean;
  supportsBase64?: boolean;
  supportsCaption?: boolean;
};

export type ChannelDeliveryCapabilities = {
  text: { format: ChannelTextFormat };
  targets: { group: boolean; direct: ChannelDirectReach };
  media?: {
    image?: ChannelMediaConstraints;
    file?: ChannelMediaConstraints;
    audio?: ChannelMediaConstraints;
    video?: ChannelMediaConstraints;
  };
  /** Present when the adapter serves `ext/channel/history`; at most 50 messages per read. */
  history?: { maxMessages: number };
};

export type ChannelChatKind = "direct" | "group" | "topic";

export type ChannelChatMembership = "shared" | "sender";

export type ChannelChatDescriptor = {
  kind: ChannelChatKind;
  label?: string;
  membership: ChannelChatMembership;
};

export interface ToolInvocationContext {
  tool: string;
  arguments: Record<string, unknown>;
  threadId?: string;
  channelContext?: string;
}

export interface ToolInvocationResult {
  success: boolean;
  result?: unknown;
  errorCode?: string;
  errorMessage?: string;
}
