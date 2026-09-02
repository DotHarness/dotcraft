import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { DesktopPluginHost } from '@dotcraft/plugin'

import { OratorioSettingsSurface as OratorioSettingsPluginSurface } from '../../bundled-plugins/oratorio/src/OratorioSettingsSurface'
import { OratorioView } from '../../bundled-plugins/oratorio/src/OratorioView'
import { consumeOratorioNavigation } from '../../bundled-plugins/oratorio/src/oratorio-navigation'
import { LocaleProvider } from '../contexts/LocaleContext'
import { useToastStore } from '../stores/toastStore'
import { useWorkspaceProjectsStore } from '../stores/workspaceProjectsStore'
import { installDesktopApiMock } from './desktopApiMock'
import { installOratorioTestHost } from './oratorioPluginTestHost'

type OratorioRequest = { method?: string; path: string; body?: any }

let pluginHost: DesktopPluginHost
let requests: OratorioRequest[]

function localProjects() {
  return {
    foregroundWorkspacePath: 'C:\\workspaces\\current',
    foregroundProjectId: 'local-current',
    secondaryLimit: 8,
    projects: [
      { projectId: 'local-other', kind: 'local', path: 'C:\\workspaces\\other', name: 'Other', state: 'cold', running: false, loaded: false, threadCount: 0, threads: [], pinned: false },
      { projectId: 'local-current', kind: 'local', path: 'C:\\workspaces\\current', name: 'Current', state: 'foreground', running: true, loaded: true, threadCount: 0, threads: [], pinned: false }
    ],
    chat: { projectId: 'chat', kind: 'chat', path: 'C:\\workspaces\\chats', name: 'Chats', state: 'cold', running: false, loaded: false, threadCount: 0, threads: [], pinned: false }
  }
}

function serverRequest(request: OratorioRequest): { status: number; data: unknown } {
  requests.push(request)
  if (request.path === '/api/v1/sources/sync-schedules') return { status: 200, data: { schedules: [] } }
  if (request.path === '/api/v1/settings/server-configuration') {
    if (request.method === 'PUT') {
      return { status: 200, data: { configuration: { revision: '2', restartRequired: false, configuration: request.body.configuration }, gitHubInstallationWarnings: [] } }
    }
    return { status: 200, data: { revision: '1', restartRequired: false, configuration: {} } }
  }
  if (request.path === '/api/v1/sources/gitlab/sync-jobs') {
    return { status: 200, data: { jobId: 'job-1', provider: 'gitlab', status: 'succeeded', mode: 'incremental', createdAt: '', updatedAt: '' } }
  }
  if (request.path === '/api/v1/sources/sync-jobs/job-1?provider=gitlab') {
    return { status: 200, data: { jobId: 'job-1', provider: 'gitlab', status: 'succeeded', mode: 'incremental', createdAt: '', updatedAt: '', projectsFailed: 0, issuesImported: 12, reviewTargetsImported: 3 } }
  }
  if (request.path.startsWith('/api/v1/tasks')) return { status: 200, data: { tasks: [], nextCursor: null } }
  return { status: 200, data: {} }
}

