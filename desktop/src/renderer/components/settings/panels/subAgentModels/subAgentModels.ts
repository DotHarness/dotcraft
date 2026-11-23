import {
  cloneModelPreference,
  readModelPreference,
  type ModelPreference
} from '../../../../../shared/modelPreference'
import type { ProviderInfoWire } from '../../providerInfo'
import { providerUsable } from '../../providers/providerDraft'

export interface SubAgentModelPreferenceConfig {
  ProviderId?: string
  Model: string
  Reasoning: {
    Enabled: boolean
    Effort: ModelPreference['reasoning']['effort']
    Output: ModelPreference['reasoning']['output']
  }
  Speed: ModelPreference['speed']
}

export const MAX_SUBAGENT_MODEL_CHOICES = 8

export interface SubAgentModelChoice {
  providerId: string
  model: string
}

export interface SubAgentModelSelection {
  providerId: string
  preference: ModelPreference
}

export type SubAgentSelections = Record<string, SubAgentModelSelection>

export type ProviderTrouble = 'missing' | 'signedOut'

export function providerTrouble(providerId: string, providers: ProviderInfoWire[]): ProviderTrouble | null {
  const provider = findProvider(providers, providerId)
  if (!provider) return 'missing'
  return providerUsable(provider) ? null : 'signedOut'
}

export function findProvider(providers: ProviderInfoWire[], providerId: string): ProviderInfoWire | undefined {
  const key = providerId.trim().toLowerCase()
  return providers.find((provider) => provider.id.toLowerCase() === key)
}

export function sameProvider(a: string, b: string): boolean {
  return a.trim().toLowerCase() === b.trim().toLowerCase()
}

export function readSubAgentModelChoices(value: unknown): SubAgentModelChoice[] {
  if (!Array.isArray(value)) return []
  return value.flatMap((item): SubAgentModelChoice[] => {
    if (item == null || typeof item !== 'object') return []
    const record = item as Record<string, unknown>
    const providerId = readString(record, 'providerId')
    const model = readString(record, 'model')
    return providerId && model ? [{ providerId, model }] : []
  })
}

export function toSubAgentModelChoicesConfig(choices: SubAgentModelChoice[]): Array<{ ProviderId: string; Model: string }> {
  return choices.map((choice) => ({ ProviderId: choice.providerId, Model: choice.model }))
}

export function hasSubAgentModelChoice(choices: SubAgentModelChoice[], candidate: SubAgentModelChoice): boolean {
  return choices.some((choice) =>
    sameProvider(choice.providerId, candidate.providerId)
    && choice.model.toLowerCase() === candidate.model.trim().toLowerCase())
}

export function readSubAgentSelection(value: unknown, mainProviderId: string): SubAgentModelSelection | null {
  const key = mainProviderId.trim()
  const preference = readModelPreference(value)
  if (!key || !preference) return null
  const providerId = value != null && typeof value === 'object'
    ? readString(value as Record<string, unknown>, 'providerId')
    : ''
  return { providerId: providerId || key, preference }
}

export function readSubAgentSelections(value: unknown): SubAgentSelections {
  if (value == null || typeof value !== 'object' || Array.isArray(value)) return {}
  const result: SubAgentSelections = {}
  for (const [rawKey, raw] of Object.entries(value as Record<string, unknown>)) {
    const selection = readSubAgentSelection(raw, rawKey)
    if (selection) result[rawKey.trim()] = selection
  }
  return result
}

export function findSubAgentSelection(selections: SubAgentSelections, mainProviderId: string): SubAgentModelSelection | null {
  const entry = Object.entries(selections).find(([key]) => sameProvider(key, mainProviderId))
  return entry ? { providerId: entry[1].providerId, preference: cloneModelPreference(entry[1].preference) } : null
}

export function toSubAgentSelectionConfig(
  mainProviderId: string,
  selection: SubAgentModelSelection
): SubAgentModelPreferenceConfig {
  const { model, reasoning, speed } = selection.preference
  const value: SubAgentModelPreferenceConfig = {
    Model: model,
    Reasoning: {
      Enabled: reasoning.enabled,
      Effort: reasoning.effort,
      Output: reasoning.output
    },
    Speed: speed
  }
  if (!sameProvider(mainProviderId, selection.providerId)) value.ProviderId = selection.providerId
  return value
}

function readString(record: Record<string, unknown>, key: string): string {
  const expected = key.toLowerCase()
  const value = Object.entries(record).find(([candidate]) => candidate.toLowerCase() === expected)?.[1]
  return typeof value === 'string' ? value.trim() : ''
}
