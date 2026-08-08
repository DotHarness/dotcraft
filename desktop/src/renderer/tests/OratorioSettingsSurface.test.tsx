import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import { OratorioSettingsSurface } from '../components/oratorio/OratorioSettingsSurface'
import { LocaleProvider } from '../contexts/LocaleContext'
import { useToastStore } from '../stores/toastStore'
import { useWorkspaceProjectsStore } from '../stores/workspaceProjectsStore'

describe('OratorioSettingsSurface', () => {
  let requestMock: ReturnType<typeof vi.fn>
  let workspaceProjectsMock: ReturnType<typeof vi.fn>

  beforeEach(() => {
    useToastStore.setState({ toasts: [] })
    useWorkspaceProjectsStore.getState().reset()
    workspaceProjectsMock = vi.fn().mockResolvedValue({
      foregroundWorkspacePath: 'C:\\workspaces\\current',
      foregroundProjectId: 'local-current',
      secondaryLimit: 8,
      projects: [
        { projectId: 'local-other', kind: 'local', path: 'C:\\workspaces\\other', name: 'Other', state: 'cold', running: false, loaded: false, threadCount: 0, threads: [], pinned: false, secondaryFolders: ['C:\\workspaces\\secondary'] },
        { projectId: 'local-current', kind: 'local', path: 'C:\\workspaces\\current', name: 'Current', state: 'foreground', running: true, loaded: true, threadCount: 0, threads: [], pinned: false },
        { projectId: 'remote-project', kind: 'remote', path: 'remote://stack/project', name: 'Remote', state: 'secondary', running: true, loaded: true, threadCount: 0, threads: [], pinned: false }
      ],
      chat: { projectId: 'chat', kind: 'chat', path: 'C:\\workspaces\\chats', name: 'Chats', state: 'cold', running: false, loaded: false, threadCount: 0, threads: [], pinned: false }
    })
    requestMock = vi.fn(async (request: { method?: string; path: string; body?: any }) => {
      if (request.path === '/api/v1/sources/sync-schedules') {
        return { status: 200, data: { schedules: [] } }
      }
      if (request.path === '/api/v1/settings/server-configuration') {
        if (request.method === 'PUT') {
          return {
            status: 200,
            data: {
              configuration: {
                revision: '2',
                restartRequired: false,
                configuration: request.body.configuration
              }
            }
          }
        }
        return {
          status: 200,
          data: {
            revision: '1',
            restartRequired: false,
            configuration: {
              gitHub: {
                repositories: ['example-org/sample-app', 'example-org/sample-service'],
                installationProfiles: [{ instance: 'github.com', owner: 'example-org', installationId: '12345', source: 'manual' }]
              },
              gitLab: { projects: ['example-group/demo-project'] },
              dotCraft: {
                repositoryWorkspaceRoutes: [{
                  project: 'github:github.com/example-org/sample-app',
                  workspacePath: 'C:\\workspaces\\missing'
                }]
              },
              automation: {
                autoDispatchAllowLabels: ['oratorio:auto', ' ORATORIO:AUTO '],
                autoDispatchBlockLabels: ['blocked', ' BLOCKED ', 'on hold']
              }
            }
          }
        }
      }
      return { status: 200, data: {} }
    })
    Object.defineProperty(window, 'api', {
      configurable: true,
      value: {
        platform: 'win32',
        settings: { get: vi.fn().mockResolvedValue({ locale: 'en' }) },
        workspace: { getProjects: workspaceProjectsMock },
        oratorio: {
          getContext: vi.fn().mockResolvedValue({ provider: 'local' }),
          onEvent: vi.fn(() => vi.fn()),
          request: requestMock
        }
      }
    })
  })

  it('renders the simplified root settings view without diagnostics requests', async () => {
    render(
      <LocaleProvider>
        <OratorioSettingsSurface />
      </LocaleProvider>
    )

    expect(await screen.findByText('Source providers')).toBeInTheDocument()
    expect(await screen.findByText('2 Projects')).toBeInTheDocument()
    expect(screen.getByText('1 Project')).toBeInTheDocument()
    expect(screen.queryByText('Diagnostics')).not.toBeInTheDocument()
    expect(screen.queryByText('Concurrency')).not.toBeInTheDocument()
    expect(screen.queryByText('Retry and stall policy')).not.toBeInTheDocument()
    expect(screen.queryByText('Worktree cleanup')).not.toBeInTheDocument()
    expect(screen.getByText('Absolute directory where Oratorio creates managed Worktrees.')).toBeInTheDocument()
    expect(screen.getByPlaceholderText('Use repository default')).toBeInTheDocument()
    expect(screen.getByText('Namespace prepended to branches created for managed runs.')).toBeInTheDocument()
    expect(screen.getByText('Empty means every unblocked eligible item may run.')).toBeInTheDocument()
    expect(screen.getByText('Any matching label prevents implementation auto-dispatch.')).toBeInTheDocument()
    expect(screen.getByText('How completed implementation drafts are delivered.')).toBeInTheDocument()
    expect(screen.getByText('Projects reviewed automatically when PRs / MRs change.')).toBeInTheDocument()
    expect(screen.getByText('Projects where completed review drafts are published automatically.')).toBeInTheDocument()
    expect(screen.getByText('Projects where review feedback starts another implementation round.')).toBeInTheDocument()
    expect(screen.queryByText('No projects selected')).not.toBeInTheDocument()
    expect(screen.getAllByText('oratorio:auto')).toHaveLength(1)
    expect(screen.getAllByText('blocked')).toHaveLength(1)

    const requestedPaths = requestMock.mock.calls.map(([request]) => request.path)
    expect(requestedPaths).toEqual([
      '/api/v1/settings/server-configuration',
      '/api/v1/sources/sync-schedules'
    ])
  })

  it('rolls back a failed label removal and retries through the shared toast', async () => {
    let failNextSave = true
    requestMock.mockImplementation(async (request: { method?: string; path: string; body?: any }) => {
      if (request.path === '/api/v1/sources/sync-schedules') {
        return { status: 200, data: { schedules: [] } }
      }
      if (request.path === '/api/v1/settings/server-configuration' && request.method === 'PUT') {
        if (failNextSave) {
          failNextSave = false
          throw new Error('save failed')
        }
        return {
          status: 200,
          data: {
            configuration: {
              revision: '2',
              restartRequired: false,
              configuration: request.body.configuration
            }
          }
        }
      }
      if (request.path === '/api/v1/settings/server-configuration') {
        return {
          status: 200,
          data: {
            revision: '1',
            restartRequired: false,
            configuration: {
              automation: {
                autoDispatchAllowLabels: ['oratorio:auto'],
                autoDispatchBlockLabels: ['blocked', 'on hold']
              }
            }
          }
        }
      }
      return { status: 200, data: {} }
    })

    render(
      <LocaleProvider>
        <OratorioSettingsSurface />
      </LocaleProvider>
    )

    fireEvent.click(await screen.findByRole('button', { name: 'Remove on hold' }))
    expect(screen.queryByText('on hold')).not.toBeInTheDocument()

    await waitFor(() => {
      expect(useToastStore.getState().toasts).toHaveLength(1)
    }, { timeout: 2000 })
    expect(screen.getByText('on hold')).toBeInTheDocument()
    expect(screen.queryByText('Couldn’t save this change. The last confirmed value was restored.')).not.toBeInTheDocument()

    const toast = useToastStore.getState().toasts[0]
    expect(toast).toMatchObject({
      type: 'error',
      message: 'Couldn’t save this change. The last confirmed value was restored.',
      action: { label: 'Retry' }
    })
    act(() => toast.action?.onClick())

    await waitFor(() => {
      const successfulSave = requestMock.mock.calls
        .map(([request]) => request)
        .filter((request) => request.method === 'PUT' && request.path === '/api/v1/settings/server-configuration')
        .at(-1)
      expect(successfulSave?.body.configuration.automation.autoDispatchBlockLabels).toEqual(['blocked'])
    }, { timeout: 2000 })
    expect(screen.queryByText('on hold')).not.toBeInTheDocument()
  })

  it('adds labels through an inline pill editor and supports cancel', async () => {
    render(
      <LocaleProvider>
        <OratorioSettingsSurface />
      </LocaleProvider>
    )

    await screen.findByText('Dispatch automation')
    const addButtons = screen.getAllByRole('button', { name: 'Add label' })
    fireEvent.click(addButtons[0])

    const input = screen.getByRole('textbox', { name: 'Add label' })
    fireEvent.change(input, { target: { value: 'frontend' } })
    fireEvent.blur(input)
    expect(screen.getByText('frontend')).toBeInTheDocument()

    fireEvent.click(screen.getAllByRole('button', { name: 'Add label' })[0])
    const duplicateInput = screen.getByRole('textbox', { name: 'Add label' })
    fireEvent.change(duplicateInput, { target: { value: 'FRONTEND' } })
    fireEvent.keyDown(duplicateInput, { key: 'Enter' })
    expect(screen.getByRole('alert')).toHaveTextContent('That label is already present.')
    fireEvent.keyDown(duplicateInput, { key: 'Escape' })

    fireEvent.click(screen.getAllByRole('button', { name: 'Add label' })[0])
    const cancelledInput = screen.getByRole('textbox', { name: 'Add label' })
    fireEvent.change(cancelledInput, { target: { value: 'cancelled-label' } })
    fireEvent.keyDown(cancelledInput, { key: 'Escape' })
    expect(screen.queryByText('cancelled-label')).not.toBeInTheDocument()
  })

  it('adds a project with one back action and syncs after saving', async () => {
    render(
      <LocaleProvider>
        <OratorioSettingsSurface />
      </LocaleProvider>
    )

    fireEvent.click(await screen.findByRole('button', { name: 'Add project' }))
    const dialog = screen.getByRole('dialog', { name: 'Add project' })
    expect(dialog).not.toHaveTextContent('Cancel')
    fireEvent.click(screen.getByRole('button', { name: 'Next' }))
    expect(screen.getByRole('button', { name: 'Back' })).toBeInTheDocument()
    const projectInput = screen.getByRole('textbox', { name: 'Project' })
    expect(projectInput).toHaveValue('')
    expect(projectInput).toHaveAttribute('placeholder', 'owner/repository')
    fireEvent.change(projectInput, { target: { value: 'example-org/new-project' } })
    fireEvent.click(screen.getByRole('button', { name: 'Next' }))

    expect(dialog).not.toHaveTextContent('Sync now')
    const workspaceSelect = await screen.findByRole('combobox', { name: 'DotCraft Workspace' })
    expect(workspaceSelect).toHaveTextContent('C:\\workspaces\\current')
    fireEvent.click(workspaceSelect)
    expect(screen.getByRole('option', { name: 'C:\\workspaces\\other' })).toBeInTheDocument()
    expect(screen.queryByText('C:\\workspaces\\secondary')).not.toBeInTheDocument()
    expect(screen.queryByText('remote://stack/project')).not.toBeInTheDocument()
    expect(screen.queryByText('C:\\workspaces\\chats')).not.toBeInTheDocument()
    fireEvent.click(screen.getByRole('option', { name: 'C:\\workspaces\\current' }))
    fireEvent.click(within(dialog).getByRole('button', { name: 'Add project' }))

    await waitFor(() => {
      expect(requestMock).toHaveBeenCalledWith(expect.objectContaining({
        method: 'POST',
        path: '/api/v1/sources/github/sync-jobs'
      }))
    }, { timeout: 2000 })
    const calls = requestMock.mock.calls.map(([request]) => `${request.method ?? 'GET'} ${request.path}`)
    expect(calls.indexOf('PUT /api/v1/settings/server-configuration')).toBeLessThan(calls.indexOf('POST /api/v1/sources/github/sync-jobs'))
  })

  it('blocks project creation when DotCraft has no local Workspace', async () => {
    workspaceProjectsMock.mockResolvedValue({
      foregroundWorkspacePath: '',
      foregroundProjectId: '',
      secondaryLimit: 8,
      projects: [{ projectId: 'remote-project', kind: 'remote', path: 'remote://stack/project', name: 'Remote', state: 'foreground', running: true, loaded: true, threadCount: 0, threads: [], pinned: false }]
    })

    render(
      <LocaleProvider>
        <OratorioSettingsSurface />
      </LocaleProvider>
    )

    fireEvent.click(await screen.findByRole('button', { name: 'Add project' }))
    fireEvent.click(screen.getByRole('button', { name: 'Next' }))
    fireEvent.change(screen.getByRole('textbox', { name: 'Project' }), { target: { value: 'example-org/new-project' } })
    fireEvent.click(screen.getByRole('button', { name: 'Next' }))

    expect(await screen.findByText('Open a local Workspace in DotCraft before adding this project.')).toBeInTheDocument()
    expect(within(screen.getByRole('dialog', { name: 'Add project' })).getByRole('button', { name: 'Add project' })).toBeDisabled()
  })

  it('preserves an unavailable saved Workspace until the project is rebound', async () => {
    render(
      <LocaleProvider>
        <OratorioSettingsSurface />
      </LocaleProvider>
    )

    fireEvent.click(await screen.findByRole('button', { name: 'example-org/sample-app Manage' }))

    const workspaceSelect = await screen.findByRole('combobox', { name: 'DotCraft Workspace' })
    expect(workspaceSelect).toHaveTextContent('C:\\workspaces\\missing')
    fireEvent.click(workspaceSelect)
    expect(screen.getByRole('option', { name: 'C:\\workspaces\\missing · Not in DotCraft Projects' })).toHaveAttribute('aria-disabled', 'true')
    expect(screen.getByRole('option', { name: 'C:\\workspaces\\current' })).toBeInTheDocument()
  })

  it.each([
    { provider: 'GitHub', expectedInstance: 'github.com' },
    { provider: 'GitLab', expectedInstance: 'gitlab.company.test' }
  ])('derives a new $provider profile instance from its configured endpoint', async ({ provider, expectedInstance }) => {
    requestMock.mockImplementation(async (request: { method?: string; path: string; body?: any }) => {
      if (request.path === '/api/v1/sources/sync-schedules') {
        return { status: 200, data: { schedules: [] } }
      }
      if (request.path === '/api/v1/settings/server-configuration') {
        return {
          status: 200,
          data: {
            revision: '1',
            restartRequired: false,
            configuration: {
              gitHub: { endpoint: 'https://api.github.com', repositories: [], installationProfiles: [] },
              gitLab: { endpoint: 'https://gitlab.company.test', projects: [], projectProfiles: [] }
            }
          }
        }
      }
      return { status: 200, data: {} }
    })

    render(
      <LocaleProvider>
        <OratorioSettingsSurface />
      </LocaleProvider>
    )

    fireEvent.click(await screen.findByRole('button', { name: 'Add project' }))
    if (provider === 'GitLab') {
      const providerSelect = screen.getByRole('combobox', { name: 'Source providers' })
      fireEvent.click(providerSelect)
      fireEvent.click(screen.getByRole('option', { name: 'GitLab' }))
    }
    fireEvent.click(screen.getByRole('button', { name: 'Next' }))

    expect(screen.getByDisplayValue(expectedInstance)).toBeInTheDocument()
  })

  it('shows restart-required independently of Diagnostics', async () => {
    requestMock.mockImplementation(async (request: { path: string }) => {
      if (request.path === '/api/v1/sources/sync-schedules') {
        return { status: 200, data: { schedules: [] } }
      }
      return {
        status: 200,
        data: { revision: '1', restartRequired: true, configuration: {} }
      }
    })

    render(
      <LocaleProvider>
        <OratorioSettingsSurface />
      </LocaleProvider>
    )

    expect(await screen.findByText('Restart Oratorio to apply pending runtime configuration.')).toBeInTheDocument()
    expect(screen.queryByText('Diagnostics')).not.toBeInTheDocument()
  })
})
