import {
  cloneSettings,
  normalizeProjectKey,
  projectKeyIsValid,
  validateEndpoint,
  type GitHubInstallationProfile,
  type GitLabProjectProfile,
  type OratorioProjectConfig,
  type OratorioSettingsConfig,
  type SourceProvider
} from './oratorio-settings-model'

export type ConnectStepId = 'source' | 'project' | 'workspace' | 'automation' | 'connect'
export const CONNECT_STEPS: readonly ConnectStepId[] = ['source', 'project', 'workspace', 'automation', 'connect']

export type SchedulePreset = 'off' | '15m' | '1h' | 'custom'
export type KeyMode = 'paste' | 'path'
export type GitLabTokenKind = 'accessToken' | 'personalAccessToken' | 'groupAccessToken'
export type WorkspaceListState = 'ready' | 'loading' | 'empty'
export type ConnectIssueField = 'readOnly' | 'endpoint' | 'credentials' | 'token' | 'projectKey' | 'projectFormat' | 'duplicate' | 'installationId' | 'workspace' | 'customMinutes'

export interface ConnectDraft {
  provider: SourceProvider
  endpoint: string
  github: {
    appId: string
    keyMode: KeyMode
    privateKey: string
    privateKeyPath: string
    /** Empty means the server detects the installation when the configuration is saved. */
    installationId: string
  }
  gitlab: {
    tokenKind: GitLabTokenKind
    token: string
  }
  projectKey: string
  workspacePath: string
  schedule: SchedulePreset
  customMinutes: number
  autoReview: boolean
  allowWrites: boolean
}

export interface ConnectContext {
  settings: OratorioSettingsConfig
  workspaces: WorkspaceListState
  readOnly: boolean
}

export const DOCS_BASE_URL = 'https://www.dotcraft.net'

export function connectDocsUrl(provider: SourceProvider, locale: string): string {
  return `${DOCS_BASE_URL}${locale === 'zh-Hans' ? '/zh' : ''}/features/oratorio/${provider}`
}

export function githubAppConfigured(settings: OratorioSettingsConfig): boolean {
  const { appId, secrets } = settings.github
  return appId.trim().length > 0 && (secrets.privateKey.configured || secrets.privateKeyPath.configured)
}

export function hasConfiguredSource(settings: OratorioSettingsConfig): boolean {
  return settings.projects.length > 0 || githubAppConfigured(settings) || settings.gitlab.profiles.length > 0
}

export function createConnectDraft(settings: OratorioSettingsConfig, provider: SourceProvider): ConnectDraft {
  return {
    provider,
    endpoint: settings[provider].endpoint,
    github: { appId: githubAppConfigured(settings) ? settings.github.appId : '', keyMode: 'paste', privateKey: '', privateKeyPath: '', installationId: '' },
    gitlab: { tokenKind: 'accessToken', token: '' },
    projectKey: '',
    workspacePath: '',
    schedule: '15m',
    customMinutes: 30,
    autoReview: true,
    allowWrites: false
  }
}

export function providerInstance(provider: SourceProvider, endpoint: string): string {
  try {
    const hostname = new URL(endpoint).hostname.toLowerCase()
    return provider === 'github' && hostname === 'api.github.com' ? 'github.com' : hostname
  } catch {
    return provider === 'github' ? 'github.com' : 'gitlab.com'
  }
}

export function canonicalConnectProjectKey(draft: ConnectDraft): string {
  return `${draft.provider}:${providerInstance(draft.provider, draft.endpoint)}/${normalizeProjectKey(draft.projectKey).toLowerCase()}`
}

export function connectStepIssues(step: ConnectStepId, draft: ConnectDraft, context: ConnectContext): ConnectIssueField[] {
  if (context.readOnly) return ['readOnly']
  const issues: ConnectIssueField[] = []
  if (step === 'source') {
    if (validateEndpoint(draft.endpoint)) issues.push('endpoint')
    if (draft.provider === 'github') {
      const configured = githubAppConfigured(context.settings) && draft.github.appId === context.settings.github.appId
      const key = draft.github.keyMode === 'paste' ? draft.github.privateKey : draft.github.privateKeyPath
      if (!configured && (!draft.github.appId.trim() || !key.trim())) issues.push('credentials')
    } else if (!draft.gitlab.token.trim()) {
      issues.push('token')
    }
  }
  if (step === 'project') {
    const key = normalizeProjectKey(draft.projectKey)
    if (!key) issues.push('projectKey')
    else if (!projectKeyIsValid(draft.projectKey)) issues.push('projectFormat')
    else if (context.settings.projects.some((item) => item.provider === draft.provider && item.projectKey.toLowerCase() === key.toLowerCase())) issues.push('duplicate')
    if (draft.provider === 'github' && draft.github.installationId.trim() && !/^\d+$/.test(draft.github.installationId.trim())) issues.push('installationId')
  }
  if (step === 'workspace' && (context.workspaces !== 'ready' || !draft.workspacePath)) issues.push('workspace')
  if (step === 'automation' && draft.schedule === 'custom' && (!Number.isInteger(draft.customMinutes) || draft.customMinutes < 1 || draft.customMinutes > 1440)) issues.push('customMinutes')
  return issues
}

