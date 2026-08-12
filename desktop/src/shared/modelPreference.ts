export type ModelPreferenceReasoningEffort = 'low' | 'medium' | 'high' | 'extraHigh' | 'ultra'
export type ModelPreferenceReasoningOutput = 'none' | 'summary' | 'full'
export type ModelPreferenceSpeed = 'standard' | 'fast'
export type ModelPreferenceContextMode = 'default' | 'max'

export interface ModelPreference {
  model: string
  reasoning: {
    enabled: boolean
    effort: ModelPreferenceReasoningEffort
    output: ModelPreferenceReasoningOutput
  }
  speed: ModelPreferenceSpeed
  contextWindow: {
    mode: ModelPreferenceContextMode
  }
}

export type ProviderPreferences = Record<string, ModelPreference>

export function toContractProviderPreferences(
  preferences: ProviderPreferences
): Record<string, ContractModelPreference> {
  return Object.fromEntries(
    Object.entries(preferences).map(([providerId, preference]) => [
      providerId,
      {
        model: preference.model,
        reasoning: { ...preference.reasoning },
        speed: preference.speed,
        contextWindow: { ...preference.contextWindow }
      }
    ])
  )
}

export function createManualModelPreference(model: string): ModelPreference {
  return {
    model: model.trim(),
    reasoning: { enabled: false, effort: 'medium', output: 'full' },
    speed: 'standard',
    contextWindow: { mode: 'default' }
  }
}

export function cloneModelPreference(preference: ModelPreference): ModelPreference {
  return {
    model: preference.model,
    reasoning: { ...preference.reasoning },
    speed: preference.speed,
    contextWindow: { ...preference.contextWindow }
  }
}

export function readProviderPreferences(value: unknown): ProviderPreferences {
  if (value == null || typeof value !== 'object' || Array.isArray(value)) return {}
  const result: ProviderPreferences = {}
  for (const [rawProviderId, rawPreference] of Object.entries(value as Record<string, unknown>)) {
    const providerId = rawProviderId.trim()
    const preference = readModelPreference(rawPreference)
    if (providerId && preference) result[providerId] = preference
  }
  return result
}

export function readModelPreference(value: unknown): ModelPreference | null {
  if (value == null || typeof value !== 'object' || Array.isArray(value)) return null
  const record = value as Record<string, unknown>
  const model = readString(record, 'model')
  if (!model || model.toLowerCase() === 'default') return null
  const reasoningRaw = readRecord(record, 'reasoning')
  const enabled = readBoolean(reasoningRaw, 'enabled') ?? false
  const effort = readEnum(
    reasoningRaw,
    'effort',
    ['low', 'medium', 'high', 'extraHigh', 'ultra'] as const
  ) ?? 'medium'
  const output = readEnum(
    reasoningRaw,
    'output',
    ['none', 'summary', 'full'] as const
  ) ?? 'full'
  const speed = readEnum(record, 'speed', ['standard', 'fast'] as const) ?? 'standard'
  const contextWindow = readRecord(record, 'contextWindow')
  const mode = readEnum(contextWindow, 'mode', ['default', 'max'] as const) ?? 'default'
  return {
    model,
    reasoning: { enabled, effort, output },
    speed,
    contextWindow: { mode }
  }
}

export function findProviderPreference(
  preferences: ProviderPreferences,
  providerId: string
): ModelPreference | null {
  const entry = Object.entries(preferences).find(
    ([candidate]) => candidate.trim().toLowerCase() === providerId.trim().toLowerCase()
  )
  return entry ? cloneModelPreference(entry[1]) : null
}

export function setProviderPreference(
  preferences: ProviderPreferences,
  providerId: string,
  preference: ModelPreference | null
): ProviderPreferences {
  const result = Object.fromEntries(
    Object.entries(preferences).map(([key, value]) => [key, cloneModelPreference(value)])
  )
  const existing = Object.keys(result).find(
    (candidate) => candidate.toLowerCase() === providerId.trim().toLowerCase()
  )
  if (existing) delete result[existing]
  if (preference && providerId.trim()) {
    result[providerId.trim()] = cloneModelPreference(preference)
  }
  return result
}

export function mergeProviderPreferences(
  userDefaults: unknown,
  workspace: unknown
): ProviderPreferences {
  let result: ProviderPreferences = {}
  for (const [providerId, preference] of Object.entries(readProviderPreferences(userDefaults))) {
    result = setProviderPreference(result, providerId, preference)
  }
  for (const [providerId, preference] of Object.entries(readProviderPreferences(workspace))) {
    result = setProviderPreference(result, providerId, preference)
  }
  return result
}

function findValue(record: Record<string, unknown>, key: string): unknown {
  const expected = key.toLowerCase()
  return Object.entries(record).find(([candidate]) => candidate.toLowerCase() === expected)?.[1]
}

function readRecord(record: Record<string, unknown>, key: string): Record<string, unknown> {
  const value = findValue(record, key)
  return value != null && typeof value === 'object' && !Array.isArray(value)
    ? value as Record<string, unknown>
    : {}
}

function readString(record: Record<string, unknown>, key: string): string | null {
  const value = findValue(record, key)
  if (typeof value !== 'string') return null
  return value.trim() || null
}

function readBoolean(record: Record<string, unknown>, key: string): boolean | null {
  const value = findValue(record, key)
  return typeof value === 'boolean' ? value : null
}

function readEnum<const T extends readonly string[]>(
  record: Record<string, unknown>,
  key: string,
  values: T
): T[number] | null {
  const value = readString(record, key)
  if (!value) return null
  return values.find((candidate) => candidate.toLowerCase() === value.toLowerCase()) ?? null
}
import type { ModelPreference as ContractModelPreference } from '@dotcraft/sdk/contracts'
