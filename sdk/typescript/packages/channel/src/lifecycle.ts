/**
 * Lifecycle status and error types for SDK module contracts.
 */

export type LifecycleStatus =
  | "configMissing"
  | "configInvalid"
  | "starting"
  | "ready"
  | "authRequired"
  | "authExpired"
  | "degraded"
  | "stopped";

export type ModuleErrorCode =
  | "configMissing"
  | "configInvalid"
  | "startupFailed"
  | "transportConnectionFailed"
  | "authRequired"
  | "authExpired"
  | "capabilityRegistrationFailed"
  | "unexpectedRuntimeFailure";

export interface ModuleError {
  code: ModuleErrorCode;
  message: string;
  detail?: Record<string, unknown>;
  timestamp: string;
}
