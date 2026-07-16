export type WorkspaceDefaultApprovalPolicy = 'default' | 'autoApprove'
export type ConcreteApprovalPolicy = 'prompt' | 'autoApprove'
export type WorkspaceContextWindowMode = 'default' | 'max'
export type WorkspaceInferenceSpeed = 'standard' | 'fast'

export interface WorkspaceCoreConfigLike {
  workspace?: {
    providerId?: string | null
    providerModels?: Record<string, string> | null
    welcomeSuggestionsEnabled?: boolean | null
    defaultApprovalPolicy?: WorkspaceDefaultApprovalPolicy | null
    contextWindowMode?: WorkspaceContextWindowMode | null
    speed?: WorkspaceInferenceSpeed | null
  } | null
  userDefaults?: {
    providerId?: string | null
    providerModels?: Record<string, string> | null
    welcomeSuggestionsEnabled?: boolean | null
    defaultApprovalPolicy?: WorkspaceDefaultApprovalPolicy | null
    contextWindowMode?: WorkspaceContextWindowMode | null
    speed?: WorkspaceInferenceSpeed | null
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

function normalizeContextWindowMode(value: unknown): WorkspaceContextWindowMode | null {
  if (typeof value !== 'string') return null
  const normalized = value.trim().toLowerCase()
  if (normalized === 'max' || normalized === 'maximum') return 'max'
  if (normalized === 'default') return 'default'
  return null
}

function getCaseInsensitiveValue(record: Record<string, unknown>, key: string): unknown {
  const expected = key.toLowerCase()
  for (const [candidate, value] of Object.entries(record)) {
    if (candidate.toLowerCase() === expected) return value
  }
  return undefined
}

function mergeProviderModels(
  userDefaults: unknown,
  workspace: unknown
): Record<string, string> {
  const result: Record<string, string> = {}
  const apply = (source: unknown): void => {
    if (source == null || typeof source !== 'object' || Array.isArray(source)) return
    for (const [rawProviderId, rawModel] of Object.entries(source as Record<string, unknown>)) {
      const providerId = rawProviderId.trim()
      if (!providerId) continue
      const existingKey = Object.keys(result).find(
        (candidate) => candidate.toLowerCase() === providerId.toLowerCase()
      )
      if (existingKey) delete result[existingKey]
      const model = normalizeOptionalModel(rawModel)
      if (!model) continue
      result[providerId] = model
    }
  }

  apply(userDefaults)
  apply(workspace)
  return result
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
  const providerModels = getCaseInsensitiveValue(config, 'ProviderModels')
  if (normalizedProviderId && providerModels != null && typeof providerModels === 'object' && !Array.isArray(providerModels)) {
    const remembered = Object.entries(providerModels as Record<string, unknown>)
      .find(([candidate]) => candidate.trim().toLowerCase() === normalizedProviderId.toLowerCase())?.[1]
    const providerModel = normalizeOptionalModel(remembered)
    if (providerModel) return providerModel
  }

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

  const providerModels = mergeProviderModels(
    core.userDefaults?.providerModels,
    core.workspace?.providerModels
  )
  if (Object.keys(providerModels).length > 0) {
    config.ProviderModels = providerModels
  }

  const speed = core.workspace?.speed ?? core.userDefaults?.speed
  if (speed === 'standard' || speed === 'fast') config.Speed = speed

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

  const contextWindowMode = normalizeContextWindowMode(
    core.workspace?.contextWindowMode ?? core.userDefaults?.contextWindowMode
  )
  if (contextWindowMode != null) {
    config.Compaction = {
      ContextWindowMode: contextWindowMode === 'max' ? 'Max' : 'Default'
    }
  }

  return config
}
