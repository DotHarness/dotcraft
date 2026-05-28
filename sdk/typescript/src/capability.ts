/**
 * Capability and tool descriptor types for SDK module contracts.
 */

export interface CapabilitySummary {
  hasChannelTools: boolean;
  hasStructuredDelivery: boolean;
  requiresInteractiveSetup: boolean;
  capabilitySetMayVaryByEnvironment: boolean;
}

export interface ToolApprovalDescriptor {
  required: boolean;
  promptTemplate?: string;
}

export interface ChannelToolDescriptor {
  name: string;
  displayName: string;
  description: string;
  inputSchema: Record<string, unknown>;
  approval?: ToolApprovalDescriptor;
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
