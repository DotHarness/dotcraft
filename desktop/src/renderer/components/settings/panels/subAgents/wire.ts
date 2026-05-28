export const PRESET_PROFILE_NAMES = ['native', 'codex-cli', 'cursor-cli'] as const
export type PresetProfileName = (typeof PRESET_PROFILE_NAMES)[number]
export const PERMISSION_MODE_KEYS = ['interactive', 'auto-approve', 'restricted'] as const
export type PermissionModeKey = (typeof PERMISSION_MODE_KEYS)[number]

export function isPresetProfileName(name: string): name is PresetProfileName {
  return (PRESET_PROFILE_NAMES as readonly string[]).includes(name)
}

export interface SubAgentProfileWriteWire {
  runtime: string
  bin?: string | null
  args?: string[] | null
  env?: Record<string, string> | null
  envPassthrough?: string[] | null
  workingDirectoryMode: string
  supportsStreaming?: boolean | null
  supportsResume?: boolean | null
  supportsModelSelection?: boolean | null
  inputFormat?: string | null
  outputFormat?: string | null
  inputMode?: string | null
  inputArgTemplate?: string | null
  inputEnvKey?: string | null
  resumeArgTemplate?: string | null
  resumeSessionIdJsonPath?: string | null
  resumeSessionIdRegex?: string | null
  outputJsonPath?: string | null
  outputInputTokensJsonPath?: string | null
  outputOutputTokensJsonPath?: string | null
  outputTotalTokensJsonPath?: string | null
  outputFileArgTemplate?: string | null
  readOutputFile?: boolean | null
  deleteOutputFileAfterRead?: boolean | null
  maxOutputBytes?: number | null
  timeout?: number | null
  trustLevel?: string | null
  permissionModeMapping?: Record<string, string> | null
  sanitizationRules?: Record<string, unknown> | null
}

export interface SubAgentProfileDiagnosticWire {
  enabled: boolean
  binaryResolved: boolean
  hiddenFromPrompt: boolean
  hiddenReason?: string | null
  warnings: string[]
}

export interface SubAgentProfileEntryWire {
  name: string
  isBuiltIn: boolean
  isTemplate: boolean
  hasWorkspaceOverride: boolean
  isDefault: boolean
  enabled: boolean
  definition: SubAgentProfileWriteWire
  builtInDefaults?: SubAgentProfileWriteWire | null
  diagnostic: SubAgentProfileDiagnosticWire
}

export interface SubAgentProfileListResult {
  profiles: SubAgentProfileEntryWire[]
  defaultName: string
  settings: SubAgentSettingsWire
}

export interface SubAgentSettingsWire {
  externalCliSessionResumeEnabled: boolean
  model?: string | null
}

export const DEFAULT_CUSTOM_TIMEOUT_SECONDS = 300
export const DEFAULT_CUSTOM_MAX_OUTPUT_BYTES = 1_048_576

export function createDefaultWriteWire(): SubAgentProfileWriteWire {
  return {
    runtime: 'cli-oneshot',
    bin: '',
    args: [],
    env: null,
    envPassthrough: [],
    workingDirectoryMode: 'workspace',
    supportsStreaming: false,
    supportsResume: false,
    supportsModelSelection: false,
    inputFormat: null,
    outputFormat: 'text',
    inputMode: 'arg',
    inputArgTemplate: null,
    inputEnvKey: null,
    resumeArgTemplate: null,
    resumeSessionIdJsonPath: null,
    resumeSessionIdRegex: null,
    outputJsonPath: null,
    outputInputTokensJsonPath: null,
    outputOutputTokensJsonPath: null,
    outputTotalTokensJsonPath: null,
    outputFileArgTemplate: '--output-last-message {path}',
    readOutputFile: true,
    deleteOutputFileAfterRead: true,
    maxOutputBytes: DEFAULT_CUSTOM_MAX_OUTPUT_BYTES,
    timeout: DEFAULT_CUSTOM_TIMEOUT_SECONDS,
    trustLevel: 'prompt',
    permissionModeMapping: null,
    sanitizationRules: null
  }
}

