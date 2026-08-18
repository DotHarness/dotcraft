import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { installDesktopApiMock } from './desktopApiMock'
import { ServersPanel } from '../components/settings/panels/servers/ServersPanel'
import { LocaleProvider } from '../contexts/LocaleContext'
import { useRemoteServersStore } from '../stores/remoteServersStore'
import type { LocalSshConfigInfo, RemoteHost, RemoteStackStatus } from '../../shared/remoteServers'

const sshConfig: LocalSshConfigInfo = {
  sshDir: '/Users/test/.ssh',
  configPath: '/Users/test/.ssh/config',
  configExists: true,
  agentAvailable: true,
  aliases: [
    {
      alias: 'prod',
      hostName: 'prod.example.com',
      user: 'deploy',
      port: '22',
      identityFiles: ['~/.ssh/prod_key']
    }
  ],
  identities: [
    {
      path: '~/.ssh/id_ed25519',
      source: 'default',
      exists: true
    },
    {
      path: '~/.ssh/prod_key',
      source: 'config',
      exists: true,
      hostAliases: ['prod']
    }
  ]
}

function resetRemoteServersStore(): void {
  useRemoteServersStore.setState({
    hosts: [],
    loaded: false,
    loading: false,
    selectedHostId: null,
    testing: {},
    testResults: {},
    discovering: {},
    statuses: {},
    statusLoading: {},
    stackOperations: {},
    activeStack: null,
    sshConfig: null,
    sshConfigLoading: false,
    error: null
  })
}

function renderServersPanel(): ReturnType<typeof render> {
  return render(
    <LocaleProvider>
      <ServersPanel />
    </LocaleProvider>
  )
}

const stackHost: RemoteHost = {
  id: 'h_prod',
  name: 'Prod',
  sshTarget: 'prod',
  stacks: [
    {
      id: 'stack_1',
      name: 'QQBot',
      composeDir: '~/sample-stack/docker',
      appServerPort: 9100,
      dashboardPort: 8080,
      sandboxProfile: false
    }
  ]
}

const runningStatus: RemoteStackStatus = {
  stackId: 'stack_1',
  health: 'running',
  dockerOk: true,
  composeOk: true,
  envOk: true,
  configOk: true,
  tokenPresent: true,
  services: [{ name: 'dotcraft', state: 'running', healthy: true }],
  servicesUp: 1,
  servicesTotal: 1
}

