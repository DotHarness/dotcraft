import { findProviderPreference, readProviderPreferences, type ModelPreference } from '../../../shared/modelPreference'
import type { InferenceSpeedWire, ModelCatalogItem } from '../../stores/modelCatalogStore'
import { DEFAULT_REASONING_CONFIG, readReasoningObject, type ResolvedReasoningConfig } from './modelReasoning'

export function setCaseInsensitiveField(target: Record<string, unknown>, key: string, value: unknown): void {
  const existingKey = Object.keys(target).find(candidate => candidate.toLowerCase() === key.toLowerCase())
  target[existingKey ?? key] = value
}

export function deleteCaseInsensitiveField(target: Record<string, unknown>, key: string): void {
  const existingKey = Object.keys(target).find(candidate => candidate.toLowerCase() === key.toLowerCase())
  if (existingKey) delete target[existingKey]
}

export function readWorkspacePreference(config: Record<string, unknown>, providerId: string): ModelPreference | null {
  const key = Object.keys(config).find(candidate => candidate.toLowerCase() === 'providerpreferences')
  return findProviderPreference(readProviderPreferences(key ? config[key] : null), providerId)
}

export function applyPreferenceToThreadConfig(config: Record<string, unknown>, preference: ModelPreference): void {
  config.model = preference.model
  config.reasoning = { ...preference.reasoning }
  config.speed = preference.speed
}

export function readThreadSpeed(config: Record<string, unknown>): InferenceSpeedWire {
  const key = Object.keys(config).find(candidate => candidate.toLowerCase() === 'speed')
  return key && config[key] === 'fast' ? 'fast' : 'standard'
}

export function applyModelCompatibility(config: Record<string, unknown>, model: ModelCatalogItem | null): void {
  const reasoningKey = Object.keys(config).find(key => key.toLowerCase() === 'reasoning')
  if (reasoningKey && config[reasoningKey] && typeof config[reasoningKey] === 'object' && model?.reasoning) {
    const current = readReasoningObject(config[reasoningKey]) ?? DEFAULT_REASONING_CONFIG
    const effortSupported = model.reasoning.supportedEfforts.some(option => option.effort === current.effort)
    if ((!current.enabled && !model.reasoning.supportsDisable) || (current.enabled && !effortSupported)) {
      config[reasoningKey] = {
        enabled: true,
        effort: model.reasoning.defaultEffort,
        output: model.reasoning.supportedOutputs.includes(current.output) ? current.output : model.reasoning.defaultOutput
      }
    }
  }
}

export function resolveReasoningFromConfiguration(config: Record<string, unknown>): ResolvedReasoningConfig {
  const key = Object.keys(config).find(candidate => candidate.toLowerCase() === 'reasoning')
  return readReasoningObject(key ? config[key] : null) ?? DEFAULT_REASONING_CONFIG
}