export interface CustomEditorState {
  name: string
  runtime: string
  bin: string
  workingDirectoryMode: string
  inputMode: string
  inputArgTemplate: string
  inputEnvKey: string
  resumeArgTemplate: string
  resumeSessionIdJsonPath: string
  resumeSessionIdRegex: string
  inputFormat: string
  outputFormat: string
  outputJsonPath: string
  outputFileArgTemplate: string
  outputInputTokensJsonPath: string
  outputOutputTokensJsonPath: string
  outputTotalTokensJsonPath: string
  readOutputFile: boolean
  deleteOutputFileAfterRead: boolean
  supportsStreaming: boolean
  supportsResume: boolean
  supportsModelSelection: boolean
  maxOutputBytes: string
  timeout: string
  trustLevel: string
  permissionModeMapping: Record<PermissionModeKey, string>
  sanitizationRulesText: string
}

export function createCustomEditorState(
  name: string,
  definition: SubAgentProfileWriteWire
): CustomEditorState {
  const source = { ...createDefaultWriteWire(), ...definition }
  return {
    name,
    runtime: source.runtime ?? 'cli-oneshot',
    bin: source.bin ?? '',
    workingDirectoryMode: source.workingDirectoryMode ?? 'workspace',
    inputMode: source.inputMode ?? 'arg',
    inputArgTemplate: source.inputArgTemplate ?? '',
    inputEnvKey: source.inputEnvKey ?? '',
    resumeArgTemplate: source.resumeArgTemplate ?? '',
    resumeSessionIdJsonPath: source.resumeSessionIdJsonPath ?? '',
    resumeSessionIdRegex: source.resumeSessionIdRegex ?? '',
    inputFormat: source.inputFormat ?? '',
    outputFormat: source.outputFormat ?? 'text',
    outputJsonPath: source.outputJsonPath ?? '',
    outputFileArgTemplate: source.outputFileArgTemplate ?? '',
    outputInputTokensJsonPath: source.outputInputTokensJsonPath ?? '',
    outputOutputTokensJsonPath: source.outputOutputTokensJsonPath ?? '',
    outputTotalTokensJsonPath: source.outputTotalTokensJsonPath ?? '',
    readOutputFile: source.readOutputFile === true,
    deleteOutputFileAfterRead: source.deleteOutputFileAfterRead === true,
    supportsStreaming: source.supportsStreaming === true,
    supportsResume: source.supportsResume === true,
    supportsModelSelection: source.supportsModelSelection === true,
    maxOutputBytes: source.maxOutputBytes != null ? String(source.maxOutputBytes) : '',
    timeout: source.timeout != null ? String(source.timeout) : '',
    trustLevel: source.trustLevel ?? '',
    permissionModeMapping: extractPermissionModeMapping(source.permissionModeMapping),
    sanitizationRulesText:
      source.sanitizationRules != null ? JSON.stringify(source.sanitizationRules, null, 2) : ''
  }
}

function extractPermissionModeMapping(
  source?: Record<string, string> | null
): Record<PermissionModeKey, string> {
  const base: Record<PermissionModeKey, string> = {
    interactive: '',
    'auto-approve': '',
    restricted: ''
  }
  if (!source) return base
  for (const key of PERMISSION_MODE_KEYS) {
    const value = source[key]
    if (typeof value === 'string') {
      base[key] = value
    }
  }
  return base
}

export interface BuildCustomResult {
  ok: true
  definition: SubAgentProfileWriteWire
}

export interface BuildFailure {
  ok: false
  error: string
}

