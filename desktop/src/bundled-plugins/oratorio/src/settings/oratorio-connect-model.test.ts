import { describe, expect, it } from 'vitest'
import {
  buildConnectTransaction,
  connectStepIssues,
  createConnectDraft,
  hasConfiguredSource,
  scheduleSeconds,
  type ConnectContext
} from './oratorio-connect-model'
import { createDefaultOratorioSettings, type OratorioSettingsConfig } from './oratorio-settings-model'

function settingsWithGitHubApp(): OratorioSettingsConfig {
  const settings = createDefaultOratorioSettings()
  settings.github.appId = '184203'
  settings.github.secrets.privateKeyPath = { configured: true, mode: 'unchanged', value: null }
  return settings
}

function context(settings = createDefaultOratorioSettings(), overrides: Partial<ConnectContext> = {}): ConnectContext {
  return { settings, workspaces: 'ready', readOnly: false, ...overrides }
}

describe('Oratorio connect-a-source model', () => {
  it('reports whether any source can produce Board items', () => {
    expect(hasConfiguredSource(createDefaultOratorioSettings())).toBe(false)
    expect(hasConfiguredSource(settingsWithGitHubApp())).toBe(true)
    const withProject = createDefaultOratorioSettings()
    withProject.projects.push({ id: 'gitlab-x', provider: 'gitlab', projectKey: 'group/x', workspacePath: '/w', profileId: '', enabled: true })
    expect(hasConfiguredSource(withProject)).toBe(true)
  })

  it('gates each step on the fields the user still owes', () => {
    const settings = createDefaultOratorioSettings()
    settings.projects.push({ id: 'github-acme-app', provider: 'github', projectKey: 'acme/app', workspacePath: '/w', profileId: '', enabled: true })
    const draft = createConnectDraft(settings, 'github')
    expect(connectStepIssues('source', draft, context(settings))).toEqual(['credentials'])
    draft.github.appId = '1'
    draft.github.privateKey = 'key'
    expect(connectStepIssues('source', draft, context(settings))).toEqual([])
    draft.endpoint = 'not a url'
    expect(connectStepIssues('source', draft, context(settings))).toEqual(['endpoint'])

    draft.projectKey = 'acme'
    expect(connectStepIssues('project', draft, context(settings))).toEqual(['projectFormat'])
    draft.projectKey = 'ACME/App'
    expect(connectStepIssues('project', draft, context(settings))).toEqual(['duplicate'])
    draft.projectKey = 'acme/other'
    draft.github.installationId = 'abc'
    expect(connectStepIssues('project', draft, context(settings))).toEqual(['installationId'])
    draft.github.installationId = ''
    expect(connectStepIssues('project', draft, context(settings))).toEqual([])

    expect(connectStepIssues('workspace', draft, context(settings))).toEqual(['workspace'])
    expect(connectStepIssues('workspace', draft, context(settings, { workspaces: 'empty' }))).toEqual(['workspace'])
    draft.workspacePath = '/w2'
    expect(connectStepIssues('workspace', draft, context(settings))).toEqual([])

    draft.schedule = 'custom'
    draft.customMinutes = 0
    expect(connectStepIssues('automation', draft, context(settings))).toEqual(['customMinutes'])
    expect(connectStepIssues('automation', draft, context(settings, { readOnly: true }))).toEqual(['readOnly'])
  })

  it('accepts an already configured GitHub App without new credentials', () => {
    const settings = settingsWithGitHubApp()
    const draft = createConnectDraft(settings, 'github')
    expect(draft.github.appId).toBe('184203')
    expect(connectStepIssues('source', draft, context(settings))).toEqual([])
  })

  it('maps schedule presets to the sync-schedule interval', () => {
    const draft = createConnectDraft(createDefaultOratorioSettings(), 'gitlab')
    expect(scheduleSeconds({ ...draft, schedule: 'off' })).toBeNull()
    expect(scheduleSeconds({ ...draft, schedule: '15m' })).toBe(900)
    expect(scheduleSeconds({ ...draft, schedule: '1h' })).toBe(3600)
    expect(scheduleSeconds({ ...draft, schedule: 'custom', customMinutes: 45 })).toBe(2700)
  })

  it('derives the GitLab profile from the project path and writes one atomic transaction', () => {
    const settings = createDefaultOratorioSettings()
    const draft = createConnectDraft(settings, 'gitlab')
    Object.assign(draft, { projectKey: '/Group/Sub/Project/', workspacePath: '/workspaces/project', allowWrites: true })
    draft.gitlab.token = 'token-value'
    draft.gitlab.tokenKind = 'personalAccessToken'

    const { settings: next, project } = buildConnectTransaction(settings, draft)

    expect(next.gitlab.enabled).toBe(true)
    expect(next.gitlab.writesEnabled).toBe(true)
    expect(next.gitlab.apiBaseUrl).toBe('https://gitlab.com/api/v4')
    expect(next.gitlab.profiles).toEqual([{
      id: 'gitlab:gitlab.com:Group/Sub/Project',
      instance: 'gitlab.com',
      projectPath: 'Group/Sub/Project',
      tokenKind: 'personalAccessToken',
      secrets: {
        token: { configured: true, mode: 'replace', value: 'token-value' },
        webhookSecret: { configured: false, mode: 'unchanged', value: null },
        webhookSigningToken: { configured: false, mode: 'unchanged', value: null }
      }
    }])
    expect(project).toEqual({ id: 'gitlab-group-sub-project', provider: 'gitlab', projectKey: 'Group/Sub/Project', workspacePath: '/workspaces/project', profileId: 'gitlab:gitlab.com:Group/Sub/Project', enabled: true })
    expect(next.projects).toEqual([project])
    expect(next.autoReview).toEqual(['gitlab:gitlab.com/group/sub/project'])
    expect(settings.projects).toEqual([])
  })

  it('derives a detected GitHub owner profile and replaces credentials only when provided', () => {
    const settings = settingsWithGitHubApp()
    const draft = createConnectDraft(settings, 'github')
    Object.assign(draft, { projectKey: 'acme/example', workspacePath: '/workspaces/example', autoReview: false })

    const { settings: next } = buildConnectTransaction(settings, draft)

    expect(next.github.appId).toBe('184203')
    expect(next.github.secrets.privateKey.mode).toBe('unchanged')
    expect(next.github.secrets.privateKeyPath.mode).toBe('unchanged')
    expect(next.github.profiles).toEqual([{ id: 'github:github.com:acme', instance: 'github.com', owner: 'acme', installationId: '', source: 'detected' }])
    expect(next.autoReview).toEqual([])

    draft.github.installationId = '48213377'
    draft.github.keyMode = 'path'
    draft.github.privateKeyPath = '/keys/app.pem'
    const manual = buildConnectTransaction(settings, draft).settings
    expect(manual.github.profiles[0]).toMatchObject({ installationId: '48213377', source: 'manual' })
    expect(manual.github.secrets.privateKeyPath).toEqual({ configured: true, mode: 'replace', value: '/keys/app.pem' })
  })
})
