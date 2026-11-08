import type { JsonValue } from '@dotcraft/sdk/contracts'

export const PRESET_PROFILE_NAMES = ['native', 'codex-cli', 'cursor-cli'] as const
export type PresetProfileName = (typeof PRESET_PROFILE_NAMES)[number]

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
  sanitizationRules?: JsonValue
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

export interface BuildPresetResult {
  ok: true
  definition: SubAgentProfileWriteWire
}

export interface BuildFailure {
  ok: false
  error: string
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
): BuildPresetResult | BuildFailure {
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

function nullableString(value: string): string | null {
  const trimmed = value.trim()
  return trimmed.length > 0 ? trimmed : null
}