export function buildCustomWriteWire(
  draft: CustomEditorState,
  args: string[],
  env: Record<string, string>,
  envPassthrough: string[],
  t: (key: string, vars?: Record<string, string | number>) => string
): BuildCustomResult | BuildFailure {
  const sanitizationRules = parseJsonObject(draft.sanitizationRulesText, t)
  if (!sanitizationRules.ok) {
    return { ok: false, error: sanitizationRules.error }
  }

  const maxOutputBytes = parseOptionalInteger(draft.maxOutputBytes, t)
  if (!maxOutputBytes.ok) {
    return { ok: false, error: maxOutputBytes.error }
  }

  const timeout = parseOptionalInteger(draft.timeout, t)
  if (!timeout.ok) {
    return { ok: false, error: timeout.error }
  }

  const permissionModeMapping = buildPermissionModeMapping(draft.permissionModeMapping)

  return {
    ok: true,
    definition: {
      runtime: draft.runtime.trim() || 'cli-oneshot',
      bin: nullableString(draft.bin),
      args: args.length > 0 ? args : null,
      env: Object.keys(env).length > 0 ? env : null,
      envPassthrough: envPassthrough.length > 0 ? envPassthrough : null,
      workingDirectoryMode: draft.workingDirectoryMode.trim() || 'workspace',
      supportsStreaming: draft.supportsStreaming,
      supportsResume: draft.supportsResume,
      supportsModelSelection: draft.supportsModelSelection,
      inputFormat: nullableString(draft.inputFormat),
      outputFormat: nullableString(draft.outputFormat),
      inputMode: nullableString(draft.inputMode),
      inputArgTemplate: nullableString(draft.inputArgTemplate),
      inputEnvKey: nullableString(draft.inputEnvKey),
      resumeArgTemplate: nullableString(draft.resumeArgTemplate),
      resumeSessionIdJsonPath: nullableString(draft.resumeSessionIdJsonPath),
      resumeSessionIdRegex: nullableString(draft.resumeSessionIdRegex),
      outputJsonPath: nullableString(draft.outputJsonPath),
      outputInputTokensJsonPath: nullableString(draft.outputInputTokensJsonPath),
      outputOutputTokensJsonPath: nullableString(draft.outputOutputTokensJsonPath),
      outputTotalTokensJsonPath: nullableString(draft.outputTotalTokensJsonPath),
      outputFileArgTemplate: nullableString(draft.outputFileArgTemplate),
      readOutputFile: draft.readOutputFile,
      deleteOutputFileAfterRead: draft.deleteOutputFileAfterRead,
      maxOutputBytes: maxOutputBytes.value,
      timeout: timeout.value,
      trustLevel: nullableString(draft.trustLevel),
      permissionModeMapping,
      sanitizationRules: sanitizationRules.value
    }
  }
}

export function validateCustomEditorState(
  draft: CustomEditorState,
  t: (key: string, vars?: Record<string, string | number>) => string
): string | null {
  if (draft.name.trim().length === 0) {
    return t('settings.subAgents.validation.nameRequired')
  }
  if (draft.runtime === 'cli-oneshot' && draft.bin.trim().length === 0) {
    return t('settings.subAgents.validation.binRequired')
  }
  if (draft.inputMode === 'arg-template' && draft.inputArgTemplate.trim().length === 0) {
    return t('settings.subAgents.validation.inputArgTemplateRequired')
  }
  if (draft.inputMode === 'env' && draft.inputEnvKey.trim().length === 0) {
    return t('settings.subAgents.validation.inputEnvKeyRequired')
  }
  if (draft.outputFormat === 'json' && draft.outputJsonPath.trim().length === 0) {
    return t('settings.subAgents.validation.outputJsonPathRequired')
  }
  if (draft.supportsResume && draft.resumeArgTemplate.trim().length === 0) {
    return t('settings.subAgents.validation.resumeArgTemplateRequired')
  }
  if (
    draft.supportsResume &&
    draft.resumeSessionIdJsonPath.trim().length === 0 &&
    draft.resumeSessionIdRegex.trim().length === 0
  ) {
    return t('settings.subAgents.validation.resumeSessionExtractorRequired')
  }
  return null
}

export interface PresetOverrideState {
  bin: string
  extraArgs: string[]
  timeout: string
}

