export type WorkspaceDefaultApprovalPolicy = 'default' | 'autoApprove'
export type ConcreteApprovalPolicy = 'prompt' | 'autoApprove'
export type WorkspaceContextWindowMode = 'default' | 'max'
export type WorkspaceInferenceSpeed = 'standard' | 'fast'

export interface WorkspaceCoreConfigLike {
  workspace?: {
    model?: string | null
    welcomeSuggestionsEnabled?: boolean | null
    defaultApprovalPolicy?: WorkspaceDefaultApprovalPolicy | null
    contextWindowMode?: WorkspaceContextWindowMode | null
    speed?: WorkspaceInferenceSpeed | null
  } | null
  userDefaults?: {
    model?: string | null
    welcomeSuggestionsEnabled?: boolean | null
    defaultApprovalPolicy?: WorkspaceDefaultApprovalPolicy | null
    contextWindowMode?: WorkspaceContextWindowMode | null
    speed?: WorkspaceInferenceSpeed | null
  } | null
}

function normalizeOptionalModel(value: unknown): string | null {
  if (typeof value !== 'string') return null
  const trimmed = value.trim()
  return trimmed && trimmed !== 'Default' ? trimmed : null
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
  const model = normalizeOptionalModel(core.workspace?.model ?? core.userDefaults?.model)
  if (model) {
    config.Model = model
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
