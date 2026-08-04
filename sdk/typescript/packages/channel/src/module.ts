/**
 * Module manifest and runtime contract types for SDK modules.
 */

import type { CapabilitySummary } from "./capability.js";
import type { LifecycleStatus, ModuleError } from "./lifecycle.js";

export type ModuleVariant = "standard" | "specialized" | "enterprise" | "other";

export type ModuleTransport = "stdio" | "websocket";

export interface LauncherDescriptor {
  bin: string;
  supportsWorkspaceFlag: boolean;
  supportsConfigOverrideFlag: boolean;
}

export interface WorkspaceContext {
  workspaceRoot: string;
  craftPath: string;
  channelName: string;
  moduleId: string;
  configOverridePath?: string;
}

export interface ModuleInterfaceDescriptor {
  shortDescription?: string;
  localizedShortDescription?: Partial<Record<"en" | "zh-Hans", string>>;
  longDescription?: string;
  localizedLongDescription?: Partial<Record<"en" | "zh-Hans", string>>;
  previewPrompt?: string;
  localizedPreviewPrompt?: Partial<Record<"en" | "zh-Hans", string>>;
}

export interface ModuleManifest {
  moduleId: string;
  channelName: string;
  displayName: string;
  localizedDisplayName?: Partial<Record<"en" | "zh-Hans", string>>;
  interface?: ModuleInterfaceDescriptor;
  packageName: string;
  configFileName: string;
  supportedTransports: ModuleTransport[];
  requiresInteractiveSetup: boolean;
  capabilitySummary: CapabilitySummary;
  channelContractVersion: string;
  supportedChannelProtocolVersions: string[];
  variant: ModuleVariant;
  launcher: LauncherDescriptor;
}

export interface ModuleInstance {
  start(): Promise<void>;
  stop(): Promise<void>;
  onStatusChange(handler: (status: LifecycleStatus, error?: ModuleError) => void): void;
  getStatus(): LifecycleStatus;
  getError(): ModuleError | undefined;
}

export type ModuleFactory = (context: WorkspaceContext) => ModuleInstance;