describe('ServersPanel', () => {
  beforeEach(() => {
    resetRemoteServersStore()
    installDesktopApiMock({
      settings: {
        get: vi.fn().mockResolvedValue({ locale: 'en' })
      },
      remoteServers: {
        list: vi.fn<() => Promise<RemoteHost[]>>().mockResolvedValue([]),
        sshConfig: vi.fn<() => Promise<LocalSshConfigInfo>>().mockResolvedValue(sshConfig),
        create: vi.fn(),
        update: vi.fn(),
        delete: vi.fn(),
        test: vi.fn().mockResolvedValue({ reachable: true, dockerOk: true, composeOk: true }),
        discoverStacks: vi.fn().mockResolvedValue([]),
        status: vi.fn(),
        logs: vi.fn(),
        action: vi.fn(),
        openInDesktop: vi.fn(),
        openDashboard: vi.fn(),
        disconnect: vi.fn()
      }
    })
  })

  it('opens Add server as a settings page and shows local SSH choices', async () => {
    renderServersPanel()

    fireEvent.click(await screen.findByRole('button', { name: /add server/i }))

    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
    expect(screen.getByRole('button', { name: /back to remote servers/i })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /^back$/i })).not.toBeInTheDocument()
    expect(screen.getByText('Saved SSH aliases')).toBeInTheDocument()

    await waitFor(() => {
      expect(screen.getByText('prod')).toBeInTheDocument()
      expect(screen.getByText('~/.ssh/id_ed25519')).toBeInTheDocument()
    })
  })

  it('opens Add instance as a settings page instead of a modal', async () => {
    useRemoteServersStore.setState({
      hosts: [
        {
          id: 'h_prod',
          name: 'Prod',
          sshTarget: 'prod',
          stacks: []
        }
      ],
      loaded: true,
      selectedHostId: 'h_prod'
    })

    renderServersPanel()

    fireEvent.click(screen.getAllByRole('button', { name: /add instance/i })[0])

    await waitFor(() => {
      expect(window.api.remoteServers.test).toHaveBeenCalledWith({ id: 'h_prod' })
    })
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
    expect(screen.getByRole('button', { name: /back to prod/i })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /^back$/i })).not.toBeInTheDocument()
    expect(screen.getByText('Register a DotCraft Docker Compose deployment on Prod.')).toBeInTheDocument()
    expect(screen.getByText('Deployment')).toBeInTheDocument()
    expect(screen.getByText('Ports')).toBeInTheDocument()
  })

  it('uses settings breadcrumbs for server detail and edit pages', async () => {
    useRemoteServersStore.setState({
      hosts: [stackHost],
      loaded: true,
      selectedHostId: 'h_prod',
      statuses: { stack_1: runningStatus }
    })

    renderServersPanel()

    await waitFor(() => {
      expect(window.api.remoteServers.test).toHaveBeenCalledWith({ id: 'h_prod' })
    })
    expect(screen.getByRole('button', { name: /back to remote servers/i })).toBeInTheDocument()

    fireEvent.click(screen.getByRole('button', { name: /edit server/i }))
    await waitFor(() => {
      expect(screen.getByRole('button', { name: /back to prod/i })).toBeInTheDocument()
    })

    fireEvent.click(screen.getByRole('button', { name: /back to prod/i }))
    fireEvent.click(screen.getAllByRole('button', { name: /more/i })[0])
    fireEvent.click(screen.getByRole('button', { name: /edit instance/i }))

    await waitFor(() => {
      expect(screen.getByRole('button', { name: /back to prod/i })).toBeInTheDocument()
    })
    expect(screen.queryByRole('button', { name: /^back$/i })).not.toBeInTheDocument()
  })

  it('auto-tests saved servers when the panel opens', async () => {
    const host: RemoteHost = {
      id: 'h_prod',
      name: 'Prod',
      sshTarget: 'prod',
      stacks: []
    }
    window.api.remoteServers.list = vi.fn<() => Promise<RemoteHost[]>>().mockResolvedValue([host])

    renderServersPanel()

    await waitFor(() => {
      expect(window.api.remoteServers.test).toHaveBeenCalledWith({ id: 'h_prod' })
    })
  })

  it('hides online SSH status until the user manually tests the server', async () => {
    const host: RemoteHost = {
      id: 'h_prod',
      name: 'Prod',
      sshTarget: 'prod',
      stacks: []
    }
    useRemoteServersStore.setState({
      hosts: [host],
      loaded: true,
      selectedHostId: 'h_prod'
    })

    renderServersPanel()

    await waitFor(() => {
      expect(window.api.remoteServers.test).toHaveBeenCalledTimes(1)
    })
    await waitFor(() => {
      expect(screen.getByRole('button', { name: /test ssh/i })).not.toBeDisabled()
    })
    expect(screen.queryByText('Online')).not.toBeInTheDocument()

    fireEvent.click(screen.getByRole('button', { name: /test ssh/i }))

    await waitFor(() => {
      expect(window.api.remoteServers.test).toHaveBeenCalledTimes(2)
      expect(screen.getByText('Online')).toBeInTheDocument()
    })
  })

  it('discovers a remote DotCraft instance and fills the Add instance form', async () => {
    useRemoteServersStore.setState({
      hosts: [
        {
          id: 'h_prod',
          name: 'Prod',
          sshTarget: 'prod',
          stacks: []
        }
      ],
      loaded: true,
      selectedHostId: 'h_prod'
    })
    window.api.remoteServers.discoverStacks = vi.fn().mockResolvedValue([
      {
        name: 'demo-stack',
        composeDir: '/srv/sample/demo-stack/deploy',
        workspaceDir: '/srv/sample/demo-stack/deploy/workspace',
        composeProjectName: 'deploy',
        appServerPort: 9100,
        dashboardPort: 8080,
        sandboxProfile: false
      }
    ])

    renderServersPanel()

    await waitFor(() => {
      expect(window.api.remoteServers.test).toHaveBeenCalledWith({ id: 'h_prod' })
    })
    fireEvent.click(screen.getAllByRole('button', { name: /add instance/i })[0])
    fireEvent.click(screen.getByRole('button', { name: /discover/i }))

    await waitFor(() => {
      expect(window.api.remoteServers.discoverStacks).toHaveBeenCalledWith('h_prod')
      expect(screen.getByDisplayValue('demo-stack')).toBeInTheDocument()
      expect(screen.getByDisplayValue('/srv/sample/demo-stack/deploy')).toBeInTheDocument()
      expect(screen.getByDisplayValue('/srv/sample/demo-stack/deploy/workspace')).toBeInTheDocument()
      expect(screen.getByDisplayValue('deploy')).toBeInTheDocument()
    })
  })

  it.each([
    ['start', 'Starting…', 'Starting instance · status will refresh shortly'],
    ['stop', 'Stopping…', 'Stopping instance · Desktop connection will be unavailable'],
    ['update', 'Updating…', 'Updating instance · connection may pause briefly']
  ] as const)('shows %s lifecycle progress without turning Open in Desktop into the busy action', async (
    kind,
    label,
    meta
  ) => {
    window.api.remoteServers.status = vi.fn().mockResolvedValue(runningStatus)
    useRemoteServersStore.setState({
      hosts: [stackHost],
      loaded: true,
      selectedHostId: 'h_prod',
      statuses: { stack_1: { ...runningStatus, imageTag: 'latest' } },
      stackOperations: { stack_1: { kind } }
    })

    renderServersPanel()

    expect(await screen.findByText(label)).toBeInTheDocument()
    expect(screen.getByText(meta)).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /open in desktop/i })).toBeDisabled()
    expect(screen.queryByText('Opening in Desktop…')).not.toBeInTheDocument()
  })

  it('shows connection progress only for Open in Desktop', async () => {
    window.api.remoteServers.status = vi.fn().mockResolvedValue(runningStatus)
    useRemoteServersStore.setState({
      hosts: [stackHost],
      loaded: true,
      selectedHostId: 'h_prod',
      statuses: { stack_1: runningStatus },
      stackOperations: { stack_1: { kind: 'connect' } }
    })

    renderServersPanel()

    expect(await screen.findByText('Opening in Desktop…')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /open in desktop/i })).toBeDisabled()
    expect(screen.queryByText('Updating…')).not.toBeInTheDocument()
  })

  it('shows the real app version without build metadata and never renders latest as a version', async () => {
    const status = { ...runningStatus, appVersion: '0.2.3+abc', imageTag: 'latest' }
    window.api.remoteServers.status = vi.fn().mockResolvedValue(status)
    useRemoteServersStore.setState({
      hosts: [stackHost],
      loaded: true,
      selectedHostId: 'h_prod',
      statuses: { stack_1: status }
    })

    renderServersPanel()

    expect(await screen.findByText('0.2.3')).toBeInTheDocument()
    expect(screen.queryByText('0.2.3+abc')).not.toBeInTheDocument()
    expect(screen.queryByText('latest')).not.toBeInTheDocument()
  })

  it('clears the active stack immediately while disconnect IPC is still running', async () => {
    let resolveDisconnect!: () => void
    window.api.remoteServers.disconnect = vi.fn(() => new Promise<void>((resolve) => {
      resolveDisconnect = resolve
    }))
    useRemoteServersStore.setState({
      activeStack: { hostId: 'h_prod', stackId: 'stack_1' }
    })

    const disconnectPromise = useRemoteServersStore.getState().disconnect('h_prod', 'stack_1')

    expect(useRemoteServersStore.getState().activeStack).toBeNull()
    resolveDisconnect()
    await disconnectPromise
  })
})
