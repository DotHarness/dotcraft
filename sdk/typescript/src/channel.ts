/**
 * External channel adapter and hosted module runtime.
 */

export { ChannelAdapter } from "./adapter.js";
export type { ChannelAdapterMessageOpts, ChannelAdapterOptions } from "./adapter.js";
export {
  ApprovalDispatcher,
  ChannelMessageQueue,
  ChannelToolDispatcher,
  CommandRouter,
  ConfigValidationError,
  DefaultSegmentBoundaryPolicy,
  DeliveryDispatcher,
  ModuleConfigLoader,
  ModuleLifecycleState,
  ThreadResolver,
  TurnStreamReducer,
  UserInputDispatcher,
  applyChannelRuntimeDefaults,
  buildChannelSender,
  isStdioChannelRuntime,
  isWebSocketChannelRuntime,
  loadJsonConfig,
  replaceLeadingSlashTextWithCommandRef,
  resolveConfigPath,
  resolveModuleStatePath,
  resolveModuleTempPath,
  withStdioRuntimeDefaults,
  withWebSocketRuntimeDefaults,
} from "./channelRuntime.js";
export type {
  ChannelAdapterMessageOptions,
  ChannelSenderContext,
  ChannelMessageJob,
  ChannelMessageQueueOptions,
  CommandRouteBeforeQueueResult,
  CommandRouteForTurnResult,
  CommandRouterOptions,
  DeliveryDispatcherOptions,
  ApprovalDispatcherOptions,
  ChannelToolDispatcherOptions,
  LoadJsonConfigResult,
  ModuleConfigLoadResult,
  ModuleConfigLoaderOptions,
  SegmentBoundaryPolicy,
  ThreadIdentityLookup,
  ThreadResolveEvent,
  ThreadResolveEventAction,
  ThreadResolverOptions,
  TurnStreamContext,
  TurnStreamDebugLogger,
  TurnStreamReducerHandlers,
  TurnStreamReducerOptions,
  UserInputDispatcherOptions,
} from "./channelRuntime.js";
export {
  buildUserInputPrompt,
  canUseNativeSingleChoiceUserInput,
  emptyUserInputResponse,
  hasUserInputAnswer,
  mergeUserInputResponses,
  normalizeUserInputQuestions,
  splitUserInputRequestByQuestion,
  userInputResponseForSingleChoice,
  userInputResponseFromText,
} from "./userInput.js";
export type {
  UserInputAnswer,
  UserInputPromptOptions,
  UserInputQuestion,
  UserInputQuestionOption,
  UserInputQuestionRequest,
  UserInputResponse,
} from "./userInput.js";
export {
  ModuleChannelAdapter,
} from "./moduleAdapter.js";
export type {
  ModuleFactory,
  ModuleInterfaceDescriptor,
  ModuleInstance,
  ModuleManifest,
  ModuleTransport,
  ModuleVariant,
  WorkspaceContext,
  LauncherDescriptor,
} from "./module.js";
export type { ConfigDescriptor, ConfigFieldKind } from "./config.js";
export type { LifecycleStatus, ModuleError, ModuleErrorCode } from "./lifecycle.js";
export type {
  CapabilitySummary,
  ChannelToolDisplayDescriptor,
  ChannelToolDescriptor,
  DeliveryCapabilityDescriptor,
  ToolApprovalDescriptor,
  ToolInvocationContext,
  ToolInvocationResult,
} from "./capability.js";
export { shouldFlushSegmentOnItemStarted } from "./segmentBoundaries.js";
export { getDeliveredFrontier } from "./deliveredFrontier.js";
export {
  configureTextMergeDebug,
  extractAgentReplyTextFromTurnCompletedParams,
  extractAgentReplyTextsFromTurnCompletedParams,
  mergeReplyTextFromDeltaAndSnapshot,
} from "./turnReply.js";