export function scheduleSeconds(draft: ConnectDraft): number | null {
  if (draft.schedule === 'off') return null
  if (draft.schedule === '15m') return 900
  if (draft.schedule === '1h') return 3600
  return draft.customMinutes * 60
}

/** Settings re-links profiles to projects by owner prefix (GitHub) or exact path (GitLab), so deriving from the project path keeps the link across reloads. */
export function deriveConnectProfile(draft: ConnectDraft): GitHubInstallationProfile | GitLabProjectProfile {
  const projectPath = normalizeProjectKey(draft.projectKey)
  const instance = providerInstance(draft.provider, draft.endpoint)
  if (draft.provider === 'github') {
    const owner = projectPath.split('/')[0] ?? ''
    const installationId = draft.github.installationId.trim()
    return { id: `github:${instance}:${owner}`, instance, owner, installationId, source: installationId ? 'manual' : 'detected' }
  }
  const unchanged = { configured: false, mode: 'unchanged' as const, value: null }
  return {
    id: `gitlab:${instance}:${projectPath}`,
    instance,
    projectPath,
    tokenKind: draft.gitlab.tokenKind,
    secrets: { token: { configured: true, mode: 'replace', value: draft.gitlab.token }, webhookSecret: unchanged, webhookSigningToken: unchanged }
  }
}

export function buildConnectTransaction(snapshot: OratorioSettingsConfig, draft: ConnectDraft): { settings: OratorioSettingsConfig; project: OratorioProjectConfig } {
  const settings = cloneSettings(snapshot)
  const projectKey = normalizeProjectKey(draft.projectKey)
  const profile = deriveConnectProfile(draft)
  settings[draft.provider].endpoint = draft.endpoint.trim()
  settings[draft.provider].writesEnabled = draft.allowWrites
  if (draft.provider === 'github') {
    const github = settings.github
    if (draft.github.appId.trim() !== github.appId || draft.github.privateKey.trim() || draft.github.privateKeyPath.trim()) {
      github.appId = draft.github.appId.trim()
      if (draft.github.keyMode === 'paste' && draft.github.privateKey.trim()) github.secrets.privateKey = { configured: true, mode: 'replace', value: draft.github.privateKey }
      if (draft.github.keyMode === 'path' && draft.github.privateKeyPath.trim()) github.secrets.privateKeyPath = { configured: true, mode: 'replace', value: draft.github.privateKeyPath.trim() }
    }
    const githubProfile = profile as GitHubInstallationProfile
    const existing = github.profiles.find((item) => item.id === githubProfile.id)
    if (!existing) github.profiles.push(githubProfile)
    else if (githubProfile.installationId) Object.assign(existing, { installationId: githubProfile.installationId, source: 'manual' })
  } else {
    const gitlab = settings.gitlab
    gitlab.enabled = true
    gitlab.apiBaseUrl = `${draft.endpoint.trim().replace(/\/+$/, '')}/api/v4`
    gitlab.profiles = [...gitlab.profiles.filter((item) => item.id !== profile.id), profile as GitLabProjectProfile]
  }
  const idBase = `${draft.provider}-${projectKey.toLowerCase().replace(/[^a-z0-9]+/g, '-')}`
  let id = idBase
  for (let suffix = 2; settings.projects.some((item) => item.id === id); suffix += 1) id = `${idBase}-${suffix}`
  const project: OratorioProjectConfig = { id, provider: draft.provider, projectKey, workspacePath: draft.workspacePath, profileId: profile.id, enabled: true }
  settings.projects.push(project)
  const canonical = canonicalConnectProjectKey(draft)
  if (draft.autoReview && !settings.autoReview.some((value) => value.toLowerCase() === canonical)) settings.autoReview.push(canonical)
  return { settings, project }
}
