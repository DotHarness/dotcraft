export { ChannelAdapter } from "./adapter.js";
export type { ChannelAdapterMessageOpts, ChannelAdapterOptions } from "./adapter.js";
export { ModuleChannelAdapter } from "./moduleAdapter.js";
export type {
  LauncherDescriptor,
  ModuleFactory,
  ModuleInstance,
  ModuleInterfaceDescriptor,
  ModuleManifest,
  ModuleTransport,
  ModuleVariant,
  WorkspaceContext,
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
export {
  DECISION_ACCEPT,
  DECISION_ACCEPT_ALWAYS,
  DECISION_ACCEPT_FOR_SESSION,
  DECISION_CANCEL,
  DECISION_DECLINE,
  commandRefPart,
  fileRefPart,
  imageDataUrlPart,
  localImagePart,
  skillRefPart,
  textPart,
} from "@dotcraft/sdk";
export type { InputPart, SenderContext } from "@dotcraft/sdk";
export type {
  AppBinding,
  AppBindingRequestGetResult,
  DynamicToolCallResult,
  DynamicToolContentItem,
  SocialChannelTarget,
} from "@dotcraft/sdk/contracts";
export {
  ConfigValidationError,
  loadJsonConfig,
  resolveConfigPath,
  resolveModuleStatePath,
  resolveModuleTempPath,
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
