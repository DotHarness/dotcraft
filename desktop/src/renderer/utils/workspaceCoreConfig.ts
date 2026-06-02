export type VisibleApprovalPolicy = 'default' | 'autoApprove'

export interface WorkspaceCoreConfigLike {
  workspace?: {
    model?: string | null
    welcomeSuggestionsEnabled?: boolean | null
    defaultApprovalPolicy?: VisibleApprovalPolicy | null
  } | null
  userDefaults?: {
    model?: string | null
    welcomeSuggestionsEnabled?: boolean | null
    defaultApprovalPolicy?: VisibleApprovalPolicy | null
  } | null
}

function normalizeOptionalModel(value: unknown): string | null {
  if (typeof value !== 'string') return null
  const trimmed = value.trim()
  return trimmed && trimmed !== 'Default' ? trimmed : null
}

export function configObjectFromWorkspaceCore(core: WorkspaceCoreConfigLike): Record<string, unknown> {
  const config: Record<string, unknown> = {}
  const model = normalizeOptionalModel(core.workspace?.model ?? core.userDefaults?.model)
  if (model) {
    config.Model = model
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
