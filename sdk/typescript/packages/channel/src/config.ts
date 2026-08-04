/**
 * Configuration descriptor types for SDK module contracts.
 */

export type ConfigFieldKind =
  | "string"
  | "secret"
  | "path"
  | "enum"
  | "boolean"
  | "number"
  | "object"
  | "list";

export interface ConfigDescriptor {
  key: string;
  displayLabel: string;
  description: string;
  required: boolean;
  dataKind: ConfigFieldKind;
  masked: boolean;
  interactiveSetupOnly: boolean;
  advanced?: boolean;
  defaultValue?: unknown;
  enumValues?: string[];
}
