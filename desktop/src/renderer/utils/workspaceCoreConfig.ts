export type WorkspaceDefaultApprovalPolicy = 'default' | 'autoApprove'
export type ConcreteApprovalPolicy = 'prompt' | 'autoApprove'
import {
  findProviderPreference,
  readProviderPreferences,
  toContractModelPreference,
  type ModelPreference
} from '../../shared/modelPreference'
import type { ConfigEdit } from '../stores/configStore'

function normalizeOptionalModel(value: unknown): string | null {
  if (typeof value !== 'string') return null
  const trimmed = value.trim()
  return trimmed && trimmed.toLowerCase() !== 'default' ? trimmed : null
}

function normalizeOptionalString(value: unknown): string | null {
  if (typeof value !== 'string') return null
  const trimmed = value.trim()
  return trimmed || null
}

function getCaseInsensitiveValue(record: Record<string, unknown>, key: string): unknown {
  const expected = key.toLowerCase()
  for (const [candidate, value] of Object.entries(record)) {
    if (candidate.toLowerCase() === expected) return value
  }
  return undefined
}

export function resolveWorkspaceProviderFromConfig(config: Record<string, unknown>): string {
  return normalizeOptionalString(getCaseInsensitiveValue(config, 'ProviderId')) ?? ''
}

export function resolveWorkspaceModelFromConfig(
  config: Record<string, unknown>,
  providerId: string,
  modelOverride?: unknown
): string {
  const override = normalizeOptionalModel(modelOverride)
  if (override) return override

  const normalizedProviderId = providerId.trim()
  const providerPreferences = readProviderPreferences(
    getCaseInsensitiveValue(config, 'ProviderPreferences')
  )
  const preference = findProviderPreference(providerPreferences, normalizedProviderId)
  if (preference) return preference.model

  return 'Default'
}

export function resolveConcreteApprovalPolicyFromWorkspaceDefault(value: unknown): ConcreteApprovalPolicy {
  return value === 'autoApprove' ? 'autoApprove' : 'prompt'
}

/** Any policy shown to a person is one of the two answers; anything else resolves through the workspace default. */
export function resolveVisibleApprovalPolicy(
  value: unknown,
  workspaceDefault: ConcreteApprovalPolicy
): ConcreteApprovalPolicy {
  if (value === 'autoApprove') return 'autoApprove'
  if (value === 'prompt') return 'prompt'
  return workspaceDefault
}

export function resolveConcreteApprovalPolicyFromConfig(config: Record<string, unknown>): ConcreteApprovalPolicy {
  const permissions = getCaseInsensitiveValue(config, 'Permissions')
  if (permissions == null || typeof permissions !== 'object' || Array.isArray(permissions)) {
    return 'prompt'
  }
  const raw = getCaseInsensitiveValue(permissions as Record<string, unknown>, 'DefaultApprovalPolicy')
  return resolveConcreteApprovalPolicyFromWorkspaceDefault(raw)
}

export function providerPreferenceEdit(providerId: string, preference: ModelPreference): ConfigEdit {
  return { keyPath: `ProviderPreferences.${providerId}`, value: toContractModelPreference(preference) }
}
