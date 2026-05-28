/**
 * @dotcraft/sdk — high-level TypeScript SDK for DotCraft.
 */

export {
  APP_BINDING_ERROR_CODES,
  appBindingToolError,
  appBindingUnavailableError,
  DotCraft,
  DotCraftThread,
} from "./dotcraft.js";
export type {
  ApprovalDecision,
  ApprovalHandler,
  AppBindingErrorCode,
  AppBindingAcceptResult,
  AppBindingAttachToolsResult,
  AppBindingManager,
  AppBindingRequestCreateResult,
  AppConnectionStartResult,
  AppConnectionStatus,
  AppHandoff,
  AppInfo,
  AppScopeDescriptor,
  AppToolCatalogEntry,
  DotCraftCapabilityOptions,
  DotCraftLocalOptions,
  DotCraftRemoteOptions,
  DotCraftRunEvent,
  DotCraftRunEventBase,
  DotCraftRunResult,
  DynamicToolBinding,
  DynamicToolCallRequest,
  DynamicToolCallResult,
  DynamicToolHandler,
  EnqueueOptions,
  GetOrCreateThreadOptions,
  InputPart,
  ListThreadOptions,
  QueuedInputResult,
  ReadThreadOptions,
  ResumeThreadOptions,
  RunInput,
  RunOptions,
  SenderContext,
  SessionIdentity,
  StartThreadOptions,
  SubscribeOptions,
  ThreadManager,
  ThreadAppBinding,
  ThreadAppBindingSummary,
  ThreadSubscription,
  UserInputHandler,
} from "./dotcraft.js";
export {
  ApprovalTimeoutError,
  DotCraftError,
  DotCraftSdkError,
  InitializationError,
  ThreadNotActiveError,
  ThreadNotFoundError,
  TurnCancelledError,
  TurnFailedError,
  TurnInProgressError,
} from "./errors.js";
export { HubClientError } from "./hubClient.js";
export {
  DECISION_ACCEPT,
  DECISION_ACCEPT_ALWAYS,
  DECISION_ACCEPT_FOR_SESSION,
  DECISION_CANCEL,
  DECISION_DECLINE,
  commandRefPart,
  fileRefPart,
  imageUrlPart,
  localImagePart,
  skillRefPart,
  textPart,
} from "./models.js";
export type { Unsubscribe } from "./client.js";

export const version = "0.1.6";
export const sdkContractVersion = "1.0.0";