export function createPresetOverrideState(
  entry: SubAgentProfileEntryWire
): PresetOverrideState {
  const source = entry.definition
  const builtIn = entry.builtInDefaults ?? source
  const baseArgs = builtIn.args ?? []
  const currentArgs = source.args ?? []
  const extraArgs =
    baseArgs.length > 0 && arraysShareHead(currentArgs, baseArgs)
      ? currentArgs.slice(baseArgs.length)
      : currentArgs
  return {
    bin: source.bin ?? '',
    extraArgs: [...extraArgs],
    timeout: source.timeout != null ? String(source.timeout) : ''
  }
}

export function buildPresetOverrideWire(
  entry: SubAgentProfileEntryWire,
  override: PresetOverrideState,
  t: (key: string, vars?: Record<string, string | number>) => string
): BuildCustomResult | BuildFailure {
  const builtIn = entry.builtInDefaults ?? entry.definition
  const timeout = parseOptionalInteger(override.timeout, t)
  if (!timeout.ok) {
    return { ok: false, error: timeout.error }
  }

  const baseArgs = builtIn.args ?? []
  const combinedArgs = [...baseArgs, ...override.extraArgs.filter((entry) => entry.trim().length > 0)]
  return {
    ok: true,
    definition: {
      ...builtIn,
      bin: nullableString(override.bin) ?? builtIn.bin ?? null,
      args: combinedArgs.length > 0 ? combinedArgs : builtIn.args ?? null,
      timeout: timeout.value ?? builtIn.timeout ?? null
    }
  }
}

export function extractPresetExtraArgs(entry: SubAgentProfileEntryWire): string[] {
  const builtIn = entry.builtInDefaults ?? entry.definition
  const baseArgs = builtIn.args ?? []
  const currentArgs = entry.definition.args ?? []
  if (baseArgs.length === 0) return [...currentArgs]
  if (!arraysShareHead(currentArgs, baseArgs)) return [...currentArgs]
  return currentArgs.slice(baseArgs.length)
}

function arraysShareHead(value: readonly string[], head: readonly string[]): boolean {
  if (value.length < head.length) return false
  for (let i = 0; i < head.length; i += 1) {
    if (value[i] !== head[i]) return false
  }
  return true
}

function buildPermissionModeMapping(
  mapping: Record<PermissionModeKey, string>
): Record<string, string> | null {
  const result: Record<string, string> = {}
  for (const key of PERMISSION_MODE_KEYS) {
    const value = mapping[key]?.trim() ?? ''
    if (value.length > 0) {
      result[key] = value
    }
  }
  return Object.keys(result).length > 0 ? result : null
}

function parseOptionalInteger(
  value: string,
  t: (key: string, vars?: Record<string, string | number>) => string
): { ok: true; value: number | null } | { ok: false; error: string } {
  const trimmed = value.trim()
  if (trimmed.length === 0) {
    return { ok: true, value: null }
  }

  const parsed = Number.parseInt(trimmed, 10)
  if (!Number.isFinite(parsed) || Number.isNaN(parsed)) {
    return { ok: false, error: t('settings.subAgents.validation.integerRequired') }
  }

  return { ok: true, value: parsed }
}

function parseJsonObject(
  value: string,
  t: (key: string, vars?: Record<string, string | number>) => string
): { ok: true; value: Record<string, unknown> | null } | { ok: false; error: string } {
  const trimmed = value.trim()
  if (trimmed.length === 0) {
    return { ok: true, value: null }
  }

  try {
    const parsed = JSON.parse(trimmed)
    if (parsed == null || typeof parsed !== 'object' || Array.isArray(parsed)) {
      return { ok: false, error: t('settings.subAgents.validation.sanitizationRulesObject') }
    }
    return { ok: true, value: parsed as Record<string, unknown> }
  } catch {
    return { ok: false, error: t('settings.subAgents.validation.sanitizationRulesJson') }
  }
}

function nullableString(value: string): string | null {
  const trimmed = value.trim()
  return trimmed.length > 0 ? trimmed : null
}
