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

export type ConfigLocale = "en" | "zh-Hans" | "ja" | "ko" | "es" | "fr" | "de";

export type ConfigLocalizedText = Partial<Record<ConfigLocale, string>>;

export interface ConfigFieldOption {
  value: string;
  displayLabel: string;
  localizedDisplayLabel?: ConfigLocalizedText;
  description?: string;
  localizedDescription?: ConfigLocalizedText;
  preview?: string;
}

export interface ConfigGroupDescriptor {
  id: string;
  displayLabel: string;
  localizedDisplayLabel?: ConfigLocalizedText;
  description?: string;
  localizedDescription?: ConfigLocalizedText;
}

export interface ConfigDescriptor {
  key: string;
  displayLabel: string;
  description: string;
  localizedDisplayLabel?: ConfigLocalizedText;
  localizedDescription?: ConfigLocalizedText;
  required: boolean;
  dataKind: ConfigFieldKind;
  masked: boolean;
  interactiveSetupOnly: boolean;
  group?: string;
  /** @deprecated Use group with an explicit configGroups entry. */
  advanced?: boolean;
  defaultValue?: unknown;
  options?: ConfigFieldOption[];
  allowCustomValue?: boolean;
  /** @deprecated Use options for localized option metadata. */
  enumValues?: string[];
}
