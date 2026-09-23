export type ApprovalPolicy = 'autoApprove' | 'deny'
export type DeliveryPolicy = 'manualDelivery' | 'autoPr'
export type SourceProvider = 'github' | 'gitlab'
export type ReviewListKey = 'autoReview' | 'draftPublish' | 'followUp'
export type SecretMode = 'unchanged' | 'replace' | 'clear'
export type GitLabTokenKind = 'accessToken' | 'personalAccessToken' | 'groupAccessToken'
export type GitLabProjectSecretKey = 'webhookSecret' | 'webhookSigningToken'
export type GitHubAppSecretKey = 'privateKey' | 'privateKeyPath' | 'webhookSecret'

export const GITLAB_TOKEN_KINDS: readonly GitLabTokenKind[] = ['accessToken', 'personalAccessToken', 'groupAccessToken']

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
  secrets: Record<GitHubAppSecretKey, SecretConfigurationField>
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
    revision: '', approvalPolicy: 'deny', runTimeoutSeconds: 1800,
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

/** Mirrors the server's instance resolution so derived profiles match the endpoint they are saved under. */
export function providerInstance(provider: SourceProvider, endpoint: string): string {
  try {
    const hostname = new URL(endpoint).hostname.toLowerCase()
    return provider === 'github' && hostname === 'api.github.com' ? 'github.com' : hostname
  } catch {
    return provider === 'github' ? 'github.com' : 'gitlab.com'
  }
}

export function sameProjectKey(left: string, right: string): boolean {
  return normalizeProjectKey(left).toLowerCase() === normalizeProjectKey(right).toLowerCase()
}

export function projectOwner(projectKey: string): string {
  return normalizeProjectKey(projectKey).split('/')[0] ?? ''
}

export function gitlabProfileForProject(settings: OratorioSettingsConfig, projectKey: string): GitLabProjectProfile | undefined {
  return settings.gitlab.profiles.find((profile) => sameProjectKey(profile.projectPath, projectKey))
}

export function githubInstallationForOwner(settings: OratorioSettingsConfig, owner: string): GitHubInstallationProfile | undefined {
  const instance = providerInstance('github', settings.github.endpoint)
  return settings.github.profiles.find((profile) => profile.instance === instance && profile.owner.toLowerCase() === owner.toLowerCase())
}

/** GitLab credentials are one profile per project, so edits upsert the profile keyed by the project path. */
export function withGitLabProfile(settings: OratorioSettingsConfig, projectKey: string, update: (profile: GitLabProjectProfile) => GitLabProjectProfile): GitLabProjectProfile[] {
  const existing = gitlabProfileForProject(settings, projectKey)
  if (existing) return settings.gitlab.profiles.map((profile) => profile === existing ? update(structuredClone(profile)) : profile)
  const instance = providerInstance('gitlab', settings.gitlab.endpoint)
  const projectPath = normalizeProjectKey(projectKey)
  const unchanged = (): SecretConfigurationField => ({ configured: false, mode: 'unchanged', value: null })
  return [...settings.gitlab.profiles, update({ id: `gitlab:${instance}:${projectPath}`, instance, projectPath, tokenKind: 'accessToken', secrets: { token: unchanged(), webhookSecret: unchanged(), webhookSigningToken: unchanged() } })]
}

export function withGitHubInstallation(settings: OratorioSettingsConfig, owner: string, installationId: string): GitHubInstallationProfile[] {
  const existing = githubInstallationForOwner(settings, owner)
  if (existing) return settings.github.profiles.map((profile) => profile === existing ? { ...profile, installationId, source: 'manual' } : profile)
  const instance = providerInstance('github', settings.github.endpoint)
  return [...settings.github.profiles, { id: `github:${instance}:${owner}`, instance, owner, installationId, source: 'manual' }]
}

export function projectKeyIsValid(value: string): boolean {
  const normalized = normalizeProjectKey(value)
  if (normalized.includes('\\') || normalized.includes('..') || normalized.split('/').some((segment) => segment === '.')) return false
  return /^[^/\s]+(?:\/[^/\s]+)+$/.test(normalized)
}
