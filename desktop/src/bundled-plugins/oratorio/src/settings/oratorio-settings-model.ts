export type ApprovalPolicy = 'default' | 'autoApprove' | 'interrupt'
export type DeliveryPolicy = 'manualDelivery' | 'autoPr'
export type SourceProvider = 'github' | 'gitlab'
export type ReviewListKey = 'autoReview' | 'draftPublish' | 'followUp'
export type SecretMode = 'unchanged' | 'replace' | 'clear'

export interface SecretConfigurationField {
  configured: boolean
  mode: SecretMode
  value: string | null
}
export interface GitHubInstallationProfile {
  id: string
  instance: string
  owner: string
  installationId: string
  source: 'detected' | 'manual'
}
export interface GitLabProjectProfile {
  id: string
  instance: string
  projectPath: string
  tokenKind: string
  secrets: Record<'token' | 'webhookSecret' | 'webhookSigningToken', SecretConfigurationField>
}

export interface OratorioProjectConfig {
  id: string
  provider: SourceProvider
  projectKey: string
  routeProjectKey?: string
  workspacePath: string
  profileId: string
  enabled: boolean
}

interface ProviderConfigBase {
  endpoint: string
  writesEnabled: boolean
  syncIntervalSeconds: number | null
}

export interface GitHubProviderConfig extends ProviderConfigBase {
  appId: string
  profiles: GitHubInstallationProfile[]
  secrets: Record<'privateKey' | 'privateKeyPath' | 'webhookSecret', SecretConfigurationField>
}

export interface GitLabProviderConfig extends ProviderConfigBase {
  enabled: boolean
  apiBaseUrl: string
  profiles: GitLabProjectProfile[]
}

export interface OratorioSettingsConfig {
  revision: string
  approvalPolicy: ApprovalPolicy
  runTimeoutSeconds: number
  managedWorktreesEnabled: boolean
  worktreeRoot: string
  worktreeBranchPrefix: string
  globalMaxActiveRuns: number
  maxActiveRunsPerRepository: number
  maxActiveRunsPerSource: number
  maxRunAttempts: number
  retryBackoffSeconds: number
  maxRetryBackoffSeconds: number
  stallTimeoutSeconds: number
  succeededWorktreeRetentionHours: number
  failedWorktreeRetentionHours: number
  worktreeCleanupEnabled: boolean
  worktreeCleanupIntervalSeconds: number
  autoDispatchEnabled: boolean
  allowedLabels: string[]
  blockedLabels: string[]
  maxImplementationTurns: number
  deliveryPolicy: DeliveryPolicy
  autoReview: string[]
  draftPublish: string[]
  followUp: string[]
  maxFollowUpRounds: number
  github: GitHubProviderConfig
  gitlab: GitLabProviderConfig
  projects: OratorioProjectConfig[]
}

export function createDefaultOratorioSettings(): OratorioSettingsConfig {
  return {
    revision: '', approvalPolicy: 'interrupt', runTimeoutSeconds: 1800,
    managedWorktreesEnabled: true, worktreeRoot: '', worktreeBranchPrefix: 'oratorio/run', globalMaxActiveRuns: 2,
    maxActiveRunsPerRepository: 1, maxActiveRunsPerSource: 2, maxRunAttempts: 3, retryBackoffSeconds: 10,
    maxRetryBackoffSeconds: 300, stallTimeoutSeconds: 300, succeededWorktreeRetentionHours: 24,
    failedWorktreeRetentionHours: 168, worktreeCleanupEnabled: true, worktreeCleanupIntervalSeconds: 60,
    autoDispatchEnabled: false, allowedLabels: [], blockedLabels: [],
    maxImplementationTurns: 3, deliveryPolicy: 'manualDelivery', autoReview: [], draftPublish: [], followUp: [], maxFollowUpRounds: 5,
    github: {
      endpoint: 'https://api.github.com', appId: '', writesEnabled: false, syncIntervalSeconds: null, profiles: [],
      secrets: {
        privateKey: { configured: false, mode: 'unchanged', value: null },
        privateKeyPath: { configured: false, mode: 'unchanged', value: null },
        webhookSecret: { configured: false, mode: 'unchanged', value: null }
      }
    },
    gitlab: { enabled: false, endpoint: 'https://gitlab.com', apiBaseUrl: 'https://gitlab.com/api/v4', writesEnabled: false, syncIntervalSeconds: null, profiles: [] },
    projects: []
  }
}

export function cloneSettings(value: OratorioSettingsConfig): OratorioSettingsConfig {
  return structuredClone(value)
}

export function validateEndpoint(value: string): string | null {
  try {
    const url = new URL(value)
    return url.protocol === 'https:' || url.protocol === 'http:' ? null : 'endpointProtocol'
  } catch {
    return 'endpointInvalid'
  }
}

export function normalizeProjectKey(value: string): string {
  return value.trim().replace(/^\/+|\/+$/g, '')
}

export function projectKeyIsValid(value: string): boolean {
  const normalized = normalizeProjectKey(value)
  if (normalized.includes('\\') || normalized.includes('..') || normalized.split('/').some((segment) => segment === '.')) return false
  return /^[^/\s]+(?:\/[^/\s]+)+$/.test(normalized)
}