describe('Oratorio connect-a-source', () => {
  beforeEach(() => {
    vi.restoreAllMocks()
    useToastStore.setState({ toasts: [] })
    useWorkspaceProjectsStore.getState().reset()
    requests = []
    installDesktopApiMock({
      platform: 'win32',
      settings: { get: vi.fn().mockResolvedValue({ locale: 'en' }) },
      workspace: { getProjects: vi.fn().mockResolvedValue(localProjects()) },
      oratorio: {
        getContext: vi.fn().mockResolvedValue({ provider: 'local', workspacePath: null, connected: true, revision: 1 }),
        onEvent: vi.fn(() => vi.fn()),
        focusRun: vi.fn().mockResolvedValue(undefined),
        request: vi.fn(async (request: OratorioRequest) => serverRequest(request))
      }
    })
    pluginHost = installOratorioTestHost()
  })

  it('connects a GitLab project through the guided flow with one configuration save and a confirmed sync', async () => {
    render(
      <LocaleProvider>
        <OratorioSettingsPluginSurface host={pluginHost} contributionId="oratorio" />
      </LocaleProvider>
    )

    fireEvent.click(await screen.findByRole('button', { name: 'Connect a source' }))
    expect(await screen.findByText('Where does your work live?')).toBeInTheDocument()
    const next = () => fireEvent.click(screen.getByRole('button', { name: 'Next' }))
    expect(screen.getByRole('button', { name: 'Next' })).toBeDisabled()

    fireEvent.click(screen.getByRole('radio', { name: /GitLab/ }))
    fireEvent.change(screen.getByLabelText('Token'), { target: { value: 'token-value' } })
    next()

    expect(await screen.findByText('Which project?')).toBeInTheDocument()
    fireEvent.change(screen.getByRole('textbox', { name: 'Project' }), { target: { value: 'group/demo' } })
    next()

    expect(await screen.findByText('Where is the checkout?')).toBeInTheDocument()
    await waitFor(() => expect(screen.getByRole('radio', { name: /Current/ })).toBeChecked())
    next()

    expect(await screen.findByText('How should Oratorio keep up?')).toBeInTheDocument()
    next()

    expect(await screen.findByText('Ready to connect')).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: 'Connect and sync' }))
    expect(await screen.findByText('Read access confirmed')).toBeInTheDocument()
    expect(screen.getByText('12 issues and 3 merge requests synced from group/demo.')).toBeInTheDocument()

    const save = requests.find((request) => request.method === 'PUT' && request.path === '/api/v1/settings/server-configuration')
    expect(save?.body.detectGitHubInstallations).toBe(false)
    expect(save?.body.configuration.gitLab).toMatchObject({
      enabled: true,
      endpoint: 'https://gitlab.com',
      apiBaseUrl: 'https://gitlab.com/api/v4',
      writesEnabled: false,
      projects: ['group/demo'],
      projectProfiles: [{ instance: 'gitlab.com', projectPath: 'group/demo', tokenKind: 'accessToken', secrets: { token: { configured: true, mode: 'replace', value: 'token-value' } } }]
    })
    expect(save?.body.configuration.dotCraft.repositoryWorkspaceRoutes).toEqual([{ project: 'gitlab:gitlab.com/group/demo', workspacePath: 'C:\\workspaces\\current' }])
    expect(save?.body.configuration.automation.autoReviewRepositories).toEqual(['gitlab:gitlab.com/group/demo'])
    expect(requests.find((request) => request.path === '/api/v1/sources/gitlab/sync-schedule')?.body).toEqual({ enabled: true, intervalSeconds: 900 })
    expect(requests.find((request) => request.path === '/api/v1/sources/gitlab/sync-jobs')?.body).toEqual({ mode: 'incremental', projects: ['group/demo'] })
    expect(useToastStore.getState().toasts.map((toast) => toast.message)).toContain('Configuration saved')
  })

  it('shows the Board onboarding state until a source exists and routes into the wizard', async () => {
    const openSettingsPage = vi.fn()
    pluginHost = installOratorioTestHost({
      navigation: { openMainView() {}, openSettingsPage, async openThread() {}, async openExternal() {}, onOpenUrl() { return () => undefined } }
    })
    render(
      <LocaleProvider>
        <OratorioView host={pluginHost} contributionId="board" />
      </LocaleProvider>
    )

    expect(await screen.findByText('Bring your work onto the Board')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Sync sources' })).toBeDisabled()
    fireEvent.click(screen.getByRole('button', { name: /Connect GitLab/ }))

    expect(openSettingsPage).toHaveBeenCalledWith('oratorio')
    expect(consumeOratorioNavigation()).toEqual({ kind: 'settings', section: 'connect', provider: 'gitlab' })
  })
})
