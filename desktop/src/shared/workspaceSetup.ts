import type { ModelPreference } from './modelPreference'
import type { DesktopProviderProtocol } from './providerProtocols'

export type WorkspaceSetupState = 'no-workspace' | 'needs-setup' | 'ready'
export type WorkspaceBootstrapProfile = 'default' | 'developer' | 'personal-assistant'
export type WorkspaceSetupProviderProtocol = DesktopProviderProtocol
export type WorkspaceSetupProviderMode = 'existing' | 'create' | 'skip'
export type WorkspaceSetupBootstrapImportSourceId = 'codex' | 'claude'
export type ProviderAuthMethod = 'apiKey' | 'chatgptOAuth'

export interface WorkspaceSetupBootstrapImportSource {
  id: WorkspaceSetupBootstrapImportSourceId
  fileName: 'AGENTS.md' | 'CLAUDE.md'
  path: string
  relativePath: string
}

export interface WorkspaceSetupProviderSummary {
  id: string
  displayName: string
  protocol: WorkspaceSetupProviderProtocol
  hasApiKey: boolean
  endPoint: string
  networkTimeoutSeconds?: number | null
  authMethod?: ProviderAuthMethod
  chatGptAccountId?: string | null
  chatGptPlanType?: string | null
}

export interface WorkspaceUserConfigDefaults {
  providerId?: string
  model?: string
  preference?: ModelPreference
}

export interface RemoteWorkspaceStatusPayload {
  source?: 'servers' | 'manual' | 'cli'
  projectId?: string
  displayName?: string
  endpoint?: string
  hostId?: string
  stackId?: string
  serverName?: string
  stackName?: string
  workspaceDir?: string
  appServerWorkspacePath?: string
  composeDir?: string
  projectName?: string
}

export interface WorkspaceStatusPayload {
  status: WorkspaceSetupState
  workspacePath: string
  hasUserConfig: boolean
  userConfigDefaults?: WorkspaceUserConfigDefaults
  providers: WorkspaceSetupProviderSummary[]
  bootstrapImportSources?: WorkspaceSetupBootstrapImportSource[]
  remote?: RemoteWorkspaceStatusPayload
}

export interface WorkspaceSetupProviderDraft {
  id: string
  displayName: string
  protocol: WorkspaceSetupProviderProtocol
  apiKey: string
  endPoint: string
  networkTimeoutSeconds?: number | null
  authMethod?: ProviderAuthMethod
  chatGptAccountId?: string | null
  chatGptPlanType?: string | null
}

export interface WorkspaceSetupRequest {
  model: string
  preference: ModelPreference
  profile: WorkspaceBootstrapProfile
  providerMode: WorkspaceSetupProviderMode
  providerId?: string
  provider?: WorkspaceSetupProviderDraft
  setAsUserDefault: boolean
  bootstrapImportSourceId?: WorkspaceSetupBootstrapImportSourceId | null
}

export interface WorkspaceSetupBootstrapImportResult {
  sourceId: WorkspaceSetupBootstrapImportSourceId
  status: 'success' | 'failed'
  warning?: string
}

export interface WorkspaceSetupRunResult {
  bootstrapImport?: WorkspaceSetupBootstrapImportResult
}

export type WorkspaceSetupModelListRequest =
  | { providerId: string }
  | { provider: WorkspaceSetupProviderDraft }

export type WorkspaceSetupModelListResult =
  | { kind: 'success'; models: WorkspaceSetupModelCatalogItem[] }
  | { kind: 'auth-required' }
  | { kind: 'unsupported' }
  | { kind: 'missing-key' }
  | { kind: 'error'; retryable?: boolean }

export interface WorkspaceSetupModelCatalogItem {
  id: string
  ownedBy?: string
  createdAt?: string
  reasoning?: {
    supportsDisable: boolean
    supportedEfforts: Array<{
      effort: 'low' | 'medium' | 'high' | 'extraHigh' | 'ultra'
      label: string
      description: string
    }>
    defaultEffort: 'low' | 'medium' | 'high' | 'extraHigh' | 'ultra'
    supportedOutputs: Array<'none' | 'summary' | 'full'>
    defaultOutput: 'none' | 'summary' | 'full'
  } | null
  speed?: {
    supportedModes: Array<'standard' | 'fast'>
    defaultMode: 'standard' | 'fast'
  } | null
  contextWindow?: {
    catalogWindow: number
    configuredWindow: number
    supportsMax: boolean
    maxWindow: number
  } | null
}
