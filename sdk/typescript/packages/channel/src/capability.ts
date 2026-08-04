/**
 * Capability and tool descriptor types for SDK module contracts.
 */
import type { JsonValue } from "@dotcraft/sdk/contracts";

export interface CapabilitySummary {
  hasChannelTools: boolean;
  hasStructuredDelivery: boolean;
  requiresInteractiveSetup: boolean;
  capabilitySetMayVaryByEnvironment: boolean;
}

export interface ChannelToolDisplayDescriptor {
  title?: string;
  subtitle?: string;
  icon?: string;
  [key: string]: unknown;
}

export interface ToolApprovalDescriptor {
  [key: string]: unknown;
  /**
   * Server approval category, for example "file" or "shell".
   */
  kind: string;
  /**
   * Name of the tool argument that contains the primary approval target.
   */
  targetArgument: string;
  /**
   * Optional static operation label forwarded to the approval service.
   */
  operation?: string;
  /**
   * Optional argument name whose runtime value is forwarded as the operation string.
   */
  operationArgument?: string;
  /**
   * Legacy adapter hint accepted for compatibility; policy remains server-owned.
   */
  required?: boolean;
  promptTemplate?: string;
}

export interface ChannelToolDescriptor {
  [key: string]: unknown;
  name: string;
  /**
   * Legacy display label kept for older adapters. New descriptors should use display.title.
   */
  displayName?: string;
  description: string;
  inputSchema: JsonValue;
  outputSchema?: JsonValue;
  display?: ChannelToolDisplayDescriptor;
  approval?: ToolApprovalDescriptor;
  requiresChatContext?: boolean;
  deferLoading?: boolean;
}

export interface DeliveryCapabilityDescriptor {
  supportedKinds: string[];
  supportsGroupDelivery: boolean;
  supportsDirectDelivery: boolean;
}

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
