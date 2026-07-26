export type WorkspaceDefaultApprovalPolicy = 'default' | 'autoApprove'
export type ConcreteApprovalPolicy = 'prompt' | 'autoApprove'
import {
  findProviderPreference,
  mergeProviderPreferences,
  readProviderPreferences,
  type ProviderPreferences
} from '../../shared/modelPreference'

export interface WorkspaceCoreConfigLike {
  workspace?: {
    providerId?: string | null
    providerPreferences?: ProviderPreferences | null
    welcomeSuggestionsEnabled?: boolean | null
    defaultApprovalPolicy?: WorkspaceDefaultApprovalPolicy | null
  } | null
  userDefaults?: {
    providerId?: string | null
    providerPreferences?: ProviderPreferences | null
    welcomeSuggestionsEnabled?: boolean | null
    defaultApprovalPolicy?: WorkspaceDefaultApprovalPolicy | null
  } | null
}

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

export function resolveConcreteApprovalPolicyFromConfig(config: Record<string, unknown>): ConcreteApprovalPolicy {
  const permissions = getCaseInsensitiveValue(config, 'Permissions')
  if (permissions == null || typeof permissions !== 'object' || Array.isArray(permissions)) {
    return 'prompt'
  }
  const raw = getCaseInsensitiveValue(permissions as Record<string, unknown>, 'DefaultApprovalPolicy')
  return resolveConcreteApprovalPolicyFromWorkspaceDefault(raw)
}

export function configObjectFromWorkspaceCore(core: WorkspaceCoreConfigLike): Record<string, unknown> {
  const config: Record<string, unknown> = {}
  const providerId = normalizeOptionalString(core.workspace?.providerId ?? core.userDefaults?.providerId)
  if (providerId) {
    config.ProviderId = providerId
  }

  const providerPreferences = mergeProviderPreferences(
    core.userDefaults?.providerPreferences,
    core.workspace?.providerPreferences
  )
  if (Object.keys(providerPreferences).length > 0) {
    config.ProviderPreferences = providerPreferences
  }

  const welcomeSuggestionsEnabled =
    core.workspace?.welcomeSuggestionsEnabled ?? core.userDefaults?.welcomeSuggestionsEnabled
  if (typeof welcomeSuggestionsEnabled === 'boolean') {
    config.WelcomeSuggestions = { Enabled: welcomeSuggestionsEnabled }
  }

  const defaultApprovalPolicy =
    core.workspace?.defaultApprovalPolicy ?? core.userDefaults?.defaultApprovalPolicy
  if (defaultApprovalPolicy === 'default' || defaultApprovalPolicy === 'autoApprove') {
    config.Permissions = { DefaultApprovalPolicy: defaultApprovalPolicy }
  }

  return config
}
