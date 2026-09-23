import {
  cloneSettings,
  githubInstallationForOwner,
  gitlabProfileForProject,
  normalizeProjectKey,
  projectKeyIsValid,
  projectOwner,
  providerInstance,
  sameProjectKey,
  validateEndpoint,
  withGitHubInstallation,
  withGitLabProfile,
  type GitLabTokenKind,
  type OratorioProjectConfig,
  type OratorioSettingsConfig,
  type SourceProvider
} from './oratorio-settings-model'

export type ConnectStepId = 'source' | 'project' | 'workspace' | 'automation' | 'connect'
export const CONNECT_STEPS: readonly ConnectStepId[] = ['source', 'project', 'workspace', 'automation', 'connect']

export type SchedulePreset = 'off' | '15m' | '1h' | 'custom'
export type KeyMode = 'paste' | 'path'
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
  /** The project this wizard already saved, which its own later steps must not report as a duplicate. */
  connectedProjectKey?: string
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
    const ownProject = context.connectedProjectKey !== undefined && sameProjectKey(context.connectedProjectKey, key)
    if (!key) issues.push('projectKey')
    else if (!projectKeyIsValid(draft.projectKey)) issues.push('projectFormat')
    else if (!ownProject && context.settings.projects.some((item) => item.provider === draft.provider && sameProjectKey(item.projectKey, key))) issues.push('duplicate')
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

/** Detection only runs for an owner the server has no installation for; a typed installation ID wins. */
export function shouldDetectGitHubInstallation(settings: OratorioSettingsConfig, draft: ConnectDraft): boolean {
  return draft.provider === 'github' && !draft.github.installationId.trim() && !githubInstallationForOwner(settings, projectOwner(draft.projectKey))?.installationId
}

/**
 * Upserts the connection so running it again after a save, a failed first sync, or a corrected
 * installation ID never duplicates the project or its profile.
 */
export function buildConnectTransaction(snapshot: OratorioSettingsConfig, draft: ConnectDraft): { settings: OratorioSettingsConfig; project: OratorioProjectConfig } {
  const settings = cloneSettings(snapshot)
  const projectKey = normalizeProjectKey(draft.projectKey)
  settings[draft.provider].endpoint = draft.endpoint.trim()
  settings[draft.provider].writesEnabled = draft.allowWrites
  const instance = providerInstance(draft.provider, draft.endpoint)
  let profileId: string
  if (draft.provider === 'github') {
    const github = settings.github
    if (draft.github.appId.trim() !== github.appId || draft.github.privateKey.trim() || draft.github.privateKeyPath.trim()) {
      github.appId = draft.github.appId.trim()
      if (draft.github.keyMode === 'paste' && draft.github.privateKey.trim()) github.secrets.privateKey = { configured: true, mode: 'replace', value: draft.github.privateKey }
      if (draft.github.keyMode === 'path' && draft.github.privateKeyPath.trim()) github.secrets.privateKeyPath = { configured: true, mode: 'replace', value: draft.github.privateKeyPath.trim() }
    }
    const owner = projectOwner(projectKey)
    const installationId = draft.github.installationId.trim()
    if (installationId) github.profiles = withGitHubInstallation(settings, owner, installationId)
    profileId = githubInstallationForOwner(settings, owner)?.id ?? `github:${instance}:${owner}`
  } else {
    const gitlab = settings.gitlab
    gitlab.enabled = true
    gitlab.apiBaseUrl = `${draft.endpoint.trim().replace(/\/+$/, '')}/api/v4`
    gitlab.profiles = withGitLabProfile(settings, projectKey, (profile) => ({
      ...profile,
      tokenKind: draft.gitlab.tokenKind,
      secrets: { ...profile.secrets, token: { configured: true, mode: 'replace', value: draft.gitlab.token } }
    }))
    profileId = gitlabProfileForProject(settings, projectKey)!.id
  }
  const existing = settings.projects.find((item) => item.provider === draft.provider && sameProjectKey(item.projectKey, projectKey))
  const project: OratorioProjectConfig = existing
    ? Object.assign(existing, { workspacePath: draft.workspacePath, profileId, enabled: true })
    : { id: `${draft.provider}:${projectKey}`, provider: draft.provider, projectKey, workspacePath: draft.workspacePath, profileId, enabled: true }
  if (!existing) settings.projects.push(project)
  const canonical = canonicalConnectProjectKey(draft)
  const reviewed = settings.autoReview.some((value) => value.toLowerCase() === canonical)
  if (draft.autoReview && !reviewed) settings.autoReview.push(canonical)
  if (!draft.autoReview && reviewed) settings.autoReview = settings.autoReview.filter((value) => value.toLowerCase() !== canonical)
  return { settings, project: { ...project } }
}
